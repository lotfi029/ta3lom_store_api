using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.SiteRebuild;

/// `Store:SiteRebuild`: the web repository and a fine-grained token allowed to send `repository_dispatch`.
/// Empty token = no rebuild is triggered (logged); Production refuses to start without one.
public sealed class SiteRebuildOptions
{
    public const string Section = "Store:SiteRebuild";

    /// `owner/repo`.
    public string Repository { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string EventType { get; set; } = "store-catalog-changed";
}

internal sealed class SiteRebuildOptionsValidator(IHostEnvironment environment) : IValidateOptions<SiteRebuildOptions>
{
    public ValidateOptionsResult Validate(string? name, SiteRebuildOptions options) =>
        environment.IsProduction() && (string.IsNullOrWhiteSpace(options.Token) || string.IsNullOrWhiteSpace(options.Repository))
            ? ValidateOptionsResult.Fail("Store:SiteRebuild:Token and Store:SiteRebuild:Repository are required in Production")
            : ValidateOptionsResult.Success;
}

/// GitHub `POST /repos/{owner}/{repo}/dispatches`. Failures throw so the outbox retries with backoff.
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

internal sealed class CatalogChangeDebounce(StoreDbContext db) : ICatalogChangeDebounce
{
    private sealed record Row(Guid Id);

    public async Task<bool> AbsorbAsync(Guid handledId, DateTimeOffset now, CancellationToken ct = default)
    {
        // Rows of one SaveChanges share occurred_at; ids are UUIDv7, so (occurred_at, id) orders the changes. Only OLDER
        // pending rows that no dispatcher has claimed are absorbed: the rebuild this message triggers covers them. A NEWER
        // pending row is never absorbed; while one is pending it rebuilds later, so this one needs no rebuild of its own.
        // Net rule: the newest pending change always triggers a rebuild.
        await db.Database.ExecuteSqlAsync($"""
            UPDATE store.outbox_message o
               SET processed_at = {now}, last_error = NULL
              FROM store.outbox_message me
             WHERE me.id = {handledId} AND o.type = {nameof(CatalogChanged)} AND o.id <> me.id
               AND o.processed_at IS NULL AND NOT o.dead_lettered
               AND (o.claimed_until IS NULL OR o.claimed_until < {now})
               AND (o.occurred_at, o.id) < (me.occurred_at, me.id)
            """, ct);
        var newer = await db.Database.SqlQuery<Row>($"""
            SELECT o.id
              FROM store.outbox_message o, store.outbox_message me
             WHERE me.id = {handledId} AND o.type = {nameof(CatalogChanged)} AND o.id <> me.id
               AND o.processed_at IS NULL AND NOT o.dead_lettered AND (o.occurred_at, o.id) > (me.occurred_at, me.id)
             LIMIT 1
            """).ToListAsync(ct);
        return newer.Count == 0;
    }
}
