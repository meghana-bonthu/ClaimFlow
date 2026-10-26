using ClaimFlow.Domain.Audit;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;
using ClaimFlow.Domain.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimFlow.Infrastructure.Persistence.Configurations;

internal sealed class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> b)
    {
        b.ToTable("Claims");
        b.HasKey(c => c.Id);
        b.Property(c => c.ClaimNumber).HasMaxLength(32).IsRequired();
        b.HasIndex(c => c.ClaimNumber).IsUnique();
        b.Property(c => c.PatientControlNumber).HasMaxLength(38);
        b.HasIndex(c => new { c.ProviderId, c.PatientControlNumber });
        b.HasIndex(c => c.Status);
        b.HasIndex(c => new { c.MemberId, c.ProviderId });
        b.HasIndex(c => c.SubmittedAt);

        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.PendReason).HasMaxLength(1000);
        b.Property(c => c.Version).IsConcurrencyToken();
        b.PrimitiveCollection(c => c.DiagnosisCodes);

        b.HasOne<Member>().WithMany().HasForeignKey(c => c.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Provider>().WithMany().HasForeignKey(c => c.ProviderId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(c => c.Lines).WithOne().HasForeignKey(l => l.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.Findings).WithOne().HasForeignKey(f => f.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.History).WithOne().HasForeignKey(h => h.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(c => c.Findings).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(c => c.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(c => c.ServiceDateFrom);
        b.Ignore(c => c.ServiceDateTo);
    }
}

internal sealed class ClaimLineConfiguration : IEntityTypeConfiguration<ClaimLine>
{
    public void Configure(EntityTypeBuilder<ClaimLine> b)
    {
        b.ToTable("ClaimLines");
        b.HasKey(l => l.Id);
        b.Property(l => l.ProcedureCode).HasMaxLength(10).IsRequired();
        b.Property(l => l.Modifier).HasMaxLength(2);
        b.Property(l => l.DenialCode).HasMaxLength(10);
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(l => new { l.ClaimId, l.LineNumber }).IsUnique();
        b.HasIndex(l => new { l.ProcedureCode, l.ServiceDate });
        b.Ignore(l => l.MemberResponsibility);
    }
}

internal sealed class FindingConfiguration : IEntityTypeConfiguration<AdjudicationFinding>
{
    public void Configure(EntityTypeBuilder<AdjudicationFinding> b)
    {
        b.ToTable("AdjudicationFindings");
        b.HasKey(f => f.Id);
        b.Property(f => f.RuleName).HasMaxLength(64);
        b.Property(f => f.ReasonCode).HasMaxLength(10);
        b.Property(f => f.Message).HasMaxLength(1000);
        b.Property(f => f.Outcome).HasConversion<string>().HasMaxLength(10);
    }
}

internal sealed class StatusChangeConfiguration : IEntityTypeConfiguration<ClaimStatusChange>
{
    public void Configure(EntityTypeBuilder<ClaimStatusChange> b)
    {
        b.ToTable("ClaimStatusHistory");
        b.HasKey(h => h.Id);
        b.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.Note).HasMaxLength(1000);
        b.HasIndex(h => new { h.ClaimId, h.ToStatus });
    }
}

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> b)
    {
        b.ToTable("Members");
        b.HasKey(m => m.Id);
        b.Property(m => m.MemberNumber).HasMaxLength(20).IsRequired();
        b.HasIndex(m => m.MemberNumber).IsUnique();
        b.Property(m => m.FirstName).HasMaxLength(60);
        b.Property(m => m.LastName).HasMaxLength(60);
        b.HasOne(m => m.Plan).WithMany().HasForeignKey(m => m.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(m => m.FullName);
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<HealthPlan>
{
    public void Configure(EntityTypeBuilder<HealthPlan> b)
    {
        b.ToTable("HealthPlans");
        b.HasKey(p => p.Id);
        b.Property(p => p.Code).HasMaxLength(20);
        b.HasIndex(p => p.Code).IsUnique();
        b.Property(p => p.CoinsuranceRate).HasPrecision(5, 4);
    }
}

internal sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> b)
    {
        b.ToTable("Providers");
        b.HasKey(p => p.Id);
        b.Property(p => p.Npi).HasMaxLength(10).IsFixedLength().IsRequired();
        b.HasIndex(p => p.Npi).IsUnique();
    }
}

internal sealed class FeeScheduleConfiguration : IEntityTypeConfiguration<FeeScheduleEntry>
{
    public void Configure(EntityTypeBuilder<FeeScheduleEntry> b)
    {
        b.ToTable("FeeSchedule");
        b.HasKey(f => f.Id);
        b.Property(f => f.ProcedureCode).HasMaxLength(10);
        b.HasIndex(f => f.ProcedureCode).IsUnique();
    }
}

internal sealed class PriorAuthorizationConfiguration : IEntityTypeConfiguration<PriorAuthorization>
{
    public void Configure(EntityTypeBuilder<PriorAuthorization> b)
    {
        b.ToTable("PriorAuthorizations");
        b.HasKey(a => a.Id);
        b.Property(a => a.ProcedureCode).HasMaxLength(10);
        b.Property(a => a.AuthorizationNumber).HasMaxLength(30);
        b.HasIndex(a => a.MemberId);
        b.HasOne<Member>().WithMany().HasForeignKey(a => a.MemberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditLog");
        b.HasKey(a => a.Id);
        b.Property(a => a.EntityType).HasMaxLength(40);
        b.Property(a => a.EntityKey).HasMaxLength(64);
        b.Property(a => a.Action).HasMaxLength(40);
        b.Property(a => a.Actor).HasMaxLength(128);
        b.Property(a => a.Details).HasMaxLength(1000);
        b.HasIndex(a => new { a.EntityType, a.EntityKey });
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.Id);
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.Role).HasMaxLength(20);
        b.Property(u => u.PasswordHash).HasMaxLength(512);
    }
}
