using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;

namespace PeopleCore.Api.Security;

public static class PeopleCoreClaims
{
    public const string Issuer = "PeopleCore";
    public const string EmployeeId = "pc_employee_id";
    public const string StaffCode = "pc_staff_code";
    public const string Role = "pc_role";
    public const string Scope = "pc_scope";
    public const string Transformed = "pc_transformed";
}

public static class Roles
{
    public const string Employee = "EMPLOYEE";
    public const string Manager = "MANAGER";
    public const string Hr = "HR";
    public const string Payroll = "PAYROLL";
    public const string Admin = "ADMIN";
    public const string Leadership = "LEADERSHIP";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { Employee, Manager, Hr, Payroll, Admin, Leadership };
}

public static class ScopeTypes
{
    public const string Self = "SELF";
    public const string Department = "DEPARTMENT";
    public const string Company = "COMPANY";
    public const string Global = "GLOBAL";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { Self, Department, Company, Global };
}

public static class Policies
{
    public const string Hr = "peoplecore.hr";
    public const string Payroll = "peoplecore.payroll";
    public const string PlatformAdmin = "peoplecore.platform_admin";
    public const string HrOrAdmin = "peoplecore.hr_or_admin";
    public const string HrOrPayroll = "peoplecore.hr_or_payroll";
    public const string PeopleCoreUser = "peoplecore.active_user";
    public const string EvidenceRegistrar = "peoplecore.evidence_registrar";
}

public sealed record ActiveEmployeeRequirement : IAuthorizationRequirement;
public sealed record InternalRoleRequirement(string Role) : IAuthorizationRequirement;

public sealed class ActiveEmployeeHandler : AuthorizationHandler<ActiveEmployeeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveEmployeeRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == PeopleCoreClaims.EmployeeId && c.Issuer == PeopleCoreClaims.Issuer)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class InternalRoleHandler : AuthorizationHandler<InternalRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, InternalRoleRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == PeopleCoreClaims.Role && c.Value == requirement.Role && c.Issuer == PeopleCoreClaims.Issuer)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class PeopleCoreClaimsTransformation(PeopleCoreDbContext db) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated) return principal;
        if (identity.HasClaim(c => c.Type == PeopleCoreClaims.Transformed && c.Issuer == PeopleCoreClaims.Issuer)) return principal;

        // Never trust inbound PeopleCore-looking claims. Only this transformation may issue pc_* authorization claims.
        var reserved = new HashSet<string>(StringComparer.Ordinal)
        { PeopleCoreClaims.EmployeeId, PeopleCoreClaims.StaffCode, PeopleCoreClaims.Role, PeopleCoreClaims.Scope, PeopleCoreClaims.Transformed };
        foreach (var claim in identity.Claims.Where(c => reserved.Contains(c.Type)).ToList()) identity.RemoveClaim(claim);

        var tid = principal.FindFirstValue("tid");
        var oid = principal.FindFirstValue("oid");
        if (string.IsNullOrWhiteSpace(tid) || string.IsNullOrWhiteSpace(oid)) return principal;

        var link = await db.EmployeeIdentities.AsNoTracking().Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.EntraTenantId == tid && x.EntraObjectId == oid && x.IsActive && x.RevokedAt == null);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (link?.Employee is null
            || !string.Equals(link.Employee.EmploymentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            || link.Employee.EffectiveFrom > today
            || (link.Employee.EffectiveTo is not null && link.Employee.EffectiveTo < today))
            return principal;

        Add(identity, PeopleCoreClaims.EmployeeId, link.EmployeeId.ToString());
        Add(identity, PeopleCoreClaims.StaffCode, link.Employee.StaffCode);
        Add(identity, PeopleCoreClaims.Role, Roles.Employee);
        Add(identity, PeopleCoreClaims.Scope, $"{Roles.Employee}:{ScopeTypes.Self}:*");

        var now = DateTimeOffset.UtcNow;
        var grants = await db.AuthorizationGrants.AsNoTracking()
            .Where(x => x.EmployeeId == link.EmployeeId && x.RevokedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now))
            .ToListAsync();

        foreach (var grant in grants.Where(g => Roles.Known.Contains(g.RoleCode) && ScopeTypes.Known.Contains(g.ScopeType)))
        {
            Add(identity, PeopleCoreClaims.Role, grant.RoleCode.ToUpperInvariant());
            Add(identity, PeopleCoreClaims.Scope, $"{grant.RoleCode.ToUpperInvariant()}:{grant.ScopeType.ToUpperInvariant()}:{grant.ScopeValue ?? "*"}");
        }
        Add(identity, PeopleCoreClaims.Transformed, "true");
        return principal;
    }

    private static void Add(ClaimsIdentity identity, string type, string value) =>
        identity.AddClaim(new Claim(type, value, ClaimValueTypes.String, PeopleCoreClaims.Issuer));
}

