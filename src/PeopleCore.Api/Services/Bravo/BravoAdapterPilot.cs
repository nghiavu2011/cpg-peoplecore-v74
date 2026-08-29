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

public static class V68BravoModes
{
    public const string MappingNotConfigured = "NOT_CONFIGURED";
    public const string MappingCanonicalFixtureOnly = "CANONICAL_FIXTURE_ONLY";
    public const string TransportNotConfigured = "NOT_CONFIGURED";
    public const string TransportSignedDryRun = "SIGNED_DRY_RUN";
    public const string PilotProfile = "V68_CANONICAL_FIXTURE_UNAPPROVED";
}

public sealed record BravoMappingStatus(string Mode, bool NativeBravoSpecificationConfirmed, string Profile, string Message);
public sealed record BravoTransportStatus(string Mode, bool LiveDeliveryEnabled, bool SecretReady, string Message);
public sealed record BravoAdapterPilotStatus(
    BravoMappingStatus Mapping,
    BravoTransportStatus Transport,
    bool OfficialPayrollResultImportEnabled,
    string OfficialPayrollResultSource,
    bool ShadowPayrollValidationOnly,
    string ProductionState);

public sealed record BravoMappedDocument(
    string Direction,
    string MessageType,
    string SchemaVersion,
    string MappingMode,
    string MappingProfile,
    bool NativeBravoFormat,
    string BodyJson,
    string SourcePayloadSha256,
    string MappedPayloadSha256);

public sealed record BravoTransportPreviewReceipt(
    string Status,
    string TransportMode,
    string ProtocolVersion,
    string EnvelopeSha256,
    string SignatureHex,
    string Nonce,
    DateTimeOffset CreatedAtUtc,
    bool Delivered);

public sealed record BravoAdapterFixturePreviewRequest(string Direction, string MessageType, JsonElement Payload);
public sealed record BravoAdapterPreviewResponse(
    Guid EvidenceId,
    Guid? IntegrationMessageId,
    string Direction,
    string MessageType,
    string MappingMode,
    string MappingProfile,
    bool NativeBravoFormat,
    string SourcePayloadSha256,
    string MappedPayloadSha256,
    string TransportMode,
    string Status,
    string EnvelopeSha256,
    string SignatureHex,
    string Nonce,
    DateTimeOffset CreatedAtUtc,
    bool Delivered);

public interface IBravoPayloadMapper
{
    BravoMappingStatus GetStatus();
    BravoMappedDocument MapOutbound(IntegrationMessage message);
    BravoMappedDocument MapFixture(BravoAdapterFixturePreviewRequest request);
}

public sealed class NotConfiguredBravoPayloadMapper : IBravoPayloadMapper
{
    public BravoMappingStatus GetStatus() => new(
        V68BravoModes.MappingNotConfigured,
        NativeBravoSpecificationConfirmed: false,
        Profile: "NONE",
        Message: "Native BRAVO field mapping has not been confirmed by Finance/BRAVO. No native mapping is performed.");

    public BravoMappedDocument MapOutbound(IntegrationMessage message) => throw new InvalidOperationException("BRAVO_MAPPING_NOT_CONFIGURED");
    public BravoMappedDocument MapFixture(BravoAdapterFixturePreviewRequest request) => throw new InvalidOperationException("BRAVO_MAPPING_NOT_CONFIGURED");
}

public sealed class CanonicalFixtureBravoPayloadMapper : IBravoPayloadMapper
{
    public BravoMappingStatus GetStatus() => new(
        V68BravoModes.MappingCanonicalFixtureOnly,
        NativeBravoSpecificationConfirmed: false,
        Profile: V68BravoModes.PilotProfile,
        Message: "Pilot-only canonical pass-through fixture. It validates the adapter pipeline but is NOT a native BRAVO schema.");

