namespace Ta3lom.Store.Domain.Errors;

/// Digital downloads (11 §1.6, RUL-41, RUL-58).
public static class DownloadErrors
{
    public static Error LimitReached => Error.Forbidden(ErrorCodes.DownloadLimitReached, "The download limit is reached.");

    public static Error Revoked => Error.Gone(ErrorCodes.DownloadRevoked, "The grant was revoked.");

    public static Error LinkExpired => Error.Gone(ErrorCodes.DownloadLinkExpired, "The link is older than 10 minutes or not this customer's.");
}
