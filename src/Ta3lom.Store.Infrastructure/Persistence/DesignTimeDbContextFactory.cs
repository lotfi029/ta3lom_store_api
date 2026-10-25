using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ta3lom.Store.Infrastructure.Persistence;

/// Used by `dotnet ef` only (migrations add/script); the web host is never started for design work.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    public StoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>();
        StoreDbContextOptions.Configure(options, "Host=localhost;Database=ta3lom_store_design;Search Path=store,public");
        return new StoreDbContext(options.Options);
    }
}
