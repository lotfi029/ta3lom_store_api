using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.Security;

/// Web site links from `Store:WebBaseUrl` (https://store.ta3lom.sa in Production).
internal sealed class WebLinks(IOptions<StoreOptions> options) : IWebLinks
{
    private string Base => (options.Value.WebBaseUrl ?? "https://store.ta3lom.sa").TrimEnd('/');

    public string OrderStatus(string locale, string reference, string statusToken) =>
        $"{Base}/{locale}/orders/{Uri.EscapeDataString(reference)}?t={Uri.EscapeDataString(statusToken)}";

    public string Project(string locale, string slug) => $"{Base}/{locale}/projects/{Uri.EscapeDataString(slug)}";
}
