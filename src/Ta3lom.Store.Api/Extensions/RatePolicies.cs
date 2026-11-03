namespace Ta3lom.Store.Api.Extensions;

/// Rate-limit policy names. Each session adds the policies of its routes.
public static class RatePolicies
{
    public const string Public = "public";
    public const string OtpRequest = "otp-request";
    public const string OtpVerify = "otp-verify";
    public const string AuthRefresh = "auth-refresh";
    public const string StaffLogin = "staff-login";
    public const string Cart = "cart";
    public const string Checkout = "checkout";
    public const string Me = "me";
    public const string Download = "download";
    public const string Admin = "admin";

    /// `POST /v1/orders/{reference}/pay` (per subject or IP).
    public const string Pay = "pay";

    /// `POST /v1/payments/webhooks/{provider}` (per IP: the gateway's servers).
    public const string Webhook = "webhook";

    /// D-b: template test sends, per staff member.
    public const string TemplateTest = "template-test";
}
