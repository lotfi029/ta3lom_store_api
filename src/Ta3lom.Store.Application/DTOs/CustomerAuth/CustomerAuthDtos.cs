namespace Ta3lom.Store.Application.DTOs.CustomerAuth;

/// `POST /v1/auth/otp/request` body (11 §4.1).
public sealed record RequestOtpRequest(string Mobile, string? Locale);

/// `202` body: the same whether or not the mobile has an account.
public sealed record OtpRequestedResponse(int ExpiresInSeconds);

/// `POST /v1/auth/otp/verify` body (11 §4.2).
public sealed record VerifyOtpRequest(string Mobile, string Code);

/// `200` body of verify; the refresh token travels only in the `t3s_rt` cookie.
public sealed record SessionResponse(string AccessToken, int ExpiresIn, CustomerSessionResponse Customer, IReadOnlyList<CartNoticeResponse> CartNotices);

/// The signed-in customer's own profile: the mobile is not masked here (11 §1.3).
public sealed record CustomerSessionResponse(Guid Id, string Mobile, string? FullName, string? Email, string Locale,
    bool AnonymousDefault, bool IsNew, bool ProfileComplete);

public sealed record CartNoticeResponse(string Code, Guid? LineId);

/// `POST /v1/auth/refresh` body (11 §4.3).
public sealed record AccessTokenResponse(string AccessToken, int ExpiresIn);