    public BravoMappedDocument MapOutbound(IntegrationMessage message)
    {
        if (message.Direction != "OUT" || message.Integration != BravoIntegrationService.IntegrationName)
            throw new InvalidOperationException("BRAVO_MAPPING_MESSAGE_UNSUPPORTED");
        if (message.MessageType != BravoIntegrationService.CompensationType)
            throw new InvalidOperationException("BRAVO_MAPPING_MESSAGE_UNSUPPORTED");
        var actual = Hash(message.PayloadJson);
        if (!string.Equals(actual, message.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BRAVO_SOURCE_PAYLOAD_HASH_MISMATCH");
        var normalized = NormalizeJson(message.PayloadJson);
        return Build(message.Direction, message.MessageType, message.SchemaVersion, normalized, message.PayloadSha256);
    }

    public BravoMappedDocument MapFixture(BravoAdapterFixturePreviewRequest request)
    {
        var direction = (request.Direction ?? string.Empty).Trim().ToUpperInvariant();
        var type = (request.MessageType ?? string.Empty).Trim().ToUpperInvariant();
        var supported = (direction == "IN" && type == BravoIntegrationService.ProjectCodeType)
            || (direction == "OUT" && type == BravoIntegrationService.CompensationType);
        if (!supported) throw new InvalidOperationException("BRAVO_FIXTURE_MESSAGE_UNSUPPORTED");
        if (request.Payload.ValueKind is not JsonValueKind.Object)
            throw new InvalidOperationException("BRAVO_FIXTURE_JSON_OBJECT_REQUIRED");
        var raw = request.Payload.GetRawText();
        var sourceHash = Hash(raw);
        return Build(direction, type, BravoIntegrationService.SchemaVersion, NormalizeJson(raw), sourceHash);
    }

    private static BravoMappedDocument Build(string direction, string type, string schemaVersion, string body, string sourceHash) => new(
        direction,
        type,
        schemaVersion,
        V68BravoModes.MappingCanonicalFixtureOnly,
        V68BravoModes.PilotProfile,
        NativeBravoFormat: false,
        BodyJson: body,
        SourcePayloadSha256: sourceHash,
        MappedPayloadSha256: Hash(body));

    private static string NormalizeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public interface IBravoMachineTransport
{
    BravoTransportStatus GetStatus();
    Task<BravoTransportPreviewReceipt> PreviewAsync(Guid? integrationMessageId, BravoMappedDocument document, CancellationToken ct = default);
}

public sealed class NotConfiguredBravoMachineTransport : IBravoMachineTransport
{
    public BravoTransportStatus GetStatus() => new(
        V68BravoModes.TransportNotConfigured,
        LiveDeliveryEnabled: false,
        SecretReady: false,
        Message: "Live BRAVO transport is intentionally NOT_CONFIGURED pending an approved BRAVO machine interface.");

    public Task<BravoTransportPreviewReceipt> PreviewAsync(Guid? integrationMessageId, BravoMappedDocument document, CancellationToken ct = default)
        => throw new InvalidOperationException("BRAVO_TRANSPORT_NOT_CONFIGURED");
}

public sealed class SignedDryRunBravoMachineTransport(IConfiguration configuration) : IBravoMachineTransport
{
    public BravoTransportStatus GetStatus()
    {
        var path = configuration["Bravo:TransportPilot:SigningKeyFile"];
        var ready = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        return new(
            V68BravoModes.TransportSignedDryRun,
            LiveDeliveryEnabled: false,
            SecretReady: ready,
            Message: ready
                ? "Signed dry-run only. HMAC evidence is produced; no network delivery occurs."
                : "Signed dry-run selected but the mounted signing key is unavailable.");
    }

    public Task<BravoTransportPreviewReceipt> PreviewAsync(Guid? integrationMessageId, BravoMappedDocument document, CancellationToken ct = default)
    {
        var key = ReadSigningKey();
        var now = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var protocol = "PEOPLECORE-V68-DRYRUN-1";
        var messageId = integrationMessageId?.ToString("N") ?? "fixture";
        var canonicalEnvelope = string.Join('|', new[]
        {
            protocol,
            messageId,
            document.Direction,
            document.MessageType,
            document.SchemaVersion,
            document.MappingProfile,
            document.MappedPayloadSha256,
            now.ToString("O"),
            nonce
        });
        var envelopeHash = Hash(canonicalEnvelope);
        using var hmac = new HMACSHA256(key);
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalEnvelope))).ToLowerInvariant();
        return Task.FromResult(new BravoTransportPreviewReceipt(
            Status: "DRY_RUN_SIGNED",
            TransportMode: V68BravoModes.TransportSignedDryRun,
            ProtocolVersion: protocol,
            EnvelopeSha256: envelopeHash,
            SignatureHex: signature,
            Nonce: nonce,
            CreatedAtUtc: now,
            Delivered: false));
    }

