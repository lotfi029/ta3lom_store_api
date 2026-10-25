namespace Ta3lom.Store.Application.DTOs.CustomerAuth;

/// `POST /v1/auth/otp/request` body.
public sealed record RequestOtpRequest(string Mobile, string? Locale);

/// `202` body: the same whether or not the mobile has an account.
public sealed record OtpRequestedResponse(int ExpiresInSeconds);

/// `POST /v1/auth/otp/verify` body.
public sealed record VerifyOtpRequest(string Mobile, string Code);

/// `200` body of verify; the refresh token travels only in the `t3s_rt` cookie.
public sealed record SessionResponse(string AccessToken, int ExpiresIn, CustomerSessionResponse Customer, IReadOnlyList<CartNoticeResponse> CartNotices);

/// The signed-in donor's own profile: the mobile is not masked here.
public sealed record CustomerSessionResponse(Guid Id, string Mobile, string? FullName, string? Email, string Locale, bool IsNew, bool ProfileComplete);

/// What the sign-in merge did to a guest line: `cart.merge_combined` or `cart.merge_dropped`.
public sealed record CartNoticeResponse(string Code, Guid? LineId);

/// `POST /v1/auth/refresh` body.
public sealed record AccessTokenResponse(string AccessToken, int ExpiresIn);
