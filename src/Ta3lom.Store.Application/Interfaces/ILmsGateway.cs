namespace Ta3lom.Store.Application.Interfaces;

/// Course delivery (RUL-42, Q-05): `InternalAccessLmsGateway` until an LMS is chosen. Called only from the outbox and the
/// launch endpoint, never inside a transaction; every call is idempotent per enrolment id.
public interface ILmsGateway
{
    Task<LmsResult> EnrolAsync(LmsEnrolment enrolment, CancellationToken ct = default);

    Task<LmsResult> RevokeAsync(LmsEnrolment enrolment, CancellationToken ct = default);

    /// A short-lived start/resume link (FR-S23).
    Task<LmsLaunch> LaunchAsync(LmsEnrolment enrolment, string locale, CancellationToken ct = default);

    /// Progress and completion for S-40's `LmsProgressSync` (null when the LMS has nothing new).
    Task<LmsProgress?> ProgressAsync(LmsEnrolment enrolment, CancellationToken ct = default);
}

public sealed record LmsEnrolment(Guid EnrolmentId, Guid CustomerId, string CourseSlug, string? LmsCourseRef, string? LmsEnrolmentRef,
    DateTimeOffset? ExpiresAt);

public sealed record LmsResult(bool Ok, string? Reference, string? Error);

public sealed record LmsLaunch(string Url, DateTimeOffset ExpiresAt);

/// What the LMS reports for one enrolment (S-40): percentage, first start, completion and the certificate number (OP-3).
public sealed record LmsProgress(short ProgressPct, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? CertificateNo);

/// Development/Staging only: lets `/dev/lms/progress` play the LMS for the internal adapter, so `LmsProgressSync` has
/// something to pull (plan 14 §4.3 dev tooling).
public interface IDevLmsProgressBoard
{
    void Report(Guid enrolmentId, LmsProgress progress);
}
