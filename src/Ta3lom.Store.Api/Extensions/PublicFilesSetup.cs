using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Infrastructure.Services.Files;

namespace Ta3lom.Store.Api.Extensions;

public static class PublicFilesSetup
{
    /// Serves `Store:Files:Root/public` at `/files/public` (covers, update images). Private files are never served.
    public static IApplicationBuilder UsePublicFiles(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var root = Path.Combine(app.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.ResolveRoot(app.Environment.ContentRootPath), "public");
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // The volume is not mounted (tests, CI): nothing to serve; uploads fail loudly instead.
            return app;
        }

        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = "/files/public",
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=86400",
        });
    }
}
