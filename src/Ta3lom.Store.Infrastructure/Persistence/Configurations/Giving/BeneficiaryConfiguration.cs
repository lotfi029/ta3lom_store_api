using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `beneficiary` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class BeneficiaryConfiguration : IEntityTypeConfiguration<Beneficiary>
{
    public void Configure(EntityTypeBuilder<Beneficiary> e)
    {
        e.ToTable("beneficiary", t =>
        {
            t.HasCheckConstraint("beneficiary_eligibility_status_check", "(eligibility_status = ANY (ARRAY['pending'::text, 'approved'::text, 'rejected'::text, 'inactive'::text]))");
            t.HasCheckConstraint("beneficiary_kind_check", "(kind = ANY (ARRAY['student'::text, 'teacher'::text, 'parent'::text, 'school'::text, 'library'::text]))");
        });
        e.HasKey(x => x.Id).HasName("beneficiary_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Code).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.BeneficiaryGroup).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Region).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.FullName).HasColumnName("full_name_enc").HasColumnType("bytea").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Mobile).HasColumnName("mobile_enc").HasColumnType("bytea").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.MobileHash).HasColumnType("character(64)").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.GuardianName).HasColumnName("guardian_name_enc").HasColumnType("bytea").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.OrganisationName).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.EligibilityStatus).HasColumnType("text").IsRequired().HasDefaultValueSql("'pending'::text").ValueGeneratedNever();
        e.Property(x => x.ZakatEligible).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.EligibilityNotes).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.VerifiedBy).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.VerifiedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CustomerId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.CreatedBy).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("beneficiary_code_key");
        e.HasIndex(x => new { x.BeneficiaryGroup, x.EligibilityStatus, x.ZakatEligible }).HasDatabaseName("ix_beneficiary_pick");
        e.HasIndex(x => x.MobileHash).IsUnique().HasFilter("(mobile_hash IS NOT NULL)").HasDatabaseName("ux_beneficiary_mobile");
        e.HasOne<Ta3lom.Store.Domain.Identity.Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("beneficiary_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
