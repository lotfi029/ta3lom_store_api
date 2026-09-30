namespace Ta3lom.Store.Application.DTOs.Account;

/// `POST /v1/me/enrolments/{id}/launch` (11 §5.4): a short-lived start/resume link.
public sealed record LaunchResponse(string Url, DateTimeOffset ExpiresAt);

/// `POST /v1/me/downloads/{grantId}/link` (11 §5.7).
public sealed record DownloadLinkResponse(string Url, DateTimeOffset ExpiresAt, int Remaining);

/// A staff view of a grant (11 §9.11 `DownloadGrant`).
public sealed record DownloadGrantResponse(Guid Id, string OrderRef, string Slug, int DownloadCount, int MaxDownloads, bool Revoked, DateTimeOffset CreatedAt);

/// `POST /v1/admin/download-grants/{id}/reset` body.
public sealed record ResetDownloadGrantRequest(string Reason);
