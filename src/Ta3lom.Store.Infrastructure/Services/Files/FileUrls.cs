using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Files;

/// `Store:Files` — where files live and how public ones are addressed (S-09 adds LocalFileStorage).
public sealed class FileStorageOptions
{
    public const string Section = "Store:Files";

    /// Root folder with `public/` and `private/` sub-folders (a volume in production).
    public string Root { get; set; } = "/srv/store/files";

    /// Base URL of public files, e.g. https://store-api.ta3lom.sa/files/public.
    public string PublicBaseUrl { get; set; } = "https://store-api.ta3lom.sa/files/public";
}

internal sealed class FileUrls(IOptions<FileStorageOptions> options) : IFileUrls
{
    public string Public(string storageKey) => $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{storageKey.TrimStart('/')}";
}
