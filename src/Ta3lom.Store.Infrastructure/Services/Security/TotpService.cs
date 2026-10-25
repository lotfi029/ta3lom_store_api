using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// Staff TOTP (D-12): RFC 6238, 30-second step, verification window ±1 step. The 20-byte secret is stored
/// Data-Protection-encrypted (purpose `Store.Staff.Totp.v1`) in `staff_user.totp_secret_enc`. The matched time step is
/// returned so the caller can refuse a step that was already used (`totp_last_step`).
internal sealed class TotpService(IDataProtectionProvider provider) : ITotpService
{
    public const string Issuer = "ta3lom store";

    private readonly IDataProtector _protector = provider.CreateProtector("Store.Staff.Totp.v1");

    public TotpEnrolment CreateEnrolment(string accountEmail)
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(secret);
        var uri = new OtpUri(OtpType.Totp, base32, accountEmail, Issuer).ToString();
        return new TotpEnrolment(uri, base32, _protector.Protect(secret));
    }

    public long? VerifyStep(byte[] protectedSecret, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var totp = new Totp(_protector.Unprotect(protectedSecret));
        return totp.VerifyTotp(code.Trim(), out var step, new VerificationWindow(previous: 1, future: 1)) ? step : null;
    }
}
