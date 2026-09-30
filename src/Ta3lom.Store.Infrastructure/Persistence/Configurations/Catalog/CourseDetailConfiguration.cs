using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `course_detail` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CourseDetailConfiguration : IEntityTypeConfiguration<CourseDetail>
{
    public void Configure(EntityTypeBuilder<CourseDetail> e)
    {
        e.ToTable("course_detail", t =>
        {
            t.HasCheckConstraint("course_detail_access_days_check", "((access_days IS NULL) OR (access_days > 0))");
            t.HasCheckConstraint("course_detail_check", "((compare_at_price IS NULL) OR (compare_at_price > price))");
            t.HasCheckConstraint("course_detail_duration_minutes_check", "(duration_minutes >= 0)");
            t.HasCheckConstraint("course_detail_lessons_check", "(lessons >= 0)");
            t.HasCheckConstraint("course_detail_price_check", "(price >= (0)::numeric)");
        });
        e.HasKey(x => x.ProductId).HasName("course_detail_pkey");
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Price).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.CompareAtPrice).HasColumnType("numeric(12,2)").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Lessons).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.DurationMinutes).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.LevelAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.LevelEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.InstructorAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.InstructorEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.AccessDays).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.LmsCourseRef).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.LearnerCount).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.HasOne<Product>().WithOne().HasForeignKey<CourseDetail>(x => x.ProductId).HasConstraintName("course_detail_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
