using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Services.Security;

namespace Ta3lom.Store.Infrastructure.Services.Lms;

/// `Store:Lms` (Q-05). `SimulateFailure` makes every enrol/revoke fail — Development/Staging only, to show RUL-42's
/// `pending_sync` → recovery path.
public sealed class LmsOptions
{
    public const string Section = "Store:Lms";

    public string Adapter { get; set; } = "internal";

    public bool SimulateFailure { get; set; }

    public int LaunchMinutes { get; set; } = 10;
}

/// Default until an LMS is chosen (Q-05): access lives in the store itself. Enrol/revoke always succeed (reference
/// `internal:{id}`); the launch link opens the storefront player with a signed, 10-minute token
/// (`{enrolmentId}.{expiryUnix}.{hmac}`, HMAC with a key derived from `Store:Tokens:StatusKeyBase64`).
internal sealed class InternalAccessLmsGateway(IOptions<LmsOptions> options, IOptions<TokenKeyOptions> keys, IWebLinks links, IClock clock)
    : ILmsGateway, IDevLmsProgressBoard
{
    private readonly ConcurrentDictionary<Guid, LmsProgress> _reported = new();

    public Task<LmsResult> EnrolAsync(LmsEnrolment enrolment, CancellationToken ct = default) =>
        Task.FromResult(options.Value.SimulateFailure
            ? new LmsResult(false, null, "LMS unavailable (Store:Lms:SimulateFailure)")
            : new LmsResult(true, "internal:" + enrolment.EnrolmentId.ToString("N"), null));

    public Task<LmsResult> RevokeAsync(LmsEnrolment enrolment, CancellationToken ct = default) =>
        Task.FromResult(options.Value.SimulateFailure ? new LmsResult(false, null, "LMS unavailable") : new LmsResult(true, enrolment.LmsEnrolmentRef, null));

    public Task<LmsLaunch> LaunchAsync(LmsEnrolment enrolment, string locale, CancellationToken ct = default)
    {
        var expires = clock.UtcNow.AddMinutes(options.Value.LaunchMinutes);
        var payload = FormattableString.Invariant($"{enrolment.EnrolmentId:N}.{expires.ToUnixTimeSeconds()}");
        var key = HMACSHA256.HashData(Convert.FromBase64String(keys.Value.StatusKeyBase64), Encoding.ASCII.GetBytes("store.lms.launch.v1"));
        var mac = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(payload))[..16]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Task.FromResult(new LmsLaunch(links.Learn(locale, enrolment.CourseSlug, payload + "." + mac), expires));
    }

    /// The internal adapter has no LMS behind it: progress is whatever `/dev/lms/progress` reported (Development/Staging),
    /// otherwise nothing new (S-40).
    public Task<LmsProgress?> ProgressAsync(LmsEnrolment enrolment, CancellationToken ct = default) =>
        Task.FromResult(_reported.TryGetValue(enrolment.EnrolmentId, out var p) ? p : null);

    public void Report(Guid enrolmentId, LmsProgress progress) => _reported[enrolmentId] = progress;
}
