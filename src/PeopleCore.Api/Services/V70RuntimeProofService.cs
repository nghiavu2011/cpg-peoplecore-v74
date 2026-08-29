using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Data;
using PeopleCore.Api.Runtime;
using PeopleCore.Api.Security;

namespace PeopleCore.Api.Services;

public sealed class V70RuntimeProofService(
    PeopleCoreDbContext db, IConfiguration c, IHostEnvironment env, DatabaseConnectionInfo database, ICurrentUser current)
{
    public async Task<object> BuildAsync(CancellationToken ct = default)
    {
        var databaseReachable = await db.Database.CanConnectAsync(ct);
        var employeeCount = databaseReachable ? await db.Employees.AsNoTracking().CountAsync(ct) : 0;
        var activeIdentityCount = databaseReachable ? await db.EmployeeIdentities.AsNoTracking().CountAsync(x => x.IsActive && x.RevokedAt == null, ct) : 0;
        var configuredTenant = c["Entra:TenantId"];
        var configuredClient = c["Entra:ClientId"];
        return new
        {
            version = "V70",
            gate = "REAL_RUNTIME_ENTRA_POSTGRESQL",
            productionLive = false,
            featureFreeze = true,
            generatedAtUtc = DateTimeOffset.UtcNow,
            runtime = new
            {
                environment = env.EnvironmentName,
                frameworkTarget = "net10.0",
                sdkPin = "10.0.400",
                correlation = CorrelationIdMiddleware.HeaderName
            },
            postgresql = new
            {
                reachable = databaseReachable,
                database = c["Database:Name"],
                secretSource = database.SecretSource,
                employeeRows = employeeCount,
                activeIdentityLinks = activeIdentityCount,
                credentialExposed = false
            },
            entra = new
            {
                tenantConfigured = !RuntimeConfigurationValidator.IsPlaceholder(configuredTenant),
                apiClientConfigured = !RuntimeConfigurationValidator.IsPlaceholder(configuredClient),
                inboundClaimMapping = false,
                requiredClaims = new[] { "tid", "oid", "azp", "scp" },
                allowedClientIdsConfigured = !RuntimeConfigurationValidator.IsPlaceholder(c["Entra:AllowedClientIds"]),
                requiredDelegatedScope = c["Entra:RequiredScope"],
                callerTenantMatches = string.Equals(current.EntraTenantId, configuredTenant, StringComparison.OrdinalIgnoreCase),
                callerObjectIdPresent = Guid.TryParse(current.EntraObjectId, out _),
                microsoftPasswordStored = false
            },
            peopleCoreIdentity = new
            {
                mapped = current.EmployeeId is not null,
                staffCode = current.StaffCode,
                authorizationSource = "PeopleCore PostgreSQL pc_role/pc_scope"
            },
            population = new
            {
                sourceBackedCurrent = c.GetValue<int>("Population:CurrentBaseline", 140),
                sourceBackedHcm = c.GetValue<int>("Population:HcmBaseline", 101),
                sourceBackedHn = c.GetValue<int>("Population:HnBaseline", 39),
                capacityTargetMinimum = c.GetValue<int>("Population:CapacityTargetMinimum", 500),
                capacityDesign = c.GetValue<int>("Population:CapacityDesign", 1000),
                note = "Database counts are runtime observations, not proof that V71 HR Master migration is complete."
            },
            lockedArchitecture = new
            {
                officialPayrollResultSource = c["Payroll:OfficialResultSource"],
                shadowPayrollOfficial = false,
                bravoNativeTransport = c["Bravo:TransportMode"],
                bravoOfficialResultImport = c["Bravo:OfficialPayrollResultImport"]
            },
            journey = new
            {
                current = "V70",
                next = "V71_REAL_HR_PILOT",
                then = new[] { "V72_BRAVO_PAYROLL_PARALLEL_RUN", "V73_FULL_END_TO_END_UAT", "V74_PRODUCTION_CUTOVER" },
                productionLiveRule = "Only after V74 runtime/UAT/security/HR/payroll/Finance-BRAVO sign-off."
            }
        };
    }
}