public interface ICurrentUser
{
    Guid? EmployeeId { get; }
    string? StaffCode { get; }
    string? EntraObjectId { get; }
    string? EntraTenantId { get; }
    bool IsInRole(string role);
}

public sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();
    private string? PcClaim(string type) => User.Claims.FirstOrDefault(c => c.Type == type && c.Issuer == PeopleCoreClaims.Issuer)?.Value;
    public Guid? EmployeeId => Guid.TryParse(PcClaim(PeopleCoreClaims.EmployeeId), out var id) ? id : null;
    public string? StaffCode => PcClaim(PeopleCoreClaims.StaffCode);
    public string? EntraObjectId => User.FindFirstValue("oid");
    public string? EntraTenantId => User.FindFirstValue("tid");
    public bool IsInRole(string role) => User.HasClaim(c => c.Type == PeopleCoreClaims.Role && c.Value == role && c.Issuer == PeopleCoreClaims.Issuer);
}

[Flags]
public enum EmployeeFieldSet { None = 0, Work = 1, HrSelf = 2, HrPrivate = 4, Identity = 8 }

public sealed record EmployeeAccessDecision(bool Allowed, EmployeeFieldSet Fields, string Scope, string Reason)
{
    public static EmployeeAccessDecision Deny(string reason) => new(false, EmployeeFieldSet.None, "DENY", reason);
}

public interface IAccessControlService
{
    Task<EmployeeAccessDecision> DecideReadEmployeeAsync(Employee target, CancellationToken ct = default);
    Task<bool> CanEditEmployeeAsync(Employee target, CancellationToken ct = default);
    Task<bool> CanCreateEmployeeAsync(string companyCode, string departmentCode, CancellationToken ct = default);
    Task<bool> CanHandleCompensationAsync(Employee target, CancellationToken ct = default);
    Task<bool> CanManagerActOnAsync(Employee target, CancellationToken ct = default);
    Task<bool> CanPayrollActOnAsync(Employee target, CancellationToken ct = default);
    Task<IReadOnlySet<Guid>> GetDirectoryEmployeeIdsAsync(CancellationToken ct = default);
}

public sealed class AccessControlService(PeopleCoreDbContext db, ICurrentUser current) : IAccessControlService
{
    public async Task<EmployeeAccessDecision> DecideReadEmployeeAsync(Employee target, CancellationToken ct = default)
    {
        if (current.EmployeeId is not Guid actorId) return EmployeeAccessDecision.Deny("IDENTITY_NOT_MAPPED");
        if (actorId == target.Id) return new(true, EmployeeFieldSet.Work | EmployeeFieldSet.HrSelf, ScopeTypes.Self, "SELF_ACCESS");

        var assignment = await CurrentAssignmentAsync(target.Id, ct);
        var company = assignment?.CompanyCode ?? target.CompanyCode;
        var department = assignment?.DepartmentCode ?? target.DepartmentCode;
        var managerId = assignment?.ManagerEmployeeId ?? target.ManagerEmployeeId;

        if (current.IsInRole(Roles.Hr) && await HasScopedGrantAsync(Roles.Hr, target.Id, company, department, ct))
            return new(true, EmployeeFieldSet.Work | EmployeeFieldSet.HrSelf | EmployeeFieldSet.HrPrivate, "HR_SCOPED", "HR_SCOPED_ACCESS");
        if (current.IsInRole(Roles.Manager) && managerId == actorId)
            return new(true, EmployeeFieldSet.Work, "DIRECT_REPORT", "MANAGER_DIRECT_REPORT");
        if (current.IsInRole(Roles.Payroll) && await HasScopedGrantAsync(Roles.Payroll, target.Id, company, department, ct))
            return new(true, EmployeeFieldSet.Work, "PAYROLL_SCOPED", "PAYROLL_WORK_FIELDS_ONLY");
        if (current.IsInRole(Roles.Leadership) && await HasScopedGrantAsync(Roles.Leadership, target.Id, company, department, ct))
            return new(true, EmployeeFieldSet.Work, "LEADERSHIP_SCOPED", "LEADERSHIP_WORK_FIELDS_ONLY");

        // Technical Admin deliberately does not inherit Employee Master read-through.
        return EmployeeAccessDecision.Deny("ROW_SCOPE_DENIED");
    }