    private byte[] ReadSigningKey()
    {
        var path = configuration["Bravo:TransportPilot:SigningKeyFile"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("BRAVO_TRANSPORT_SIGNING_KEY_MISSING");
        var raw = File.ReadAllText(path).Trim();
        if (raw.Length >= 64 && raw.All(Uri.IsHexDigit))
        {
            var bytes = Convert.FromHexString(raw);
            if (bytes.Length >= 32) return bytes;
        }
        var utf8 = Encoding.UTF8.GetBytes(raw);
        if (utf8.Length < 32) throw new InvalidOperationException("BRAVO_TRANSPORT_SIGNING_KEY_TOO_SHORT");
        return utf8;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class BravoTransportConfigurationValidator
{
    public static void ValidateOrThrow(IConfiguration configuration, IHostEnvironment environment)
    {
        var mappingMode = (configuration["Bravo:MappingMode"] ?? V68BravoModes.MappingNotConfigured).ToUpperInvariant();
        var transportMode = (configuration["Bravo:TransportPilot:Mode"] ?? V68BravoModes.TransportNotConfigured).ToUpperInvariant();
        var live = configuration.GetValue<bool>("Bravo:TransportPilot:LiveDeliveryEnabled");
        if (live) throw new InvalidOperationException("V68 does not permit live BRAVO delivery.");
        if (environment.IsProduction() && transportMode != V68BravoModes.TransportNotConfigured)
            throw new InvalidOperationException("Production BRAVO transport must remain NOT_CONFIGURED until separately approved.");
        if (transportMode == V68BravoModes.TransportSignedDryRun && !environment.IsEnvironment("Pilot"))
            throw new InvalidOperationException("SIGNED_DRY_RUN BRAVO transport is Pilot-only.");
        if (mappingMode != V68BravoModes.MappingNotConfigured && mappingMode != V68BravoModes.MappingCanonicalFixtureOnly)
            throw new InvalidOperationException("Unsupported V68 BRAVO mapping mode.");
        if (transportMode != V68BravoModes.TransportNotConfigured && transportMode != V68BravoModes.TransportSignedDryRun)
            throw new InvalidOperationException("Unsupported V68 BRAVO transport mode.");
        if (transportMode == V68BravoModes.TransportSignedDryRun)
        {
            var path = configuration["Bravo:TransportPilot:SigningKeyFile"];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("SIGNED_DRY_RUN requires a mounted BRAVO transport signing key file.");
        }
    }
}

public sealed class BravoAdapterPilotService(
    PeopleCoreDbContext db,
    IBravoPayloadMapper mapper,
    IBravoMachineTransport transport,
    ICurrentUser current,
    IAuditService audit,
    IHttpContextAccessor http,
    IConfiguration configuration)
{
    public BravoAdapterPilotStatus GetStatus() => new(
        mapper.GetStatus(),
        transport.GetStatus(),
        OfficialPayrollResultImportEnabled: false,
        OfficialPayrollResultSource: configuration["Payroll:OfficialResultSource"] ?? "BRAVO",
        ShadowPayrollValidationOnly: !configuration.GetValue<bool>("Payroll:ShadowEngineEnabled"),
        ProductionState: "NOT_LIVE");

    public async Task<BravoAdapterPreviewResponse> PreviewOutboundAsync(Guid messageId, CancellationToken ct)
    {
        var message = await db.IntegrationMessages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == messageId && x.Integration == BravoIntegrationService.IntegrationName && x.Direction == "OUT", ct)
            ?? throw new KeyNotFoundException("BRAVO_OUTBOX_MESSAGE_NOT_FOUND");
        var mapped = mapper.MapOutbound(message);
        return await PersistPreviewAsync(message.Id, mapped, ct);
    }

    public async Task<BravoAdapterPreviewResponse> PreviewFixtureAsync(BravoAdapterFixturePreviewRequest request, CancellationToken ct)
    {
        var mapped = mapper.MapFixture(request);
        return await PersistPreviewAsync(null, mapped, ct);
    }

    public Task<List<BravoTransportEvidence>> ListEvidenceAsync(Guid? integrationMessageId, CancellationToken ct)
    {
        var q = db.BravoTransportEvidence.AsNoTracking().AsQueryable();
        if (integrationMessageId is not null) q = q.Where(x => x.IntegrationMessageId == integrationMessageId);
        return q.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
    }

    private async Task<BravoAdapterPreviewResponse> PersistPreviewAsync(Guid? integrationMessageId, BravoMappedDocument mapped, CancellationToken ct)
    {
        var receipt = await transport.PreviewAsync(integrationMessageId, mapped, ct);
        if (receipt.Delivered) throw new InvalidOperationException("V68_DRY_RUN_MUST_NOT_DELIVER");
        var evidence = new BravoTransportEvidence
        {
            Id = Guid.NewGuid(),
            IntegrationMessageId = integrationMessageId,
            Direction = mapped.Direction,
            MessageType = mapped.MessageType,
            MappingMode = mapped.MappingMode,
            MappingProfile = mapped.MappingProfile,
            SourcePayloadSha256 = mapped.SourcePayloadSha256,
            MappedPayloadSha256 = mapped.MappedPayloadSha256,
            TransportMode = receipt.TransportMode,
            EnvelopeSha256 = receipt.EnvelopeSha256,
            SignatureHex = receipt.SignatureHex,
            Nonce = receipt.Nonce,
            Status = receipt.Status,
            CorrelationId = CorrelationId(),
            CreatedBy = current.StaffCode ?? "SYSTEM",
            CreatedAt = receipt.CreatedAtUtc
        };
        db.BravoTransportEvidence.Add(evidence);
        audit.Record("BRAVO_V68_TRANSPORT_PREVIEW", "BravoTransportEvidence", evidence.Id.ToString(), new
        {
            evidence.IntegrationMessageId,
            evidence.Direction,
            evidence.MessageType,
            evidence.MappingMode,
            evidence.MappingProfile,
            evidence.TransportMode,
            evidence.Status,
            evidence.SourcePayloadSha256,
            evidence.MappedPayloadSha256,
            evidence.EnvelopeSha256
        });
        await db.SaveChangesAsync(ct);
        return new BravoAdapterPreviewResponse(
            evidence.Id, evidence.IntegrationMessageId, evidence.Direction, evidence.MessageType,
            evidence.MappingMode, evidence.MappingProfile, NativeBravoFormat: false,
            evidence.SourcePayloadSha256, evidence.MappedPayloadSha256, evidence.TransportMode,
            evidence.Status, evidence.EnvelopeSha256, evidence.SignatureHex, evidence.Nonce,
            evidence.CreatedAt, Delivered: false);
    }

    private string? CorrelationId() => http.HttpContext?.Items.TryGetValue(PeopleCore.Api.Runtime.CorrelationIdMiddleware.ItemKey, out var value) == true ? value?.ToString() : null;
}
