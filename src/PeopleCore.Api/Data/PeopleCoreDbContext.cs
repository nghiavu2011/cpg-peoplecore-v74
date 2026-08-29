using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Domain;

namespace PeopleCore.Api.Data;

public sealed class PeopleCoreDbContext(DbContextOptions<PeopleCoreDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeePrivateProfile> EmployeePrivateProfiles => Set<EmployeePrivateProfile>();
    public DbSet<EmployeeAssignment> EmployeeAssignments => Set<EmployeeAssignment>();
    public DbSet<EmployeeIdentity> EmployeeIdentities => Set<EmployeeIdentity>();
    public DbSet<AuthorizationGrant> AuthorizationGrants => Set<AuthorizationGrant>();
    public DbSet<MigrationBatch> MigrationBatches => Set<MigrationBatch>();
    public DbSet<MigrationRow> MigrationRows => Set<MigrationRow>();
    public DbSet<IdentityMappingCandidate> IdentityMappingCandidates => Set<IdentityMappingCandidate>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<IntegrationMessage> IntegrationMessages => Set<IntegrationMessage>();
    public DbSet<ProjectCode> ProjectCodes => Set<ProjectCode>();
    public DbSet<CompensationHandoff> CompensationHandoffs => Set<CompensationHandoff>();
    public DbSet<BravoTransportEvidence> BravoTransportEvidence => Set<BravoTransportEvidence>();
    public DbSet<PayrollOfficialResult> PayrollOfficialResults => Set<PayrollOfficialResult>();
    public DbSet<ShadowPayrollResult> ShadowPayrollResults => Set<ShadowPayrollResult>();
    public DbSet<ReconciliationItem> ReconciliationItems => Set<ReconciliationItem>();
    public DbSet<PilotRun> PilotRuns => Set<PilotRun>();
    public DbSet<PilotCheck> PilotChecks => Set<PilotCheck>();
    public DbSet<HrPilotRun> HrPilotRuns => Set<HrPilotRun>();
    public DbSet<HrPilotParticipant> HrPilotParticipants => Set<HrPilotParticipant>();
    public DbSet<HrPilotCheck> HrPilotChecks => Set<HrPilotCheck>();
    public DbSet<HrPilotScenarioEvidence> HrPilotScenarioEvidence => Set<HrPilotScenarioEvidence>();
    public DbSet<PayrollParallelRun> PayrollParallelRuns => Set<PayrollParallelRun>();
    public DbSet<PayrollParallelOfficialResult> PayrollParallelOfficialResults => Set<PayrollParallelOfficialResult>();
    public DbSet<PayrollParallelShadowResult> PayrollParallelShadowResults => Set<PayrollParallelShadowResult>();
    public DbSet<PayrollParallelSnapshot> PayrollParallelSnapshots => Set<PayrollParallelSnapshot>();
    public DbSet<PayrollParallelVariance> PayrollParallelVariances => Set<PayrollParallelVariance>();
    public DbSet<PayrollParallelVarianceResolution> PayrollParallelVarianceResolutions => Set<PayrollParallelVarianceResolution>();
    public DbSet<PayrollParallelCheck> PayrollParallelChecks => Set<PayrollParallelCheck>();
    public DbSet<E2eUatRun> E2eUatRuns => Set<E2eUatRun>();
    public DbSet<E2eUatParticipant> E2eUatParticipants => Set<E2eUatParticipant>();
    public DbSet<E2eUatScenarioEvidence> E2eUatScenarioEvidence => Set<E2eUatScenarioEvidence>();
    public DbSet<E2eUatDefect> E2eUatDefects => Set<E2eUatDefect>();
    public DbSet<E2eUatDefectResolution> E2eUatDefectResolutions => Set<E2eUatDefectResolution>();
    public DbSet<E2eUatSignoff> E2eUatSignoffs => Set<E2eUatSignoff>();
    public DbSet<E2eUatCheck> E2eUatChecks => Set<E2eUatCheck>();
    public DbSet<ProductionCutoverRun> ProductionCutoverRuns => Set<ProductionCutoverRun>();
    public DbSet<ProductionCutoverStepEvidence> ProductionCutoverStepEvidence => Set<ProductionCutoverStepEvidence>();
    public DbSet<ProductionCutoverDecision> ProductionCutoverDecisions => Set<ProductionCutoverDecision>();
    public DbSet<ProductionCutoverSignoff> ProductionCutoverSignoffs => Set<ProductionCutoverSignoff>();
    public DbSet<ProductionCutoverCheck> ProductionCutoverChecks => Set<ProductionCutoverCheck>();
    public DbSet<FunctionalEvidence> FunctionalEvidence => Set<FunctionalEvidence>();
    public DbSet<EvidenceArtifact> EvidenceArtifacts => Set<EvidenceArtifact>();
    public DbSet<EmploymentContract> EmploymentContracts => Set<EmploymentContract>();
    public DbSet<EmployeeLifecycleEvent> EmployeeLifecycleEvents => Set<EmployeeLifecycleEvent>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AttendanceDay> AttendanceDays => Set<AttendanceDay>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<TimesheetEntry> TimesheetEntries => Set<TimesheetEntry>();
    public DbSet<PerformancePeriod> PerformancePeriods => Set<PerformancePeriod>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();
    public DbSet<TaxInsuranceSnapshot> TaxInsuranceSnapshots => Set<TaxInsuranceSnapshot>();
    public DbSet<PayslipRelease> PayslipReleases => Set<PayslipRelease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("peoplecore");

        modelBuilder.Entity<Employee>(e =>
        {
            e.ToTable("employee");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StaffCode).IsUnique();
            e.HasIndex(x => x.CorporateEmail).IsUnique();
            e.Property(x => x.RowVersion).IsConcurrencyToken();
            e.HasOne(x => x.PrivateProfile).WithOne().HasForeignKey<EmployeePrivateProfile>(x => x.EmployeeId);
        });

        modelBuilder.Entity<EmployeePrivateProfile>(e =>
        {
            e.ToTable("employee_private_profile");
            e.HasKey(x => x.EmployeeId);
        });

        modelBuilder.Entity<EmployeeAssignment>(e =>
        {
            e.ToTable("employee_assignment");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
            e.HasIndex(x => new { x.ManagerEmployeeId, x.EffectiveFrom });
        });

        modelBuilder.Entity<EmployeeIdentity>(e =>
        {
            e.ToTable("employee_identity");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntraTenantId, x.EntraObjectId }).IsUnique();
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId);
        });

        modelBuilder.Entity<AuthorizationGrant>(e =>
        {
            e.ToTable("authorization_grant");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeId, x.RoleCode, x.ScopeType, x.ScopeValue });
        });

        modelBuilder.Entity<MigrationBatch>(e =>
        {
            e.ToTable("migration_batch");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BatchKind, x.SourceSha256 }).IsUnique();
        });

        modelBuilder.Entity<MigrationRow>(e =>
        {
            e.ToTable("migration_row");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BatchId, x.RowNumber }).IsUnique();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.NormalizedJson).HasColumnType("jsonb");
            e.Property(x => x.ErrorsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<IdentityMappingCandidate>(e =>
        {
            e.ToTable("identity_mapping_candidate");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BatchId, x.EmployeeId });
            e.HasIndex(x => new { x.EntraTenantId, x.EntraObjectId });
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<IntegrationMessage>(e =>
        {
            e.ToTable("integration_message");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.Integration, x.Direction, x.Status, x.CreatedAt });
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ProjectCode>(e =>
        {
            e.ToTable("project_code");
            e.HasKey(x => x.Code);
            e.HasIndex(x => new { x.Status, x.ValidFrom, x.ValidTo });
            e.HasIndex(x => x.ParentCode);
        });

        modelBuilder.Entity<CompensationHandoff>(e =>
        {
            e.ToTable("compensation_handoff");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom, x.ApprovalReference }).IsUnique();
            e.HasIndex(x => x.IntegrationMessageId).IsUnique();
        });

        modelBuilder.Entity<BravoTransportEvidence>(e =>
        {
            e.ToTable("bravo_transport_evidence");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.IntegrationMessageId, x.CreatedAt });
            e.HasIndex(x => new { x.Direction, x.MessageType, x.CreatedAt });
        });

        modelBuilder.Entity<PayrollOfficialResult>(e =>
        {
            e.ToTable("payroll_official_result");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeId, x.PayrollPeriod }).IsUnique();
            e.Property(x => x.ComponentsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ShadowPayrollResult>(e =>
        {
            e.ToTable("shadow_payroll_result");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeId, x.PayrollPeriod }).IsUnique();
            e.Property(x => x.ComponentsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ReconciliationItem>(e =>
        {
            e.ToTable("reconciliation_item");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeId, x.PayrollPeriod, x.ComponentCode }).IsUnique();
        });


        modelBuilder.Entity<PilotRun>(e =>
        {
            e.ToTable("pilot_run");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartedAt);
        });

        modelBuilder.Entity<PilotCheck>(e =>
        {
            e.ToTable("pilot_check");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PilotRunId, x.CheckCode, x.CheckedAt });
        });


        modelBuilder.Entity<HrPilotRun>(e =>
        {
            e.ToTable("hr_pilot_run");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartedAt);
        });
        modelBuilder.Entity<HrPilotParticipant>(e =>
        {
            e.ToTable("hr_pilot_participant");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.HrPilotRunId, x.StaffCode }).IsUnique();
        });
        modelBuilder.Entity<HrPilotCheck>(e =>
        {
            e.ToTable("hr_pilot_check");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.HrPilotRunId, x.CheckCode, x.CheckedAt });
        });
        modelBuilder.Entity<HrPilotScenarioEvidence>(e =>
        {
            e.ToTable("hr_pilot_scenario_evidence");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.HrPilotRunId, x.ScenarioCode, x.RecordedAt });
        });
        modelBuilder.Entity<PayrollParallelRun>(e =>
        {
            e.ToTable("payroll_parallel_run"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.PayrollPeriod, x.IterationNo }).IsUnique();
        });
        modelBuilder.Entity<PayrollParallelOfficialResult>(e =>
        {
            e.ToTable("payroll_parallel_official_result"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.PayrollParallelRunId, x.EmployeeId }).IsUnique(); e.Property(x => x.ComponentsJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<PayrollParallelShadowResult>(e =>
        {
            e.ToTable("payroll_parallel_shadow_result"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.PayrollParallelRunId, x.EmployeeId }).IsUnique(); e.Property(x => x.ComponentsJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<PayrollParallelSnapshot>(e =>
        {
            e.ToTable("payroll_parallel_snapshot"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.PayrollParallelRunId, x.EmployeeId }).IsUnique();
        });
        modelBuilder.Entity<PayrollParallelVariance>(e =>
        {
            e.ToTable("payroll_parallel_variance"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.SnapshotId, x.ComponentCode }).IsUnique();
        });
        modelBuilder.Entity<PayrollParallelVarianceResolution>(e =>
        {
            e.ToTable("payroll_parallel_variance_resolution"); e.HasKey(x => x.Id); e.HasIndex(x => x.PayrollParallelVarianceId).IsUnique();
        });
        modelBuilder.Entity<PayrollParallelCheck>(e =>
        {
            e.ToTable("payroll_parallel_check"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.PayrollParallelRunId, x.CheckCode, x.CheckedAt });
        });
        modelBuilder.Entity<E2eUatRun>(e =>
        {
            e.ToTable("e2e_uat_run"); e.HasKey(x => x.Id); e.HasIndex(x => x.StartedAt); e.HasIndex(x => x.ReleaseCandidate);
        });
        modelBuilder.Entity<E2eUatParticipant>(e =>
        {
            e.ToTable("e2e_uat_participant"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.E2eUatRunId, x.StaffCode }).IsUnique();
        });
        modelBuilder.Entity<E2eUatScenarioEvidence>(e =>
        {
            e.ToTable("e2e_uat_scenario_evidence"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.E2eUatRunId, x.ScenarioCode, x.RecordedAt });
        });
        modelBuilder.Entity<E2eUatDefect>(e =>
        {
            e.ToTable("e2e_uat_defect"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.E2eUatRunId, x.DefectCode }).IsUnique();
        });
        modelBuilder.Entity<E2eUatDefectResolution>(e =>
        {
            e.ToTable("e2e_uat_defect_resolution"); e.HasKey(x => x.Id); e.HasIndex(x => x.E2eUatDefectId);
        });
        modelBuilder.Entity<E2eUatSignoff>(e =>
        {
            e.ToTable("e2e_uat_signoff"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.E2eUatRunId, x.SignoffRole, x.SignedAt });
        });
        modelBuilder.Entity<E2eUatCheck>(e =>
        {
            e.ToTable("e2e_uat_check"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.E2eUatRunId, x.CheckCode, x.CheckedAt });
        });
        modelBuilder.Entity<ProductionCutoverRun>(e =>
        {
            e.ToTable("production_cutover_run"); e.HasKey(x => x.Id); e.HasIndex(x => x.StartedAt); e.HasIndex(x => x.ReleaseCandidate);
        });
        modelBuilder.Entity<ProductionCutoverStepEvidence>(e =>
        {
            e.ToTable("production_cutover_step_evidence"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProductionCutoverRunId, x.StepCode, x.RecordedAt });
        });
        modelBuilder.Entity<ProductionCutoverDecision>(e =>
        {
            e.ToTable("production_cutover_decision"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProductionCutoverRunId, x.DecidedAt });
        });
        modelBuilder.Entity<ProductionCutoverSignoff>(e =>
        {
            e.ToTable("production_cutover_signoff"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProductionCutoverRunId, x.SignoffRole, x.SignedAt });
        });
        modelBuilder.Entity<ProductionCutoverCheck>(e =>
        {
            e.ToTable("production_cutover_check"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProductionCutoverRunId, x.CheckCode, x.CheckedAt });
        });
        modelBuilder.Entity<FunctionalEvidence>(e =>
        {
            e.ToTable("functional_evidence"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ScenarioCode, x.CreatedAt }); e.HasIndex(x => new { x.EmployeeId, x.CreatedAt });
        });
        modelBuilder.Entity<EvidenceArtifact>(e =>
        {
            e.ToTable("evidence_artifact"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ArtifactType, x.ObservedAt });
        });
        modelBuilder.Entity<EmploymentContract>(e =>
        {
            e.ToTable("employment_contract"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
        });
        modelBuilder.Entity<EmployeeLifecycleEvent>(e =>
        {
            e.ToTable("employee_lifecycle_event"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.EffectiveDate });
        });
        modelBuilder.Entity<LeaveRequest>(e =>
        {
            e.ToTable("leave_request"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.StartDate, x.EndDate }); e.HasIndex(x => new { x.Status, x.RequestedAt });
        });
        modelBuilder.Entity<AttendanceDay>(e =>
        {
            e.ToTable("attendance_day"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.WorkDate }).IsUnique(); e.HasIndex(x => new { x.Status, x.WorkDate });
        });
        modelBuilder.Entity<OvertimeRequest>(e =>
        {
            e.ToTable("overtime_request"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.WorkDate }); e.HasIndex(x => new { x.Status, x.RequestedAt });
        });
        modelBuilder.Entity<TimesheetEntry>(e =>
        {
            e.ToTable("timesheet_entry"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.WorkDate }); e.HasIndex(x => new { x.ProjectCode, x.WorkDate });
        });
        modelBuilder.Entity<PerformancePeriod>(e =>
        {
            e.ToTable("performance_period"); e.HasKey(x => x.Id); e.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<PerformanceReview>(e =>
        {
            e.ToTable("performance_review"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.PerformancePeriodId, x.EmployeeId }).IsUnique(); e.HasIndex(x => new { x.ManagerEmployeeId, x.Status });
        });
        modelBuilder.Entity<TaxInsuranceSnapshot>(e =>
        {
            e.ToTable("tax_insurance_snapshot"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.PayrollPeriod, x.IterationNo }).IsUnique(); e.Property(x => x.OutputsJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<PayslipRelease>(e =>
        {
            e.ToTable("payslip_release"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.PayrollPeriod }).IsUnique();
        });

        // V67 runtime defect fix: SQL migrations use unquoted snake_case columns.
        // Apply an internal convention so EF property names resolve to the existing PostgreSQL schema
        // without introducing another runtime package/dependency.
        ApplySnakeCaseColumns(modelBuilder);
    }

    private static void ApplySnakeCaseColumns(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
                chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnlyAudit();
        GuardIntegrationEnvelope();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnlyAudit();
        GuardIntegrationEnvelope();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardAppendOnlyAudit();
        GuardIntegrationEnvelope();
        return base.SaveChangesAsync(cancellationToken);
    }


    private void GuardIntegrationEnvelope()
    {
        foreach (var e in ChangeTracker.Entries<IntegrationMessage>())
        {
            if (e.State == EntityState.Deleted)
                throw new InvalidOperationException("Integration messages are retained evidence and cannot be deleted.");
            if (e.State != EntityState.Modified) continue;
            var immutable = new[] { nameof(IntegrationMessage.Direction), nameof(IntegrationMessage.Integration), nameof(IntegrationMessage.MessageType), nameof(IntegrationMessage.IdempotencyKey), nameof(IntegrationMessage.PayloadJson), nameof(IntegrationMessage.PayloadSha256), nameof(IntegrationMessage.SchemaVersion), nameof(IntegrationMessage.CreatedAt) };
            if (immutable.Any(name => e.Property(name).IsModified))
                throw new InvalidOperationException("Integration envelope fields are immutable after creation.");
        }
    }

    private void GuardAppendOnlyAudit()
    {
        var illegalAuditMutation = ChangeTracker.Entries<AuditEvent>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (illegalAuditMutation)
            throw new InvalidOperationException("Audit events are append-only and cannot be modified or deleted.");

        var illegalPilotEvidenceMutation = ChangeTracker.Entries<PilotCheck>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (illegalPilotEvidenceMutation)
            throw new InvalidOperationException("Pilot UAT check evidence is append-only and cannot be modified or deleted.");


        var illegalBravoTransportEvidenceMutation = ChangeTracker.Entries<BravoTransportEvidence>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (illegalBravoTransportEvidenceMutation)
            throw new InvalidOperationException("BRAVO transport pilot evidence is append-only and cannot be modified or deleted.");


        var illegalV71ParticipantMutation = ChangeTracker.Entries<HrPilotParticipant>().Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        var illegalV71CheckMutation = ChangeTracker.Entries<HrPilotCheck>().Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        var illegalV71ScenarioMutation = ChangeTracker.Entries<HrPilotScenarioEvidence>().Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (illegalV71ParticipantMutation || illegalV71CheckMutation || illegalV71ScenarioMutation)
            throw new InvalidOperationException("V71 HR pilot participant/check/scenario evidence is append-only and cannot be modified or deleted.");

        var illegalV72Mutation = ChangeTracker.Entries().Any(e =>
            (e.State is EntityState.Modified or EntityState.Deleted) &&
            (e.Entity is PayrollParallelOfficialResult || e.Entity is PayrollParallelShadowResult || e.Entity is PayrollParallelSnapshot || e.Entity is PayrollParallelVariance || e.Entity is PayrollParallelVarianceResolution || e.Entity is PayrollParallelCheck));
        if (illegalV72Mutation)
            throw new InvalidOperationException("V72 payroll parallel-run result/variance/check evidence is append-only and cannot be modified or deleted.");

        var illegalV73Mutation = ChangeTracker.Entries().Any(e =>
            (e.State is EntityState.Modified or EntityState.Deleted) &&
            (e.Entity is E2eUatParticipant || e.Entity is E2eUatScenarioEvidence || e.Entity is E2eUatDefect || e.Entity is E2eUatDefectResolution || e.Entity is E2eUatSignoff || e.Entity is E2eUatCheck));
        if (illegalV73Mutation)
            throw new InvalidOperationException("V73 E2E UAT participant/scenario/defect/signoff/check evidence is append-only and cannot be modified or deleted.");

        var illegalV74Mutation = ChangeTracker.Entries().Any(e =>
            (e.State is EntityState.Modified or EntityState.Deleted) &&
            (e.Entity is ProductionCutoverStepEvidence || e.Entity is ProductionCutoverDecision || e.Entity is ProductionCutoverSignoff || e.Entity is ProductionCutoverCheck));
        if (illegalV74Mutation)
            throw new InvalidOperationException("V74 cutover step/decision/signoff/check evidence is append-only and cannot be modified or deleted.");

        var illegalRc2Mutation = ChangeTracker.Entries().Any(e =>
            (e.State is EntityState.Modified or EntityState.Deleted) &&
            (e.Entity is FunctionalEvidence || e.Entity is EvidenceArtifact || e.Entity is EmployeeLifecycleEvent || e.Entity is TaxInsuranceSnapshot || e.Entity is PayslipRelease));
        if (illegalRc2Mutation)
            throw new InvalidOperationException("V74-RC2 functional/lifecycle/payslip evidence is append-only and cannot be modified or deleted.");
    }
}
