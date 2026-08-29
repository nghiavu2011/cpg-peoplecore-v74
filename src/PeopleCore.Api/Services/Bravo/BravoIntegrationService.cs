using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services.Bravo;

public sealed class BravoIntegrationService(
    PeopleCoreDbContext db,
    IAccessControlService access,
    ICurrentUser current,
    IAuditService audit,
    IBravoAdapter adapter,
    IConfiguration configuration,
    IHttpContextAccessor http)
{
    public const string IntegrationName = "BRAVO";
    public const string CompensationType = "COMPENSATION_APPROVED_V1";
    public const string ProjectCodeType = "PROJECT_CODE_V1";
    public const string SchemaVersion = "1.0";

    public async Task<IntegrationMessage> QueueApprovedCompensationAsync(ApprovedCompensationHandoffRequest request, CancellationToken ct)
    {
        if (request.EmployeeId == Guid.Empty) throw new InvalidOperationException("EMPLOYEE_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.ApprovalReference)) throw new InvalidOperationException("APPROVAL_REFERENCE_REQUIRED");
        if (request.Components is null || request.Components.Count == 0) throw new InvalidOperationException("COMPENSATION_COMPONENTS_REQUIRED");
        if (request.Components.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Currency)))
            throw new InvalidOperationException("INVALID_COMPENSATION_COMPONENT");
        if (request.Components.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException("DUPLICATE_COMPENSATION_COMPONENT");

        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == request.EmployeeId, ct)
            ?? throw new KeyNotFoundException("EMPLOYEE_NOT_FOUND");
        if (!await access.CanHandleCompensationAsync(employee, ct))
            throw new UnauthorizedAccessException("COMPENSATION_SCOPE_DENIED");

        var components = request.Components
            .Select(x => new CompensationComponentDto(x.Code.Trim().ToUpperInvariant(), x.Amount, x.Currency.Trim().ToUpperInvariant()))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Currency, StringComparer.Ordinal)
            .ToArray();

        var canonical = new
        {
            schemaVersion = SchemaVersion,
            employeeId = employee.Id,
            staffCode = employee.StaffCode,
            effectiveFrom = request.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            approvalReference = request.ApprovalReference.Trim(),
            components
        };
        var payload = JsonSerializer.Serialize(canonical);
        var payloadHash = Sha256(payload);
        var idem = Sha256($"{IntegrationName}|OUT|{CompensationType}|{employee.Id:N}|{request.EffectiveFrom:yyyy-MM-dd}|{request.ApprovalReference.Trim()}|{payloadHash}");

        var sameApproval = await db.CompensationHandoffs.AsNoTracking().SingleOrDefaultAsync(x =>
            x.EmployeeId == employee.Id && x.EffectiveFrom == request.EffectiveFrom && x.ApprovalReference == request.ApprovalReference.Trim(), ct);
        if (sameApproval is not null)
        {
            if (!string.Equals(sameApproval.ApprovedPayloadSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("APPROVAL_REFERENCE_CONFLICT");
            var prior = await db.IntegrationMessages.AsNoTracking().SingleAsync(x => x.Id == sameApproval.IntegrationMessageId, ct);
            return prior;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.IntegrationMessages.SingleOrDefaultAsync(x => x.IdempotencyKey == idem, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
            await tx.RollbackAsync(ct);
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var message = new IntegrationMessage
        {
            Id = Guid.NewGuid(), Direction = "OUT", Integration = IntegrationName, MessageType = CompensationType,
            IdempotencyKey = idem, PayloadJson = payload, PayloadSha256 = payloadHash, SchemaVersion = SchemaVersion,
            CorrelationId = CorrelationId(), ExternalReference = request.ApprovalReference.Trim(), Status = "PENDING",
            AttemptCount = 0, CreatedAt = now
        };
        db.IntegrationMessages.Add(message);
        db.CompensationHandoffs.Add(new CompensationHandoff
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, EffectiveFrom = request.EffectiveFrom,
            ApprovalReference = request.ApprovalReference.Trim(), ApprovedPayloadSha256 = payloadHash,
            IntegrationMessageId = message.Id, Status = "QUEUED", CreatedBy = current.StaffCode ?? "SYSTEM", CreatedAt = now
        });
        audit.Record("BRAVO_COMPENSATION_QUEUED", "IntegrationMessage", message.Id.ToString(), new
        {
            employeeId = employee.Id, employee.StaffCode, request.EffectiveFrom, request.ApprovalReference,
            payloadSha256 = payloadHash, idempotencyKey = idem
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return message;
    }

    public async Task<IntegrationMessage> AcceptProjectCodeBatchAsync(BravoProjectCodeBatchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SourceBatchReference)) throw new InvalidOperationException("SOURCE_BATCH_REFERENCE_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.SourceRevision)) throw new InvalidOperationException("SOURCE_REVISION_REQUIRED");
        if (request.Projects is null || request.Projects.Count == 0) throw new InvalidOperationException("PROJECT_CODES_REQUIRED");
        if (request.Projects.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name)))
            throw new InvalidOperationException("INVALID_PROJECT_CODE");
        if (request.Projects.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException("DUPLICATE_PROJECT_CODE_IN_BATCH");

        var projects = request.Projects.Select(x => new ProjectCodeDto(
            x.Code.Trim().ToUpperInvariant(), x.Name.Trim(), string.IsNullOrWhiteSpace(x.ParentCode) ? null : x.ParentCode.Trim().ToUpperInvariant(),
            NormalizeProjectStatus(x.Status), x.ValidFrom, x.ValidTo)).OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
        var canonical = new { schemaVersion = SchemaVersion, sourceBatchReference = request.SourceBatchReference.Trim(), sourceRevision = request.SourceRevision.Trim(), projects };
        var payload = JsonSerializer.Serialize(canonical);
        var hash = Sha256(payload);
        var idem = $"BRAVO:IN:{ProjectCodeType}:{request.SourceBatchReference.Trim()}";

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.IntegrationMessages.SingleOrDefaultAsync(x => x.IdempotencyKey == idem, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadSha256, hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
            await tx.RollbackAsync(ct);
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var message = new IntegrationMessage
        {
            Id = Guid.NewGuid(), Direction = "IN", Integration = IntegrationName, MessageType = ProjectCodeType,
            IdempotencyKey = idem, PayloadJson = payload, PayloadSha256 = hash, SchemaVersion = SchemaVersion,
            CorrelationId = CorrelationId(), ExternalReference = request.SourceBatchReference.Trim(), Status = "PROCESSING",
            AttemptCount = 1, CreatedAt = now, LastAttemptAt = now
        };
        db.IntegrationMessages.Add(message);
        await db.SaveChangesAsync(ct);

        foreach (var dto in projects)
        {
            if (dto.ValidFrom is not null && dto.ValidTo is not null && dto.ValidTo < dto.ValidFrom)
                throw new InvalidOperationException($"PROJECT_CODE_DATE_RANGE_INVALID:{dto.Code}");
            var row = await db.ProjectCodes.SingleOrDefaultAsync(x => x.Code == dto.Code, ct);
            if (row is null)
            {
                db.ProjectCodes.Add(new ProjectCode
                {
                    Code = dto.Code, Name = dto.Name, ParentCode = dto.ParentCode, Status = dto.Status,
                    ValidFrom = dto.ValidFrom, ValidTo = dto.ValidTo, SourceSystem = IntegrationName,
                    SourceRevision = request.SourceRevision.Trim(), LastSourceMessageId = message.Id, SyncedAt = now
                });
            }
            else
            {
                row.Name = dto.Name; row.ParentCode = dto.ParentCode; row.Status = dto.Status;
                row.ValidFrom = dto.ValidFrom; row.ValidTo = dto.ValidTo; row.SourceSystem = IntegrationName;
                row.SourceRevision = request.SourceRevision.Trim(); row.LastSourceMessageId = message.Id; row.SyncedAt = now;
            }
        }
        message.Status = "PROCESSED";
        message.ProcessedAt = now;
        audit.Record("BRAVO_PROJECT_CODES_IMPORTED", "IntegrationMessage", message.Id.ToString(), new
        {
            request.SourceBatchReference, request.SourceRevision, rowCount = projects.Length, payloadSha256 = hash
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return message;
    }

    public async Task<IntegrationMessage> AttemptOutboundAsync(Guid messageId, CancellationToken ct)
    {
        var message = await db.IntegrationMessages.SingleOrDefaultAsync(x => x.Id == messageId && x.Direction == "OUT" && x.Integration == IntegrationName, ct)
            ?? throw new KeyNotFoundException("OUTBOX_MESSAGE_NOT_FOUND");
        if (message.Status == "SENT") return message;
        if (message.MessageType != CompensationType) throw new InvalidOperationException("UNSUPPORTED_OUTBOX_MESSAGE_TYPE");

        var now = DateTimeOffset.UtcNow;
        message.AttemptCount += 1;
        message.LastAttemptAt = now;
        var result = await adapter.PushApprovedCompensationAsync(message.PayloadJson, message.IdempotencyKey, ct);
        if (result.Accepted)
        {
            message.Status = "SENT"; message.ProcessedAt = now; message.ExternalReference = result.Reference ?? message.ExternalReference; message.LastError = null;
            var h = await db.CompensationHandoffs.SingleOrDefaultAsync(x => x.IntegrationMessageId == message.Id, ct);
            if (h is not null) h.Status = "SENT";
        }
        else
        {
            var maxAttempts = Math.Max(1, configuration.GetValue("Bravo:MaxDeliveryAttempts", 8));
            message.LastError = result.Status;
            if (message.AttemptCount >= maxAttempts)
            {
                message.Status = "REVIEW";
                message.NextAttemptAt = null;
                var h = await db.CompensationHandoffs.SingleOrDefaultAsync(x => x.IntegrationMessageId == message.Id, ct);
                if (h is not null) h.Status = "REVIEW";
            }
            else
            {
                message.Status = "RETRY";
                message.NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(message.AttemptCount, 5))));
            }
        }
        audit.Record("BRAVO_OUTBOX_ATTEMPT", "IntegrationMessage", message.Id.ToString(), new
        {
            message.MessageType, message.Status, message.AttemptCount, adapterStatus = result.Status
        });
        await db.SaveChangesAsync(ct);
        return message;
    }

    public Task<List<ProjectCode>> ListProjectCodesAsync(bool activeOnly, CancellationToken ct) =>
        db.ProjectCodes.AsNoTracking()
            .Where(x => !activeOnly || x.Status == "ACTIVE")
            .OrderBy(x => x.Code).Take(5000).ToListAsync(ct);

    public Task<List<IntegrationMessage>> ListMessagesAsync(string? direction, string? status, CancellationToken ct)
    {
        var q = db.IntegrationMessages.AsNoTracking().Where(x => x.Integration == IntegrationName);
        if (!string.IsNullOrWhiteSpace(direction)) q = q.Where(x => x.Direction == direction.ToUpper());
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status.ToUpper());
        return q.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
    }

    private string? CorrelationId() => http.HttpContext?.Items.TryGetValue(PeopleCore.Api.Runtime.CorrelationIdMiddleware.ItemKey, out var value) == true ? value?.ToString() : null;
    private static string NormalizeProjectStatus(string? status) => string.Equals(status?.Trim(), "INACTIVE", StringComparison.OrdinalIgnoreCase) ? "INACTIVE" : "ACTIVE";
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
