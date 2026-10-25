namespace Ta3lom.Store.Application.Interfaces;

/// Keyed hashes for values that must be matched but never stored or logged in clear (PDPL): client IPs
/// (`otp_challenge.ip_hash`, `audit_log.ip_hash`, rate-limit partitions) and message recipients (`notification.recipient_hash`).
public interface IPseudonymizer
{
    string HashIp(string ip);

    string HashMobile(string mobileE164);

    /// Lower-cased before hashing, so an address matches whatever case it was typed in.
    string HashEmail(string email);
}