    public async Task<bool> CanEditEmployeeAsync(Employee target, CancellationToken ct = default)
    {
        if (!current.IsInRole(Roles.Hr)) return false;
        var assignment = await CurrentAssignmentAsync(target.Id, ct);
        return await HasScopedGrantAsync(Roles.Hr, target.Id, assignment?.CompanyCode ?? target.CompanyCode, assignment?.DepartmentCode ?? target.DepartmentCode, ct);
    }

    public async Task<bool> CanCreateEmployeeAsync(string companyCode, string departmentCode, CancellationToken ct = default)
    {
        if (!current.IsInRole(Roles.Hr) || current.EmployeeId is not Guid actorId) return false;
        var grants = await ActiveGrants(actorId, Roles.Hr, DateTimeOffset.UtcNow, ct);
        return grants.Any(g => ScopeMatches(g, actorId, Guid.Empty, companyCode, departmentCode, allowSelf:false));
    }

    public async Task<bool> CanHandleCompensationAsync(Employee target, CancellationToken ct = default)
    {
        var assignment = await CurrentAssignmentAsync(target.Id, ct);
        var company = assignment?.CompanyCode ?? target.CompanyCode;
        var department = assignment?.DepartmentCode ?? target.DepartmentCode;
        if (current.IsInRole(Roles.Hr) && await HasScopedGrantAsync(Roles.Hr, target.Id, company, department, ct)) return true;
        if (current.IsInRole(Roles.Payroll) && await HasScopedGrantAsync(Roles.Payroll, target.Id, company, department, ct)) return true;
        return false;
    }

    public async Task<bool> CanManagerActOnAsync(Employee target, CancellationToken ct = default)
    {
        if (!current.IsInRole(Roles.Manager) || current.EmployeeId is not Guid actorId) return false;
        var assignment = await CurrentAssignmentAsync(target.Id, ct);
        return assignment?.ManagerEmployeeId == actorId;
    }

    public async Task<bool> CanPayrollActOnAsync(Employee target, CancellationToken ct = default)
    {
        if (!current.IsInRole(Roles.Payroll)) return false;
        var assignment = await CurrentAssignmentAsync(target.Id, ct);
        return await HasScopedGrantAsync(Roles.Payroll, target.Id, assignment?.CompanyCode ?? target.CompanyCode, assignment?.DepartmentCode ?? target.DepartmentCode, ct);
    }

