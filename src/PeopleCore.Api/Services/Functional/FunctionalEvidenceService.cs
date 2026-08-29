using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Runtime;
using PeopleCore.Api.Security;

namespace PeopleCore.Api.Services.Functional;

public sealed class FunctionalEvidenceService(PeopleCoreDbContext db, ICurrentUser current, IHttpContextAccessor http)
{
    public string Record(string scenarioCode, string domain, string action, Guid? employeeId, Guid? relatedEntityId, string status, object? safeMetadata = null)
    {
        var json = JsonSerializer.Serialize(safeMetadata ?? new { });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var id = Guid.NewGuid();
        db.FunctionalEvidence.Add(new FunctionalEvidence
        {
            Id = id,
            ScenarioCode = scenarioCode.Trim().ToUpperInvariant(),
            Domain = domain.Trim().ToUpperInvariant(),
            Action = action.Trim().ToUpperInvariant(),
            EmployeeId = employeeId,
            RelatedEntityId = relatedEntityId,
            Status = status.Trim().ToUpperInvariant(),
            PayloadSha256 = hash,
            CorrelationId = http.HttpContext?.Items[CorrelationIdMiddleware.ItemKey]?.ToString(),
            CreatedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED",
            CreatedAt = DateTimeOffset.UtcNow
        });
        return $"FUNC:{id:D}";
    }

    public async Task<FunctionalEvidence?> ResolveAsync(string reference, CancellationToken ct = default)
    {
        if (!TryReference(reference, "FUNC", out var id)) return null;
        return await db.FunctionalEvidence.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<EvidenceArtifact?> ResolveArtifactAsync(string reference, CancellationToken ct = default)
    {
        if (!TryReference(reference, "ARTIFACT", out var id)) return null;
        return await db.EvidenceArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public static bool TryReference(string? reference, string prefix, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var raw = reference.Trim();
        var expected = prefix + ":";
        return raw.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(raw[expected.Length..], out id);
    }

    public static FunctionalEvidenceDto ToDto(FunctionalEvidence x) => new(x.Id, x.ScenarioCode, x.Domain, x.Action, x.EmployeeId, x.RelatedEntityId, x.Status, x.PayloadSha256, x.CorrelationId, x.CreatedBy, x.CreatedAt);
}

public sealed class EvidenceArtifactService(PeopleCoreDbContext db, ICurrentUser current)
{
    public async Task<EvidenceArtifactDto> RegisterAsync(RegisterEvidenceArtifactRequest request, CancellationToken ct = default)
    {
        var type = Clean(request.ArtifactType, 120).ToUpperInvariant();
        var hash = Clean(request.Sha256, 64).ToLowerInvariant();
        var result = Clean(request.Result, 20).ToUpperInvariant();
        var storage = Clean(request.StorageReference, 600);
        if (type.Length == 0 || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)) || storage.Length == 0) throw new InvalidOperationException("EVIDENCE_ARTIFACT_FIELDS_INVALID");
        if (result is not ("PASS" or "FAIL" or "BLOCKED")) throw new InvalidOperationException("EVIDENCE_ARTIFACT_RESULT_INVALID");
        if (request.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw new InvalidOperationException("EVIDENCE_ARTIFACT_FUTURE_TIME_INVALID");
        var artifact = new EvidenceArtifact
        {
            Id = Guid.NewGuid(), ArtifactType = type, Sha256 = hash, StorageReference = storage, Result = result,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : Clean(request.CorrelationId, 128),
            ObservedAt = request.ObservedAt, RecordedAt = DateTimeOffset.UtcNow, RecordedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED"
        };
        db.EvidenceArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct);
        return ToDto(artifact);
    }

    public async Task<EvidenceArtifactDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var x = await db.EvidenceArtifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        return x is null ? null : ToDto(x);
    }

    private static EvidenceArtifactDto ToDto(EvidenceArtifact x) => new(x.Id, x.ArtifactType, x.Sha256, x.StorageReference, x.Result, x.CorrelationId, x.ObservedAt, x.RecordedAt, x.RecordedBy);
    private static string Clean(string? value, int max) { var x = (value ?? string.Empty).Trim(); return x.Length <= max ? x : x[..max]; }
}
