namespace Ta3lom.Store.Application.Interfaces;

/// Keyed hashes for values that must be matched but never stored or logged in clear (PDPL, 09 §5.13):
/// client IPs (`otp_challenge.ip_hash`, `audit_log.ip_hash`, rate-limit partitions) and mobiles.
public interface IPseudonymizer
{
    string HashIp(string ip);

    string HashMobile(string mobileE164);
}
