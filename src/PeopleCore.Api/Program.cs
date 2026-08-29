using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PeopleCore.Api.Data;
using PeopleCore.Api.Health;
using PeopleCore.Api.Runtime;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;
using PeopleCore.Api.Services.Audit;
using PeopleCore.Api.Services.Bravo;
using PeopleCore.Api.Services.Payroll;

using PeopleCore.Api.Services.Functional;
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; o.UseUtcTimestamp = true; });

var database = DatabaseConnectionFactory.Resolve(builder.Configuration);
RuntimeConfigurationValidator.ValidateOrThrow(builder.Configuration, builder.Environment, database);
BravoTransportConfigurationValidator.ValidateOrThrow(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(database);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long>("Runtime:MaxRequestBodyBytes", 10485760);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Runtime:RequestHeadersTimeoutSeconds", 15));
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var id)) context.ProblemDetails.Extensions["correlationId"] = id;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<PeopleCoreDbContext>(options => options.UseNpgsql(database.ConnectionString));

var trialAuthEnabled = builder.Environment.IsEnvironment("Trial") && builder.Configuration.GetValue<bool>("TrialAuth:Enabled");
if (trialAuthEnabled)
{
    builder.Services.AddAuthentication(TrialAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, TrialAuthenticationHandler>(TrialAuthenticationHandler.SchemeName, _ => { });
}
else
{
    var tenantId = builder.Configuration["Entra:TenantId"]!;
    var clientId = builder.Configuration["Entra:ClientId"]!;
    var authorityBase = builder.Configuration["Entra:AuthorityBase"] ?? "https://login.microsoftonline.com";
    var requiredScope = builder.Configuration["Entra:RequiredScope"] ?? "PeopleCore.Access";
    var allowedClientIds = (builder.Configuration["Entra:AllowedClientIds"] ?? string.Empty)
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Authority = $"{authorityBase.TrimEnd('/')}/{tenantId}/v2.0";
        options.Audience = clientId;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromMinutes(2)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var tokenTenant = context.Principal?.FindFirst("tid")?.Value;
                var tokenObject = context.Principal?.FindFirst("oid")?.Value;
                if (!string.Equals(tokenTenant, tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("Entra tenant claim does not match configured PeopleCore tenant.");
                    return Task.CompletedTask;
                }
                if (!Guid.TryParse(tokenObject, out _))
                {
                    context.Fail("Entra object-id claim is required for PeopleCore identity mapping.");
                    return Task.CompletedTask;
                }
                var actorClient = context.Principal?.FindFirst("azp")?.Value;
                if (string.IsNullOrWhiteSpace(actorClient) || !allowedClientIds.Contains(actorClient))
                {
                    context.Fail("Calling Entra client application is not approved for PeopleCore.");
                    return Task.CompletedTask;
                }
                var scopes = (context.Principal?.FindFirst("scp")?.Value ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!scopes.Contains(requiredScope, StringComparer.Ordinal))
                {
                    context.Fail("Required delegated PeopleCore API scope is missing.");
                    return Task.CompletedTask;
                }
                return Task.CompletedTask;
            }
        };
    });
}

builder.Services.AddScoped<IClaimsTransformation, PeopleCoreClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationHandler, ActiveEmployeeHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, InternalRoleHandler>();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new ActiveEmployeeRequirement()).Build();
    options.AddPolicy(Policies.PeopleCoreUser, p => p.RequireAuthenticatedUser().AddRequirements(new ActiveEmployeeRequirement()));
    options.AddPolicy(Policies.Hr, p => p.AddRequirements(new InternalRoleRequirement(Roles.Hr)));
    options.AddPolicy(Policies.Payroll, p => p.AddRequirements(new InternalRoleRequirement(Roles.Payroll)));
    options.AddPolicy(Policies.PlatformAdmin, p => p.AddRequirements(new InternalRoleRequirement(Roles.Admin)));
    options.AddPolicy(Policies.HrOrAdmin, p => p.RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == PeopleCoreClaims.Role && (c.Value == Roles.Hr || c.Value == Roles.Admin) && c.Issuer == PeopleCoreClaims.Issuer)));
    options.AddPolicy(Policies.HrOrPayroll, p => p.RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == PeopleCoreClaims.Role && (c.Value == Roles.Hr || c.Value == Roles.Payroll) && c.Issuer == PeopleCoreClaims.Issuer)));
    options.AddPolicy(Policies.EvidenceRegistrar, p => p.RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == PeopleCoreClaims.Role && (c.Value == Roles.Hr || c.Value == Roles.Payroll || c.Value == Roles.Admin || c.Value == Roles.Leadership) && c.Issuer == PeopleCoreClaims.Issuer)));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permit = builder.Configuration.GetValue("Runtime:RateLimitPermitLimit", 120);
    var window = TimeSpan.FromSeconds(builder.Configuration.GetValue("Runtime:RateLimitWindowSeconds", 60));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        if (http.Request.Path.StartsWithSegments("/health")) return RateLimitPartition.GetNoLimiter("health");
        var key = http.User.FindFirst("oid")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = window, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst });
    });
});

