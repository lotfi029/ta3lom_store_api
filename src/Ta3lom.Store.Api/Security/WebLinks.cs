using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.Security;

/// Storefront links from `Store:WebBaseUrl` (https://ta3lom.sa in Production).
internal sealed class WebLinks(IOptions<StoreOptions> options) : IWebLinks
{
    private string Base => (options.Value.WebBaseUrl ?? "https://ta3lom.sa").TrimEnd('/');

    public string OrderThanks(string locale, string reference, string statusToken) =>
        $"{Base}/{locale}/store/orders/{Uri.EscapeDataString(reference)}/thanks?t={Uri.EscapeDataString(statusToken)}";

    public string Card(string locale, string cardToken) => $"{Base}/{locale}/store/card/{Uri.EscapeDataString(cardToken)}";

    public string ApiDownload(string token) => $"{options.Value.PublicBaseUrl.TrimEnd('/')}/v1/downloads/{Uri.EscapeDataString(token)}";

    public string Learn(string locale, string courseSlug, string token) =>
        $"{Base}/{locale}/learn/{Uri.EscapeDataString(courseSlug)}?t={Uri.EscapeDataString(token)}";

    public string MyCourses(string locale) => $"{Base}/{locale}/store/account/courses";

    public string Product(string locale, string slug) => $"{Base}/{locale}/store/products/{Uri.EscapeDataString(slug)}";

    public string Certificate(string locale, string certificateNo) => $"{Base}/{locale}/store/certificates/{Uri.EscapeDataString(certificateNo)}";
}
