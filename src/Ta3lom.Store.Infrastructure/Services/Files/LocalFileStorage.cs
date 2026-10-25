using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Files;

/// Files under `Store:Files:Root`: `public/{key}` is served at `/files/public`, `private/{key}` never is.
/// Keys are relative; any attempt to leave the root is refused.
internal sealed class LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment) : IFileStorage
{
    public async Task SaveAsync(string storageKey, bool isPublic, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathFor(storageKey, isPublic);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(file, ct);
        }

        File.Move(temp, path, overwrite: true);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, bool isPublic, CancellationToken ct = default)
    {
        var path = PathFor(storageKey, isPublic);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null);
    }

    public Task DeleteAsync(string storageKey, bool isPublic, CancellationToken ct = default)
    {
        var path = PathFor(storageKey, isPublic);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// Root folder of public files (served by the Api at `/files/public`).
    public string PublicRoot => Path.Combine(Root, "public");

    private string Root => options.Value.ResolveRoot(environment.ContentRootPath);

    private string PathFor(string storageKey, bool isPublic)
    {
        var baseDir = Path.GetFullPath(Path.Combine(Root, isPublic ? "public" : "private"));
        var full = Path.GetFullPath(Path.Combine(baseDir, storageKey));
        return full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : throw new InvalidOperationException("Storage key escapes the storage root.");
    }
}
