using System.Net;
using Npgsql;

namespace PeopleCore.Api.Runtime;

public sealed record DatabaseConnectionInfo(string ConnectionString, string SecretSource);

public static class DatabaseConnectionFactory
{
    public static DatabaseConnectionInfo Resolve(IConfiguration configuration)
    {
        var explicitConnection = configuration.GetConnectionString("PeopleCore");
        if (!string.IsNullOrWhiteSpace(explicitConnection) && !RuntimeConfigurationValidator.IsPlaceholder(explicitConnection))
            return new DatabaseConnectionInfo(explicitConnection, "CONNECTION_STRING");

        var passwordFile = configuration["Secrets:DatabasePasswordFile"] ?? Environment.GetEnvironmentVariable("PEOPLECORE_DB_PASSWORD_FILE");
        string? password = null;
        var source = "NONE";
        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            if (!File.Exists(passwordFile)) throw new InvalidOperationException("Database password secret file is configured but does not exist.");
            password = File.ReadAllText(passwordFile).Trim();
            source = "FILE";
        }
        if (string.IsNullOrWhiteSpace(password))
        {
            password = configuration["Database:Password"];
            if (!string.IsNullOrWhiteSpace(password)) source = "CONFIG";
        }
        if (string.IsNullOrWhiteSpace(password) || RuntimeConfigurationValidator.IsPlaceholder(password))
            throw new InvalidOperationException("Database credential is required via secret file or environment-backed configuration.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = configuration["Database:Host"] ?? "localhost",
            Port = configuration.GetValue("Database:Port", 5432),
            Database = configuration["Database:Name"] ?? "peoplecore",
            Username = configuration["Database:Username"] ?? "peoplecore",
            Password = password,
            Pooling = true,
            Timeout = configuration.GetValue("Database:ConnectTimeoutSeconds", 10),
            CommandTimeout = configuration.GetValue("Database:CommandTimeoutSeconds", 30),
            ApplicationName = "CPG PeopleCore V74-RC2"
        };
        var sslMode = configuration["Database:SslMode"];
        if (!string.IsNullOrWhiteSpace(sslMode)) builder["SSL Mode"] = sslMode;
        return new DatabaseConnectionInfo(builder.ConnectionString, source);
    }
}

