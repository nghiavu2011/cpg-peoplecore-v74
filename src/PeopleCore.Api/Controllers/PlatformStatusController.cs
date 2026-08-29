using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Runtime;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Bravo;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/platform")]
[Authorize(Policy = Policies.HrOrAdmin)]
public sealed class PlatformStatusController(IConfiguration configuration, IHostEnvironment env, IBravoAdapter bravo, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status()
    {
        var trial = env.IsEnvironment("Trial") && configuration.GetValue<bool>("TrialAuth:Enabled");
        var tenantConfigured = !RuntimeConfigurationValidator.IsPlaceholder(configuration["Entra:TenantId"]);
        var clientConfigured = !RuntimeConfigurationValidator.IsPlaceholder(configuration["Entra:ClientId"]);
        return Ok(new
        {
            version = configuration["Product:Version"] ?? "V74-RC2",
            productionLive = configuration.GetValue<bool>("Product:ProductionLive"),
            mode = trial ? "LOCAL_TRIAL_NOT_PRODUCTION_EVIDENCE" : env.EnvironmentName,
            api = "V74_RC2_FUNCTIONAL_COMPLETION",
            postgresql = "MIGRATIONS_001_TO_011_PLUS_RUNTIME_HEALTH",
            identity = trial ? "LOCAL_TRIAL_HARNESS" : tenantConfigured && clientConfigured ? "ENTRA_CONFIGURED_NOT_VERIFIED" : "ENTRA_PLACEHOLDER",
            currentAuthMode = http.HttpContext?.User.FindFirst(TrialAuthenticationHandler.AuthModeClaim)?.Value ?? "ENTRA_OR_ANONYMOUS",
            serverAuthorization = "POSTGRESQL_PC_ROLE_SCOPE",
            functionalModules = "CONTRACT_LEAVE_ATTENDANCE_OT_TIMESHEET_PERFORMANCE_TAX_INSURANCE_PAYSLIP",
            bravo = bravo.GetStatus(),
            shadowPayroll = configuration.GetValue<bool>("Payroll:ShadowEngineEnabled") ? "ENABLED_VALIDATION_ONLY" : "DISABLED_PENDING_VERIFIED_RULES",
            officialPayrollSource = "BRAVO",
            payslipReleaseEnabled = configuration.GetValue<bool>("Payroll:PayslipReleaseEnabled"),
            trialEvidenceAcceptedForProduction = false
        });
    }
}
