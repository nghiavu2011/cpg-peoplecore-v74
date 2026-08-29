namespace PeopleCore.Api.Contracts;

public sealed record EmployeeWorkDto(
    Guid Id,
    string StaffCode,
    string CorporateEmail,
    string DisplayName,
    string CompanyCode,
    string DepartmentCode,
    string OfficeCode,
    string PositionTitle,
    string? GradeCode,
    Guid? ManagerEmployeeId,
    string TimesheetPolicy,
    string EmploymentStatus,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long RowVersion);

public sealed record EmployeeSelfDto(string? PersonalEmail, string? Mobile, DateOnly? DateOfBirth, string? HomeCity, DateOnly? HireDate, DateOnly? LastWorkingDate);
public sealed record EmployeeHrPrivateDto(string? HrPrivateNotes);
public sealed record EmployeeEnvelopeDto(EmployeeWorkDto Work, EmployeeSelfDto? Self, EmployeeHrPrivateDto? HrPrivate, string AccessScope, string[] FieldSets);

public sealed record CreateEmployeeRequest(
    string StaffCode,
    string CorporateEmail,
    string DisplayName,
    string CompanyCode,
    string DepartmentCode,
    string OfficeCode,
    string PositionTitle,
    string? GradeCode,
    Guid? ManagerEmployeeId,
    string TimesheetPolicy,
    DateOnly EffectiveFrom,
    DateOnly? HireDate);

public sealed record PatchEmployeeRequest(
    long ExpectedRowVersion,
    string? CorporateEmail,
    string? DisplayName,
    string? EmploymentStatus,
    DateOnly? HireDate,
    DateOnly? LastWorkingDate,
    string? HrPrivateNotes);

public sealed record CreateAssignmentRequest(
    long ExpectedRowVersion,
    string CompanyCode,
    string DepartmentCode,
    string OfficeCode,
    string PositionTitle,
    string? GradeCode,
    Guid? ManagerEmployeeId,
    string TimesheetPolicy,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record LinkIdentityRequest(string EntraTenantId, string EntraObjectId, string Reason);
public sealed record RevokeRequest(string Reason);
public sealed record CreateAccessGrantRequest(Guid EmployeeId, string RoleCode, string ScopeType, string? ScopeValue, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, string Reason);
