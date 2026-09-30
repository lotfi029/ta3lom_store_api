using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.SiteRebuild;

/// `Store:SiteRebuild` (S-35): the ta3lom-web repository and a fine-grained token allowed to send `repository_dispatch`.
/// Empty = no rebuild is triggered (logged).
public sealed class SiteRebuildOptions
{
    public const string Section = "Store:SiteRebuild";

    /// `owner/repo`.
    public string Repository { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string EventType { get; set; } = "store-catalog-changed";
}

/// GitHub `POST /repos/{owner}/{repo}/dispatches` (plan 14 S-35). Failures throw so the outbox retries with backoff.
internal sealed partial class GitHubSiteRebuildTrigger(HttpClient http, IOptions<SiteRebuildOptions> options, ILogger<GitHubSiteRebuildTrigger> logger)
    : ISiteRebuildTrigger
{
    public async Task<bool> TriggerAsync(string reason, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Repository) || string.IsNullOrWhiteSpace(o.Token))
        {
            LogSkipped(logger, reason);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://api.github.com/repos/{o.Repository}/dispatches"))
        {
            Content = JsonContent.Create(new { event_type = o.EventType, client_payload = new { reason } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.Token);
        request.Headers.UserAgent.ParseAdd("ta3lom-store-api");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Site rebuild not configured (Store:SiteRebuild); skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);
}
