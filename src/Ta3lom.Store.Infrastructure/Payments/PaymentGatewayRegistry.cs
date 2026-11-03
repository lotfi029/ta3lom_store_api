using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Payments;

/// Every registered adapter by name; `Default` = `Store:Payments:Provider`. The fake exists only in Development and the
/// test host (C-2): elsewhere `/v1/payments/webhooks/fake` is an unknown provider (404).
internal sealed class PaymentGatewayRegistry(IEnumerable<IPaymentGateway> gateways, IOptions<PaymentOptions> options, IHostEnvironment environment)
    : IPaymentGatewayRegistry
{
    private readonly Dictionary<string, IPaymentGateway> _byName = gateways
        .Where(g => g.Name != Fake.FakePaymentGateway.ProviderName
                    || environment.IsDevelopment() || environment.IsEnvironment(PaymentOptionsValidator.TestingEnvironment))
        .ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

    public IPaymentGateway Default => Find(options.Value.Provider)
        ?? throw new InvalidOperationException($"Payment provider '{options.Value.Provider}' is not registered.");

    public IPaymentGateway? Find(string name) => _byName.GetValueOrDefault(name ?? string.Empty);
}