    public async Task<IReadOnlySet<Guid>> GetDirectoryEmployeeIdsAsync(CancellationToken ct = default)
    {
        if (current.EmployeeId is not Guid actorId) return new HashSet<Guid>();
        var ids = new HashSet<Guid> { actorId };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (current.IsInRole(Roles.Manager))
        {
            var direct = await db.EmployeeAssignments.AsNoTracking()
                .Where(x => x.ManagerEmployeeId == actorId && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today))
                .Select(x => x.EmployeeId).Distinct().ToListAsync(ct);
            ids.UnionWith(direct);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var role in new[] { Roles.Hr, Roles.Payroll, Roles.Leadership })
        {
            if (!current.IsInRole(role)) continue;
            var grants = await ActiveGrants(actorId, role, now, ct);
            if (grants.Any(g => string.Equals(g.ScopeType, ScopeTypes.Global, StringComparison.OrdinalIgnoreCase)))
                return (await db.Employees.AsNoTracking().Select(x => x.Id).ToListAsync(ct)).ToHashSet();

            var companies = grants.Where(g => string.Equals(g.ScopeType, ScopeTypes.Company, StringComparison.OrdinalIgnoreCase) && g.ScopeValue != null)
                .Select(g => g.ScopeValue!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (companies.Length > 0)
            {
                var byCompany = await db.EmployeeAssignments.AsNoTracking()
                    .Where(x => companies.Contains(x.CompanyCode) && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today))
                    .Select(x => x.EmployeeId).Distinct().ToListAsync(ct);
                ids.UnionWith(byCompany);
            }

            var departments = grants.Where(g => string.Equals(g.ScopeType, ScopeTypes.Department, StringComparison.OrdinalIgnoreCase) && g.ScopeValue != null)
                .Select(g => g.ScopeValue!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (departments.Length > 0)
            {
                var byDept = await db.EmployeeAssignments.AsNoTracking()
                    .Where(x => departments.Contains(x.DepartmentCode) && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today))
                    .Select(x => x.EmployeeId).Distinct().ToListAsync(ct);
                ids.UnionWith(byDept);
            }
        }
        return ids;
    }

    private async Task<EmployeeAssignment?> CurrentAssignmentAsync(Guid employeeId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.EmployeeAssignments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today))
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
    }

    private async Task<bool> HasScopedGrantAsync(string role, Guid targetId, string company, string department, CancellationToken ct)
    {
        if (current.EmployeeId is not Guid actorId) return false;
        var grants = await ActiveGrants(actorId, role, DateTimeOffset.UtcNow, ct);
        return grants.Any(g => ScopeMatches(g, actorId, targetId, company, department, allowSelf:true));
    }

    private static bool ScopeMatches(AuthorizationGrant g, Guid actorId, Guid targetId, string company, string department, bool allowSelf) =>
        g.ScopeType.ToUpperInvariant() switch
        {
            ScopeTypes.Global => true,
            ScopeTypes.Company => string.Equals(g.ScopeValue, company, StringComparison.OrdinalIgnoreCase),
            ScopeTypes.Department => string.Equals(g.ScopeValue, department, StringComparison.OrdinalIgnoreCase),
            ScopeTypes.Self => allowSelf && actorId == targetId,
            _ => false
        };

    private Task<List<AuthorizationGrant>> ActiveGrants(Guid employeeId, string role, DateTimeOffset now, CancellationToken ct) =>
        db.AuthorizationGrants.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.RoleCode == role && x.RevokedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now)).ToListAsync(ct);
}

public static class AccessGrantRules
{
    public static void Validate(string role, string scopeType, string? scopeValue)
    {
        role = role.Trim().ToUpperInvariant();
        scopeType = scopeType.Trim().ToUpperInvariant();
        if (!Roles.Known.Contains(role)) throw new InvalidOperationException("UNKNOWN_ROLE");
        if (!ScopeTypes.Known.Contains(scopeType)) throw new InvalidOperationException("UNKNOWN_SCOPE");
        var needsValue = scopeType is ScopeTypes.Company or ScopeTypes.Department;
        if (needsValue && string.IsNullOrWhiteSpace(scopeValue)) throw new InvalidOperationException("SCOPE_VALUE_REQUIRED");
        if (!needsValue && !string.IsNullOrWhiteSpace(scopeValue)) throw new InvalidOperationException("SCOPE_VALUE_NOT_ALLOWED");
        if (role == Roles.Employee && scopeType != ScopeTypes.Self) throw new InvalidOperationException("EMPLOYEE_SCOPE_MUST_BE_SELF");
        if (role == Roles.Manager && scopeType != ScopeTypes.Self) throw new InvalidOperationException("MANAGER_SCOPE_MUST_BE_SELF");
        if (role == Roles.Admin && scopeType != ScopeTypes.Global) throw new InvalidOperationException("ADMIN_SCOPE_MUST_BE_GLOBAL");
    }
}
