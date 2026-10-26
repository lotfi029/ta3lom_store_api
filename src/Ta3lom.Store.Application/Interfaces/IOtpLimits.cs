namespace Ta3lom.Store.Application.Interfaces;

/// The per-mobile and per-IP OTP caps of D-10. Always the `SessionPolicy` values, except in Development where
/// `Store:Otp:Development` may raise them for local end-to-end runs (API-8; refused at start-up elsewhere).
public interface IOtpLimits
{
    int MaxPerMobile { get; }

    int MaxPerIp { get; }
}
