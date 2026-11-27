using System.Collections.Concurrent;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Ta3lom.Store.Api.Extensions;

/// Live "the public catalogue changed" signal for open store pages: `GET /v1/catalog/events` (Server-Sent Events).
///
/// Every successful staff catalogue write (the same routes that evict the output cache, <see cref="HttpCaching"/>) bumps a
/// per-process version and wakes every open stream, which sends `event: catalog` with the new version. The page then
/// re-reads its catalogue data (no-cache + ETag, so a 304 when its view was already current). No data travels on the
/// stream, so nothing about a donor or a project leaks through it.
///
/// A stream starts with `event: ready`, sends an empty `ping` every 25 s (keeps proxies from closing an idle
/// connection; an event with no data is never dispatched by EventSource) and ends after 15 minutes so connections cycle
/// across deploys; EventSource reconnects on its own. One API container serves the store, so an in-memory fan-out is
/// enough; several containers would need a shared bus (PostgreSQL NOTIFY) instead.
internal sealed class CatalogChanges
{
    public const string ReadyEvent = "ready";
    public const string ChangedEvent = "catalog";
    public const string PingEvent = "ping";

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Channel<long>, byte> _subscribers = new();
    private long _version;

    /// The number of open streams (diagnostics and tests).
    public int Subscribers => _subscribers.Count;

    /// Called after a successful catalogue write: wakes every open stream.
    public void Publish()
    {
        var version = Interlocked.Increment(ref _version);
        foreach (var subscriber in _subscribers.Keys)
        {
            subscriber.Writer.TryWrite(version);
        }
    }

    /// The event stream of one connection, until the client leaves or <see cref="MaxLifetime"/> passes.
    public async IAsyncEnumerable<SseItem<string>> StreamAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // Capacity 1, newest wins: a burst of edits while a client is slow becomes one event.
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _subscribers.TryAdd(channel, 0);
        try
        {
            yield return new SseItem<string>(Format(Interlocked.Read(ref _version)), ReadyEvent);

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lifetime.CancelAfter(MaxLifetime);
            while (true)
            {
                var next = await WaitAsync(channel, lifetime, ct);
                if (next == Wake.End)
                {
                    yield break;
                }

                if (next == Wake.Heartbeat)
                {
                    yield return new SseItem<string>(string.Empty, PingEvent);
                    continue;
                }

                var version = 0L;
                while (channel.Reader.TryRead(out var v))
                {
                    version = v;
                }

                yield return new SseItem<string>(Format(version), ChangedEvent);
            }
        }
        finally
        {
            _subscribers.TryRemove(channel, out _);
        }
    }

    private enum Wake
    {
        Changed,
        Heartbeat,
        End,
    }

    private static async Task<Wake> WaitAsync(Channel<long> channel, CancellationTokenSource lifetime, CancellationToken ct)
    {
        using var beat = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        beat.CancelAfter(Heartbeat);
        try
        {
            return await channel.Reader.WaitToReadAsync(beat.Token) ? Wake.Changed : Wake.End;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return lifetime.IsCancellationRequested ? Wake.End : Wake.Heartbeat;
        }
    }

    private static string Format(long version) => version.ToString(CultureInfo.InvariantCulture);
}
