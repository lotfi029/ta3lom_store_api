using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Payments;

/// Every CONFIGURED adapter by name; `Default` = `Store:Payments:Provider`. The fake exists only in Development and the
/// test host (C-2); Moyasar only when it is the provider or its secret key is set (showcase mode has neither): elsewhere
/// `/v1/payments/webhooks/{name}` is an unknown provider (404). With `Provider = none` there is no default — the payment
/// routes answer 409 before they could ask for one.
internal sealed class PaymentGatewayRegistry(IEnumerable<IPaymentGateway> gateways, IOptions<PaymentOptions> options, IHostEnvironment environment)
    : IPaymentGatewayRegistry
{
    private readonly Dictionary<string, IPaymentGateway> _byName = gateways
        .Where(g => g.Name switch
        {
            Fake.FakePaymentGateway.ProviderName => environment.IsDevelopment() || environment.IsEnvironment(PaymentOptionsValidator.TestingEnvironment),
            Moyasar.MoyasarGateway.ProviderName => string.Equals(options.Value.Provider?.Trim(), Moyasar.MoyasarGateway.ProviderName, StringComparison.OrdinalIgnoreCase)
                                                   || !string.IsNullOrWhiteSpace(options.Value.Moyasar.SecretKey),
            _ => true,
        })
        .ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

    public IPaymentGateway Default => !options.Value.PaymentsEnabled
        ? throw new InvalidOperationException("Store: payments disabled (showcase) — Store:Payments:Provider = none has no default gateway.")
        : Find(options.Value.Provider) ?? throw new InvalidOperationException($"Payment provider '{options.Value.Provider}' is not registered.");

    public IPaymentGateway? Find(string name) => _byName.GetValueOrDefault(name ?? string.Empty);
}
