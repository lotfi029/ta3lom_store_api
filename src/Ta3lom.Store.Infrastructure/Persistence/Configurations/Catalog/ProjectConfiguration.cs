using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `project` (BR-A02 … BR-A04, D-06 … D-08). `search_vector` is a stored generated column over the normalised Arabic and
/// English title and summary; its GIN index and the trigram index on the normalised Arabic title are created by raw SQL in
/// the migration.
internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public const string SearchVector = "SearchVector";

    public void Configure(EntityTypeBuilder<Project> e)
    {
        e.ToTable("project", t =>
        {
            t.HasCheckConstraint("ck_project_slug", "slug ~ '^[a-z0-9-]{2,80}$'");
            t.HasCheckConstraint("ck_project_status", "status IN ('draft', 'published', 'archived')");
            t.HasCheckConstraint("ck_project_badge", "badge IS NULL OR badge IN ('new', 'urgent')");
            t.HasCheckConstraint("ck_project_min", "min_amount IS NULL OR min_amount > 0");
            t.HasCheckConstraint("ck_project_title", "char_length(title_ar) BETWEEN 2 AND 160");
            t.HasCheckConstraint("ck_project_summary", "summary_ar IS NULL OR char_length(summary_ar) <= 300");
        });
        e.HasKey(x => x.Id).HasName("pk_project");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Slug).HasColumnType("text").IsRequired();
        e.Property(x => x.Status).HasColumnType("text").IsRequired();
        e.Property(x => x.TitleAr).HasColumnType("text").IsRequired();
        e.Property(x => x.TitleEn).HasColumnType("text");
        e.Property(x => x.SummaryAr).HasColumnType("text");
        e.Property(x => x.SummaryEn).HasColumnType("text");
        e.Property(x => x.BodyAr).HasColumnType("text");
        e.Property(x => x.BodyEn).HasColumnType("text");
        e.Property(x => x.AllowOpenAmount).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.MinAmount);
        e.Property(x => x.Badge).HasColumnType("text");
        e.Property(x => x.Sort).HasDefaultValue(0).ValueGeneratedNever();
        e.Property(x => x.SeoTitleAr).HasColumnType("text");
        e.Property(x => x.SeoTitleEn).HasColumnType("text");
        e.Property(x => x.SeoDescAr).HasColumnType("text");
        e.Property(x => x.SeoDescEn).HasColumnType("text");
        e.Property(x => x.Version).IsRowVersion();
        e.Property<NpgsqlTsVector>(SearchVector).HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple', store.ar_normalize(coalesce(title_ar, '') || ' ' || coalesce(title_en, '') || ' ' || coalesce(summary_ar, '') || ' ' || coalesce(summary_en, '')))",
                stored: true);

        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_project_slug");
        e.HasIndex(x => new { x.Status, x.CategoryId, x.Sort }).HasDatabaseName("ix_project_listing");
        e.HasIndex(x => x.CategoryId).HasDatabaseName("ix_project_category");
        e.HasIndex(x => x.CoverAssetId).HasDatabaseName("ix_project_cover");
        e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).HasConstraintName("fk_project_category").OnDelete(DeleteBehavior.Restrict);
        e.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.CoverAssetId).HasConstraintName("fk_project_cover").OnDelete(DeleteBehavior.Restrict);

        e.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.ProjectId).HasConstraintName("fk_option_project").OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_options");
        e.HasMany(x => x.SlugHistory).WithOne().HasForeignKey(h => h.ProjectId).HasConstraintName("fk_slug_history_project").OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.SlugHistory).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_slugHistory");
    }
}

/// `project_option` (BR-A03): `ux_option_label (project_id, label_ar)`.
internal sealed class ProjectOptionConfiguration : IEntityTypeConfiguration<ProjectOption>
{
    public void Configure(EntityTypeBuilder<ProjectOption> e)
    {
        e.ToTable("project_option", t =>
        {
            t.HasCheckConstraint("ck_option_amount", "amount > 0");
            t.HasCheckConstraint("ck_option_label", "char_length(label_ar) BETWEEN 2 AND 60");
        });
        e.HasKey(x => x.Id).HasName("pk_project_option");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.LabelAr).HasColumnType("text").IsRequired();
        e.Property(x => x.LabelEn).HasColumnType("text");
        e.Property(x => x.Amount).IsRequired();
        e.Property(x => x.Sort).HasDefaultValue(0).ValueGeneratedNever();
        e.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever();
        e.HasIndex(x => new { x.ProjectId, x.LabelAr }).IsUnique().HasDatabaseName("ux_option_label");
    }
}

/// `project_slug_history` (D-08): `old_slug` is unique across all projects.
internal sealed class ProjectSlugHistoryConfiguration : IEntityTypeConfiguration<ProjectSlugHistory>
{
    public void Configure(EntityTypeBuilder<ProjectSlugHistory> e)
    {
        e.ToTable("project_slug_history");
        e.HasKey(x => x.Id).HasName("pk_project_slug_history");
        e.Property(x => x.Id).UseIdentityAlwaysColumn();
        e.Property(x => x.OldSlug).HasColumnType("text").IsRequired();
        e.Property(x => x.ChangedAt).IsRequired();
        e.HasIndex(x => x.OldSlug).IsUnique().HasDatabaseName("ux_slug_history_old");
        e.HasIndex(x => x.ProjectId).HasDatabaseName("ix_slug_history_project");
    }
}
