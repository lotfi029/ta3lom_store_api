namespace Ta3lom.Store.Application.DTOs.Staff;

/// `POST /v1/admin/auth/login`.
public sealed record StaffLoginRequest(string Email, string Password);

/// Step 1 result: the 5-minute MFA token for the TOTP step.
public sealed record StaffLoginResponse(string MfaToken, bool TotpEnrolled);

public sealed record TotpEnrolResponse(string OtpauthUri, string SecretBase32);

public sealed record TotpVerifyRequest(string Code);

/// `mustChangePassword`: the access token is limited to the password change and logout until it is done.
public sealed record StaffSessionResponse(string AccessToken, int ExpiresIn, StaffMeResponse Staff);

/// `POST /v1/admin/auth/password`.
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// `GET /v1/admin/me`.
public sealed record StaffMeResponse(Guid Id, string Email, string Name, IReadOnlyList<string> Roles, bool MustChangePassword, DateTimeOffset? LastLoginAt);

/// A staff member in the admin list; `version` is sent back on update.
public sealed record StaffUserResponse(Guid Id, string Email, string Name, IReadOnlyList<string> Roles, bool IsActive, bool TotpEnrolled,
    bool MustChangePassword, DateTimeOffset? LockedUntil, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt, uint Version);

public sealed record CreateStaffRequest(string Email, string Name, IReadOnlyList<string> Roles);

public sealed record CreateStaffResponse(StaffUserResponse Staff, string OneTimePassword);

public sealed record UpdateStaffRequest(string Name, IReadOnlyList<string> Roles, bool IsActive, uint Version);

public sealed record OneTimePasswordResponse(string OneTimePassword);
