namespace Ta3lom.Store.Application.DTOs.Staff;

/// `POST /v1/admin/auth/login` (11 §9.1).
public sealed record StaffLoginRequest(string Email, string Password);

public sealed record StaffLoginResponse(string MfaToken, bool TotpEnrolled);

public sealed record TotpEnrolResponse(string OtpauthUri, string SecretBase32);

public sealed record TotpVerifyRequest(string Code);

public sealed record StaffSessionResponse(string AccessToken, int ExpiresIn, StaffMeResponse Staff);

/// `GET /v1/admin/me`.
public sealed record StaffMeResponse(Guid Id, string Email, string Name, IReadOnlyList<string> Roles, DateTimeOffset? LastLoginAt);

/// `StaffUser` of 11 §9.19.
public sealed record StaffUserResponse(Guid Id, string Email, string Name, IReadOnlyList<string> Roles, bool IsActive,
    bool TotpEnrolled, DateTimeOffset? LockedUntil, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt);

public sealed record CreateStaffRequest(string Email, string Name, IReadOnlyList<string> Roles);

public sealed record CreateStaffResponse(StaffUserResponse Staff, string OneTimePassword);

public sealed record UpdateStaffRequest(string Name, IReadOnlyList<string> Roles, bool IsActive);

public sealed record OneTimePasswordResponse(string OneTimePassword);
