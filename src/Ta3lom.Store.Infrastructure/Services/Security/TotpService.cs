using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// Staff TOTP (RUL-62, 09 §5.8): RFC 6238, 30-second step, verification window ±1 step. The 20-byte secret is
/// stored Data-Protection-encrypted (purpose `Store.Staff.Totp.v1`) in `staff_user.totp_secret_enc`.
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

    public bool Verify(byte[] protectedSecret, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var totp = new Totp(_protector.Unprotect(protectedSecret));
        return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
    }
}
