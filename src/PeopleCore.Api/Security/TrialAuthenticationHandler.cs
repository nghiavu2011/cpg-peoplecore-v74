using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeopleCore.Api.Data;

namespace PeopleCore.Api.Security;

/// <summary>
/// Local-only authentication harness for the isolated Trial environment.
/// It deliberately does not issue any PeopleCore pc_* authorization claims;
/// PeopleCoreClaimsTransformation still resolves tid+oid from PostgreSQL and
/// remains the sole issuer of internal role/scope claims.
///
/// This handler must never be enabled in Pilot or Production and must never be
/// accepted as Entra/UAT/production evidence.
/// </summary>
public sealed class TrialAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    PeopleCoreDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PeopleCoreTrial";
    public const string StaffHeader = "X-Trial-Staff-Code";
    public const string SecretHeader = "X-Trial-Key";
    public const string AuthModeClaim = "auth_mode";
    public const string AuthModeValue = "TRIAL_LOCAL_ONLY";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.RequestServices.GetRequiredService<IHostEnvironment>().IsEnvironment("Trial")
            || !configuration.GetValue<bool>("TrialAuth:Enabled"))
            return AuthenticateResult.Fail("Trial authentication is not enabled in the Trial environment.");

        var staffCode = Request.Headers[StaffHeader].ToString().Trim().ToUpperInvariant();
        var suppliedSecret = Request.Headers[SecretHeader].ToString();
        if (string.IsNullOrWhiteSpace(staffCode) || string.IsNullOrEmpty(suppliedSecret))
            return AuthenticateResult.NoResult();

        // The harness is intentionally unable to impersonate real employee records.
        if (!staffCode.StartsWith("TRIAL-", StringComparison.Ordinal))
            return AuthenticateResult.Fail("Trial authentication is restricted to TRIAL-* fixture identities.");

        var secretFile = configuration["TrialAuth:SharedSecretFile"]
            ?? Environment.GetEnvironmentVariable("PEOPLECORE_TRIAL_AUTH_SECRET_FILE");
        if (string.IsNullOrWhiteSpace(secretFile) || !File.Exists(secretFile))
            return AuthenticateResult.Fail("Trial authentication secret file is unavailable.");

        var expectedSecret = (await File.ReadAllTextAsync(secretFile, Context.RequestAborted)).Trim();
        if (!FixedTimeEquals(expectedSecret, suppliedSecret))
            return AuthenticateResult.Fail("Invalid trial authentication key.");

        var expectedTenant = (configuration["TrialAuth:TenantId"] ?? "trial-local").Trim();
        var employee = await db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StaffCode == staffCode, Context.RequestAborted);
        if (employee is null || !string.Equals(employee.EmploymentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Trial employee is unavailable or inactive.");

        var identityLink = await db.EmployeeIdentities.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EmployeeId == employee.Id
                && x.IsActive
                && x.RevokedAt == null
                && x.EntraTenantId == expectedTenant,
                Context.RequestAborted);
        if (identityLink is null)
            return AuthenticateResult.Fail("Trial identity mapping is unavailable.");

        var claims = new List<Claim>
        {
            new("tid", identityLink.EntraTenantId),
            new("oid", identityLink.EntraObjectId),
            new("name", employee.DisplayName),
            new("preferred_username", employee.CorporateEmail),
            new(AuthModeClaim, AuthModeValue),
            new("scp", "PeopleCore.Trial"),
            new("azp", "peoplecore-trial-harness")
        };
        var identity = new ClaimsIdentity(claims, SchemeName, "name", ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