public static class RuntimeConfigurationValidator
{
    public static bool IsPlaceholder(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("__", StringComparison.Ordinal) || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) || value == "00000000-0000-0000-0000-000000000000";
    }

    public static void ValidateOrThrow(IConfiguration c, IHostEnvironment env, DatabaseConnectionInfo db)
    {
        var errors = new List<string>();
        var trialAuthRequested = c.GetValue<bool>("TrialAuth:Enabled");
        var isTrial = env.IsEnvironment("Trial");

        if (trialAuthRequested && !isTrial) errors.Add("TrialAuth may only be enabled when ASPNETCORE_ENVIRONMENT=Trial.");
        if ((env.IsProduction() || env.IsEnvironment("Pilot")) && trialAuthRequested) errors.Add("TrialAuth is forbidden in Production and Pilot.");

        if (!isTrial || !trialAuthRequested)
        {
            if (IsPlaceholder(c["Entra:TenantId"])) errors.Add("Entra:TenantId is missing or placeholder.");
            if (IsPlaceholder(c["Entra:ClientId"])) errors.Add("Entra:ClientId is missing or placeholder.");
            if (IsPlaceholder(c["Entra:RequiredScope"])) errors.Add("Entra:RequiredScope is missing or placeholder.");
            if (IsPlaceholder(c["Entra:AllowedClientIds"])) errors.Add("Entra:AllowedClientIds is missing or placeholder.");
        }
        else
        {
            var secretFile = c["TrialAuth:SharedSecretFile"] ?? Environment.GetEnvironmentVariable("PEOPLECORE_TRIAL_AUTH_SECRET_FILE");
            if (string.IsNullOrWhiteSpace(secretFile) || !File.Exists(secretFile) || string.IsNullOrWhiteSpace(File.ReadAllText(secretFile).Trim()))
                errors.Add("TrialAuth shared secret file must exist and contain a non-empty random secret.");
            if (!string.Equals(c["TrialAuth:TenantId"], "trial-local", StringComparison.Ordinal))
                errors.Add("TrialAuth:TenantId must remain trial-local.");
            var allowedHosts = (c["AllowedHosts"] ?? string.Empty).Trim();
            if (allowedHosts == "*" || !allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(h => h is "localhost" or "127.0.0.1" or "[::1]"))
                errors.Add("Trial AllowedHosts must be localhost/loopback only.");
            if (c.GetValue<bool>("Product:ProductionLive")) errors.Add("Trial must keep Product:ProductionLive=false.");
            if (c.GetValue<bool>("Payroll:PayslipReleaseEnabled")) errors.Add("Trial must keep Payroll:PayslipReleaseEnabled=false.");
            if (c.GetValue<bool>("V74:CutoverOrchestrationEnabled")) errors.Add("Trial must keep V74 cutover orchestration disabled.");
            if (!string.Equals(c["Bravo:Mode"], "Stub", StringComparison.OrdinalIgnoreCase)) errors.Add("Trial must use BRAVO Stub only; it is not production integration evidence.");
            if (c.GetValue<bool>("Bravo:TransportPilot:LiveDeliveryEnabled")) errors.Add("Trial BRAVO live delivery must remain disabled.");
        }

        if (!string.Equals(c["Payroll:OfficialResultSource"], "BRAVO", StringComparison.OrdinalIgnoreCase)) errors.Add("Official payroll source must remain BRAVO.");
        if (c.GetValue<bool>("Payroll:ShadowEngineEnabled")) errors.Add("Shadow Payroll cannot be enabled as an official calculation path.");
        var hosts = (c["AllowedHosts"] ?? string.Empty).Trim();
        if ((env.IsProduction() || env.IsEnvironment("Pilot")) && (hosts == "*" || string.IsNullOrWhiteSpace(hosts))) errors.Add("AllowedHosts must be explicit outside Development.");
        if (env.IsProduction() && c.GetValue<bool>("Pilot:Enabled")) errors.Add("Pilot features must be disabled in Production.");
        if (env.IsProduction() && !c.GetValue<bool>("Runtime:UseHsts")) errors.Add("Runtime:UseHsts must be enabled in Production.");
        if (env.IsProduction() && string.IsNullOrWhiteSpace(c["Runtime:KnownProxies"])) errors.Add("Production reverse-proxy addresses must be explicitly configured.");
        if (env.IsProduction() && db.SecretSource != "FILE" && !c.GetValue<bool>("Runtime:AllowEnvironmentSecrets")) errors.Add("Production database credential must come from a secret file unless explicitly approved.");
        if (env.IsProduction() && string.Equals(hosts, "localhost;127.0.0.1", StringComparison.OrdinalIgnoreCase)) errors.Add("Production AllowedHosts must be replaced with the approved external/internal hostnames.");
        var dbSslMode = (c["Database:SslMode"] ?? string.Empty).Trim();
        if (env.IsProduction() && !new[] { "Require", "VerifyCA", "VerifyFull" }.Contains(dbSslMode, StringComparer.OrdinalIgnoreCase)) errors.Add("Production PostgreSQL transport must use SSL Mode Require, VerifyCA or VerifyFull.");
        if (c.GetValue<bool>("Product:ProductionLive") && !env.IsProduction()) errors.Add("Product:ProductionLive may only be true when ASPNETCORE_ENVIRONMENT=Production.");
        if (errors.Count > 0) throw new InvalidOperationException("V74-RC2 runtime configuration rejected: " + string.Join(" ", errors));
    }

    public static IReadOnlyList<IPAddress> KnownProxies(IConfiguration c)
    {
        var raw = c["Runtime:KnownProxies"] ?? string.Empty;
        return raw.Split(new[]{',',';'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => IPAddress.TryParse(x, out var ip) ? ip : null).Where(x => x is not null).Cast<IPAddress>().ToArray();
    }
}
