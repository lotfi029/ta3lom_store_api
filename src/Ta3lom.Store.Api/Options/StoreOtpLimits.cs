using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Options;

/// API-8: reads `Store:Otp:Development` only in Development; every other environment gets the D-10 constants.
internal sealed class StoreOtpLimits(IOptions<StoreOptions> options, IHostEnvironment environment) : IOtpLimits
{
    private OtpDevelopmentSettings? Development => environment.IsDevelopment() ? options.Value.Otp.Development : null;

    public int MaxPerMobile => Development?.MaxPerMobile ?? SessionPolicy.OtpMaxPerMobile;

    public int MaxPerIp => Development?.MaxPerIp ?? SessionPolicy.OtpMaxPerIp;
}
