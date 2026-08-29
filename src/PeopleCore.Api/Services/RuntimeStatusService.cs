using PeopleCore.Api.Runtime;
using PeopleCore.Api.Security;

namespace PeopleCore.Api.Services;

public sealed class RuntimeStatusService(IConfiguration c, IHostEnvironment env, DatabaseConnectionInfo db, IHttpContextAccessor http)
{
    public object GetSafeStatus()
    {
        var trial = env.IsEnvironment("Trial") && c.GetValue<bool>("TrialAuth:Enabled");
        return new
        {
            version = c["Product:Version"] ?? "V74-RC2",
            gate = trial ? "LOCAL_TRIAL_RUN_CANDIDATE" : c["Journey:CurrentGate"] ?? "V74",
            environment = env.EnvironmentName,
            productionLive = c.GetValue<bool>("Product:ProductionLive"),
            featureFreeze = c.GetValue<bool>("Product:FeatureFreeze", true),
            trial = new { enabled = trial, evidenceClass = trial ? "LOCAL_ONLY_NOT_PRODUCTION_EVIDENCE" : "NOT_APPLICABLE" },
            database = new { host = c["Database:Host"], name = c["Database:Name"], secretSource = db.SecretSource, passwordExposed = false },
            identity = new
            {
                mode = trial ? "LOCAL_TRIAL_HARNESS" : "MICROSOFT_ENTRA_ID",
                entraTenantConfigured = !RuntimeConfigurationValidator.IsPlaceholder(c["Entra:TenantId"]),
                entraClientConfigured = !RuntimeConfigurationValidator.IsPlaceholder(c["Entra:ClientId"]),
                tokenContract = trial ? "TRIAL-* + local secret -> tid+oid fixture claims" : "tid+oid+azp+scp",
                authorizationClaims = "PeopleCore PostgreSQL pc_* only",
                currentAuthMode = http.HttpContext?.User.FindFirst(TrialAuthenticationHandler.AuthModeClaim)?.Value ?? "ENTRA_OR_ANONYMOUS",
                passwordStored = false
            },
            security = new
            {
                allowedHosts = c["AllowedHosts"],
                httpsRedirect = c.GetValue<bool>("Runtime:UseHttpsRedirection"),
                hsts = c.GetValue<bool>("Runtime:UseHsts"),
                trustedProxyCount = RuntimeConfigurationValidator.KnownProxies(c).Count,
                rateLimitPerWindow = c.GetValue<int>("Runtime:RateLimitPermitLimit", 120),
                requestBodyLogging = false,
                tokenLogging = false
            },
            observability = new { structuredConsole = "JSON", correlationHeader = CorrelationIdMiddleware.HeaderName, health = new[] { "/health/live", "/health/ready", "/health/startup" } },
            recovery = new { backup = "pg_dump custom format + SHA256", restore = "guarded operation", productionRestore = "requires approved DR runbook" },
            payroll = new { officialResultSource = c["Payroll:OfficialResultSource"], shadowOfficial = false, payslipRelease = c.GetValue<bool>("Payroll:PayslipReleaseEnabled"), bravoIntegration = c["Bravo:Mode"], bravoTransport = c["Bravo:TransportMode"], bravoContract = c["Bravo:CanonicalContractVersion"] },
            population = new { currentBaseline = c.GetValue<int>("Population:CurrentBaseline", 140), capacityTargetMinimum = c.GetValue<int>("Population:CapacityTargetMinimum", 500), capacityDesign = c.GetValue<int>("Population:CapacityDesign", 1000) },
            journey = new { current = c["Journey:CurrentGate"] ?? "V74", next = c["Journey:NextGate"] ?? "ROADMAP_COMPLETE", final = c["Journey:FinalGate"] ?? "V74", rule = "DO_NOT_MARK_PRODUCTION_LIVE_WITHOUT_REAL_RUNTIME_UAT_SIGNOFF" },
            scope = trial ? "V74_RC2_LOCAL_TRIAL_NOT_PRODUCTION_EVIDENCE" : "V74_RC2_PRODUCTION_GOVERNED"
        };
    }
}
