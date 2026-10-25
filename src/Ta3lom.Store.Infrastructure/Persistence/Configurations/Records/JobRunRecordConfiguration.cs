using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Infrastructure.Persistence.Records;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Records;

/// `job_run`: (job, fire_time) claimed once across instances.
internal sealed class JobRunRecordConfiguration : IEntityTypeConfiguration<JobRunRecord>
{
    public void Configure(EntityTypeBuilder<JobRunRecord> e)
    {
        e.ToTable("job_run");
        e.HasKey(x => new { x.Job, x.FireTime }).HasName("pk_job_run");
        e.Property(x => x.Job).HasColumnType("text");
        e.Property(x => x.StartedAt).IsRequired();
    }
}
