using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Data;
using PeopleCore.Api.Runtime;
using PeopleCore.Api.Services.Bravo;

namespace PeopleCore.Api.Health;

public sealed class DatabaseConnectivityHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try { using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PeopleCoreDbContext>(); return await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy("PostgreSQL reachable") : HealthCheckResult.Unhealthy("PostgreSQL unreachable"); }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("PostgreSQL connectivity failed", ex); }
    }
}

public sealed class DatabaseSchemaHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PeopleCoreDbContext>();
            var connection = db.Database.GetDbConnection(); if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT (to_regclass('peoplecore.employee') IS NOT NULL AND to_regclass('peoplecore.employee_identity') IS NOT NULL AND to_regclass('peoplecore.authorization_grant') IS NOT NULL AND to_regclass('peoplecore.migration_batch') IS NOT NULL AND to_regclass('peoplecore.pilot_run') IS NOT NULL AND to_regclass('peoplecore.audit_event') IS NOT NULL AND to_regclass('peoplecore.project_code') IS NOT NULL AND to_regclass('peoplecore.compensation_handoff') IS NOT NULL AND to_regclass('peoplecore.bravo_transport_evidence') IS NOT NULL AND to_regclass('peoplecore.hr_pilot_run') IS NOT NULL AND to_regclass('peoplecore.hr_pilot_scenario_evidence') IS NOT NULL AND to_regclass('peoplecore.payroll_parallel_run') IS NOT NULL AND to_regclass('peoplecore.payroll_parallel_variance') IS NOT NULL AND to_regclass('peoplecore.functional_evidence') IS NOT NULL AND to_regclass('peoplecore.evidence_artifact') IS NOT NULL AND to_regclass('peoplecore.leave_request') IS NOT NULL AND to_regclass('peoplecore.attendance_day') IS NOT NULL AND to_regclass('peoplecore.overtime_request') IS NOT NULL AND to_regclass('peoplecore.timesheet_entry') IS NOT NULL AND to_regclass('peoplecore.performance_review') IS NOT NULL AND to_regclass('peoplecore.tax_insurance_snapshot') IS NOT NULL AND to_regclass('peoplecore.payslip_release') IS NOT NULL)";
            var ok = Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
            return ok ? HealthCheckResult.Healthy("Required PeopleCore schema present") : HealthCheckResult.Unhealthy("Required PeopleCore schema is incomplete");
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("Schema check failed", ex); }
    }
}

public sealed class RuntimeBoundaryHealthCheck(IConfiguration c, IHostEnvironment env) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var problems = new List<string>();
        var trial = env.IsEnvironment("Trial") && c.GetValue<bool>("TrialAuth:Enabled");
        if (!trial)
        {
            if (RuntimeConfigurationValidator.IsPlaceholder(c["Entra:TenantId"])) problems.Add("entra-tenant");
            if (RuntimeConfigurationValidator.IsPlaceholder(c["Entra:ClientId"])) problems.Add("entra-client");
        }
        else
        {
            var secretFile = c["TrialAuth:SharedSecretFile"] ?? Environment.GetEnvironmentVariable("PEOPLECORE_TRIAL_AUTH_SECRET_FILE");
            if (string.IsNullOrWhiteSpace(secretFile) || !File.Exists(secretFile)) problems.Add("trial-secret");
            if (c.GetValue<bool>("Product:ProductionLive")) problems.Add("trial-production-live");
            if (c.GetValue<bool>("Payroll:PayslipReleaseEnabled")) problems.Add("trial-payslip-release");
            if (c.GetValue<bool>("V74:CutoverOrchestrationEnabled")) problems.Add("trial-cutover-enabled");
        }
        if (c.GetValue<bool>("TrialAuth:Enabled") && !env.IsEnvironment("Trial")) problems.Add("trial-auth-outside-trial");
        if (!string.Equals(c["Payroll:OfficialResultSource"], "BRAVO", StringComparison.OrdinalIgnoreCase)) problems.Add("official-payroll-source");
        if (c.GetValue<bool>("Payroll:ShadowEngineEnabled")) problems.Add("shadow-enabled");
        if ((env.IsProduction() || env.IsEnvironment("Pilot")) && (c["AllowedHosts"] == "*" || string.IsNullOrWhiteSpace(c["AllowedHosts"]))) problems.Add("allowed-hosts");
        return Task.FromResult(problems.Count == 0 ? HealthCheckResult.Healthy(trial ? "Trial-local boundaries intact (not production evidence)" : "Locked runtime boundaries intact") : HealthCheckResult.Unhealthy("Runtime boundary failures: " + string.Join(',', problems)));
    }
}

public sealed class DiskSpaceHealthCheck(IConfiguration c) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var path = c["Runtime:ReadinessDiskPath"] ?? "/tmp"; var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root)) return Task.FromResult(HealthCheckResult.Unhealthy("Disk root unavailable"));
            var drive = new DriveInfo(root); var minimum = c.GetValue<long>("Runtime:MinimumFreeDiskBytes", 268435456);
            return Task.FromResult(drive.AvailableFreeSpace >= minimum ? HealthCheckResult.Healthy("Disk free-space threshold satisfied") : HealthCheckResult.Unhealthy("Disk free-space threshold not satisfied"));
        }
        catch (Exception ex) { return Task.FromResult(HealthCheckResult.Unhealthy("Disk check failed", ex)); }
    }
}


public sealed class BravoAdapterPilotHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var pilot = scope.ServiceProvider.GetRequiredService<BravoAdapterPilotService>();
            var status = pilot.GetStatus();
            if (status.Transport.Mode == V68BravoModes.TransportSignedDryRun && !status.Transport.SecretReady)
                return Task.FromResult(HealthCheckResult.Unhealthy("V70 retained signed dry-run transport key unavailable"));
            if (status.Transport.LiveDeliveryEnabled)
                return Task.FromResult(HealthCheckResult.Unhealthy("V70 live BRAVO delivery must remain disabled"));
            return Task.FromResult(HealthCheckResult.Healthy("V70 retained BRAVO adapter pilot boundary intact"));
        }
        catch (Exception ex) { return Task.FromResult(HealthCheckResult.Unhealthy("V70 retained BRAVO adapter pilot check failed", ex)); }
    }
}

public static class V66HealthResponse
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var expose = configuration.GetValue<bool>("Runtime:ExposeHealthDetails");
        var version = configuration["Product:Version"] ?? "V74-RC2";
        var productionLive = configuration.GetValue<bool>("Product:ProductionLive");
        object payload = expose
            ? new { status = report.Status.ToString(), version, productionLive, durationMs = report.TotalDuration.TotalMilliseconds, checks = report.Entries.ToDictionary(x => x.Key, x => new { status = x.Value.Status.ToString(), description = x.Value.Description }) }
            : new { status = report.Status.ToString(), version, productionLive, durationMs = report.TotalDuration.TotalMilliseconds };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
