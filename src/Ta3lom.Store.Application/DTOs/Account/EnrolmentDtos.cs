namespace Ta3lom.Store.Application.DTOs.Account;

/// `POST /v1/me/enrolments/{id}/launch` (11 §5.4): a short-lived start/resume link.
public sealed record LaunchResponse(string Url, DateTimeOffset ExpiresAt);
