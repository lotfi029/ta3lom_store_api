using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.GetCatalogConfig;

/// `GET /v1/catalog/config` (#9, FR-S12–S14): public settings for the buy box and checkout.
public sealed record GetCatalogConfigQuery : IQuery<CatalogConfigResponse>;

internal sealed class GetCatalogConfigQueryHandler(IStoreSettings settings) : IQueryHandler<GetCatalogConfigQuery, CatalogConfigResponse>
{
    public async Task<Result<CatalogConfigResponse>> HandleAsync(GetCatalogConfigQuery query, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        return new CatalogConfigResponse(s.VatEnabled, Wire.Money(s.VatRate), s.ShippingFlatFee.ToWire(), s.MinGiftAmount.ToWire(), s.CartMaxTotal.ToWire(),
            CatalogLimits.CartMaxLines, CatalogLimits.PrintMaxQuantity, CatalogLimits.GiftMaxQuantity, CatalogLimits.PaymentMethods,
            s.DedicationFreeTextEnabled, [.. s.DedicationTemplates.Select(t => new DedicationTemplateResponse(t.Key, t.Ar, t.En))],
            new SponsorPresets(CatalogLimits.CourseSponsorPresets, CatalogLimits.BookSponsorPresets), CatalogLimits.CauseUnitPresets, CatalogLimits.AmountPresets);
    }
}