builder.Services.AddHealthChecks()
    .AddCheck<RuntimeBoundaryHealthCheck>("runtime-boundaries", tags: new[] { "ready", "startup" })
    .AddCheck<DatabaseConnectivityHealthCheck>("postgres-connectivity", tags: new[] { "ready", "startup" })
    .AddCheck<DatabaseSchemaHealthCheck>("postgres-schema", tags: new[] { "startup" })
    .AddCheck<BravoAdapterPilotHealthCheck>("bravo-adapter-pilot", tags: new[] { "ready", "startup" })
    .AddCheck<DiskSpaceHealthCheck>("disk-space", tags: new[] { "ready" });

var knownProxies = RuntimeConfigurationValidator.KnownProxies(builder.Configuration);
if (knownProxies.Count > 0) builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
    foreach (var proxy in knownProxies) options.KnownProxies.Add(proxy);
});

builder.Services.AddHsts(o => { o.IncludeSubDomains = true; o.MaxAge = TimeSpan.FromDays(builder.Configuration.GetValue("Runtime:HstsDays", 180)); });
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IAccessControlService, AccessControlService>();
builder.Services.AddScoped<EmployeeMasterService>();
builder.Services.AddScoped<IdentityAdminService>();
builder.Services.AddScoped<EmployeeMigrationService>();
builder.Services.AddScoped<IdentityMappingPilotService>();
builder.Services.AddScoped<PilotUatService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<RuntimeStatusService>();
builder.Services.AddScoped<V70RuntimeProofService>();
builder.Services.AddScoped<V71HrPilotService>();
builder.Services.AddScoped<V72PayrollParallelRunService>();
builder.Services.AddScoped<V73E2eUatService>();
builder.Services.AddScoped<V74ProductionCutoverService>();
builder.Services.AddScoped<FunctionalEvidenceService>();
builder.Services.AddScoped<EvidenceArtifactService>();
builder.Services.AddScoped<ContractLifecycleService>();
builder.Services.AddScoped<LeaveService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<OvertimeService>();
builder.Services.AddScoped<TimesheetService>();
builder.Services.AddScoped<PerformanceService>();
builder.Services.AddScoped<TaxInsuranceService>();
builder.Services.AddScoped<PayslipService>();

// Locked Hybrid background remains unchanged: BRAVO is official Phase 1-2; Shadow is validation-only.
builder.Services.AddScoped<IBravoAdapter, BravoAdapterStub>();
builder.Services.AddScoped<BravoIntegrationService>();
var bravoMappingMode = (builder.Configuration["Bravo:MappingMode"] ?? V68BravoModes.MappingNotConfigured).ToUpperInvariant();
builder.Services.AddScoped<IBravoPayloadMapper>(sp => bravoMappingMode == V68BravoModes.MappingCanonicalFixtureOnly
    ? ActivatorUtilities.CreateInstance<CanonicalFixtureBravoPayloadMapper>(sp)
    : ActivatorUtilities.CreateInstance<NotConfiguredBravoPayloadMapper>(sp));
var bravoTransportMode = (builder.Configuration["Bravo:TransportPilot:Mode"] ?? V68BravoModes.TransportNotConfigured).ToUpperInvariant();
builder.Services.AddScoped<IBravoMachineTransport>(sp => bravoTransportMode == V68BravoModes.TransportSignedDryRun
    ? ActivatorUtilities.CreateInstance<SignedDryRunBravoMachineTransport>(sp)
    : ActivatorUtilities.CreateInstance<NotConfiguredBravoMachineTransport>(sp));
builder.Services.AddScoped<BravoAdapterPilotService>();
builder.Services.AddScoped<IShadowPayrollEngine, ShadowPayrollEngine>();
builder.Services.AddScoped<IPayrollReconciliationService, PayrollReconciliationService>();

var app = builder.Build();
if (knownProxies.Count > 0) app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-PeopleCore-Version"] = builder.Configuration["Product:Version"] ?? "V74-RC2";
    context.Response.Headers["X-PeopleCore-Mode"] = builder.Environment.EnvironmentName;
    if (trialAuthEnabled) context.Response.Headers["X-PeopleCore-Trial"] = "LOCAL_ONLY_NOT_PRODUCTION_EVIDENCE";
    await next();
});
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<DomainExceptionMiddleware>();
if (builder.Configuration.GetValue<bool>("Runtime:UseHsts")) app.UseHsts();
if (builder.Configuration.GetValue<bool>("Runtime:UseHttpsRedirection")) app.UseHttpsRedirection();
if (trialAuthEnabled)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = V66HealthResponse.WriteAsync }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready"), ResponseWriter = V66HealthResponse.WriteAsync }).AllowAnonymous();
app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = r => r.Tags.Contains("startup"), ResponseWriter = V66HealthResponse.WriteAsync }).AllowAnonymous();
app.MapControllers();
app.Run();

public partial class Program { }
