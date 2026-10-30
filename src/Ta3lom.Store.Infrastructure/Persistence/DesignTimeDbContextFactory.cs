using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ta3lom.Store.Infrastructure.Persistence;

/// Used by `dotnet ef` only (migrations add/script, database update); the web host is never started for design work.
/// `ConnectionStrings__Store` from the environment wins, so `dotnet ef database update` migrates the database the API
/// uses; without it the design database is targeted (enough for `migrations add` and `migrations script`).
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    public const string FallbackConnectionString = "Host=localhost;Database=ta3lom_store_design;Search Path=store,public";

    public StoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Store");
        var options = new DbContextOptionsBuilder<StoreDbContext>();
        StoreDbContextOptions.Configure(options, string.IsNullOrWhiteSpace(connectionString) ? FallbackConnectionString : connectionString);
        return new StoreDbContext(options.Options);
    }
}
