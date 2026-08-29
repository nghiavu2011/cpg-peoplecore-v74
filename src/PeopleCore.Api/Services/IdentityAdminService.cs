using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services;

public sealed class IdentityAdminService(PeopleCoreDbContext db, ICurrentUser current, IAuditService audit)
{
    public async Task<IReadOnlyList<object>> SearchIdentityLinksAsync(string? query, CancellationToken ct)
    {
        var q = from link in db.EmployeeIdentities.AsNoTracking()
                join employee in db.Employees.AsNoTracking() on link.EmployeeId equals employee.Id
                select new { link, employee };
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            q = q.Where(x => x.employee.StaffCode.ToLower().Contains(term) || x.employee.CorporateEmail.ToLower().Contains(term));
        }
        return await q.OrderBy(x => x.employee.StaffCode).Take(100)
            .Select(x => (object)new { x.employee.Id, x.employee.StaffCode, x.employee.CorporateEmail, x.link.EntraTenantId, x.link.EntraObjectId, x.link.LinkedEmail, x.link.IsActive, x.link.LinkedAt, x.link.RevokedAt })
            .ToListAsync(ct);
    }

    public async Task UpsertIdentityLinkAsync(string staffCode, LinkIdentityRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        staffCode = staffCode.Trim().ToUpperInvariant();
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.StaffCode == staffCode, ct) ?? throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");
        var tenantId = req.EntraTenantId.Trim();
        var objectId = req.EntraObjectId.Trim();

        var sameIdentity = await db.EmployeeIdentities.SingleOrDefaultAsync(x => x.EntraTenantId == tenantId && x.EntraObjectId == objectId, ct);
        if (sameIdentity is not null && sameIdentity.EmployeeId != employee.Id)
            throw new InvalidOperationException("ENTRA_IDENTITY_ALREADY_LINKED");

        var currentActive = await db.EmployeeIdentities.SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.IsActive, ct);
        if (currentActive is not null && (sameIdentity is null || currentActive.Id != sameIdentity.Id))
        {
            currentActive.IsActive = false;
            currentActive.RevokedAt = DateTimeOffset.UtcNow;
        }

        EmployeeIdentity link;
        if (sameIdentity is not null)
        {
            link = sameIdentity;
            link.IsActive = true;
            link.RevokedAt = null;
            link.LinkedAt = DateTimeOffset.UtcNow;
            link.LinkedEmail = employee.CorporateEmail;
        }
        else
        {
            link = new EmployeeIdentity
            {
                Id = Guid.NewGuid(), EmployeeId = employee.Id, EntraTenantId = tenantId, EntraObjectId = objectId,
                LinkedEmail = employee.CorporateEmail, IsActive = true, LinkedAt = DateTimeOffset.UtcNow
            };
            db.EmployeeIdentities.Add(link);
        }

        audit.Record("IDENTITY_LINK_UPSERTED", "EmployeeIdentity", link.Id.ToString(), new { employee.StaffCode, req.Reason });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RevokeIdentityLinkAsync(string staffCode, RevokeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        staffCode = staffCode.Trim().ToUpperInvariant();
        var employeeId = await db.Employees.Where(x => x.StaffCode == staffCode).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (employeeId is null) return false;
        var link = await db.EmployeeIdentities.SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive, ct);
        if (link is null) return false;
        link.IsActive = false; link.RevokedAt = DateTimeOffset.UtcNow;
        audit.Record("IDENTITY_LINK_REVOKED", "EmployeeIdentity", link.Id.ToString(), new { staffCode, req.Reason });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<object>> ListGrantsAsync(Guid? employeeId, CancellationToken ct)
    {
        var q = db.AuthorizationGrants.AsNoTracking().AsQueryable();
        if (employeeId is not null) q = q.Where(x => x.EmployeeId == employeeId);
        return await q.OrderByDescending(x => x.StartsAt).Take(200)
            .Select(x => (object)new { x.Id, x.EmployeeId, x.RoleCode, x.ScopeType, x.ScopeValue, x.StartsAt, x.EndsAt, x.Reason, x.GrantedBy, x.RevokedAt })
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateGrantAsync(CreateAccessGrantRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        if (req.EndsAt is not null && req.EndsAt <= req.StartsAt) throw new InvalidOperationException("INVALID_EFFECTIVE_RANGE");
        AccessGrantRules.Validate(req.RoleCode, req.ScopeType, req.ScopeValue);
        if (!await db.Employees.AnyAsync(x => x.Id == req.EmployeeId, ct)) throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");

        var role = req.RoleCode.Trim().ToUpperInvariant();
        var scopeType = req.ScopeType.Trim().ToUpperInvariant();
        var scopeValue = string.IsNullOrWhiteSpace(req.ScopeValue) ? null : req.ScopeValue.Trim().ToUpperInvariant();
        var duplicate = await db.AuthorizationGrants.AnyAsync(x => x.EmployeeId == req.EmployeeId
            && x.RoleCode == role && x.ScopeType == scopeType && x.ScopeValue == scopeValue && x.RevokedAt == null
            && (x.EndsAt == null || x.EndsAt > req.StartsAt) && (req.EndsAt == null || x.StartsAt < req.EndsAt), ct);
        if (duplicate) throw new InvalidOperationException("OVERLAPPING_AUTHORIZATION_GRANT");

        var grant = new AuthorizationGrant
        {
            Id = Guid.NewGuid(), EmployeeId = req.EmployeeId, RoleCode = role, ScopeType = scopeType,
            ScopeValue = scopeValue, StartsAt = req.StartsAt, EndsAt = req.EndsAt,
            Reason = req.Reason.Trim(), GrantedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED"
        };
        db.AuthorizationGrants.Add(grant);
        audit.Record("AUTHORIZATION_GRANT_CREATED", "AuthorizationGrant", grant.Id.ToString(), new { grant.EmployeeId, grant.RoleCode, grant.ScopeType, grant.ScopeValue, req.Reason });
        await db.SaveChangesAsync(ct);
        return grant.Id;
    }

    public async Task<bool> RevokeGrantAsync(Guid id, RevokeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var grant = await db.AuthorizationGrants.SingleOrDefaultAsync(x => x.Id == id && x.RevokedAt == null, ct);
        if (grant is null) return false;
        grant.RevokedAt = DateTimeOffset.UtcNow;
        audit.Record("AUTHORIZATION_GRANT_REVOKED", "AuthorizationGrant", grant.Id.ToString(), new { req.Reason });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
