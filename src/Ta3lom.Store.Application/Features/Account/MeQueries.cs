using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.DTOs.Orders;
using Ta3lom.Store.Application.Features.Orders;

namespace Ta3lom.Store.Application.Features.Account;

/// `/v1/me` reads (11 §5; FR-S22 … FR-S26). Every query is scoped to the signed-in customer; another customer's id → 404.
public sealed record GetProfileQuery : IQuery<ProfileResponse>;

public sealed record ListAddressesQuery : IQuery<IReadOnlyList<AddressResponse>>;

public sealed record ListEnrolmentsQuery : IQuery<IReadOnlyList<EnrolmentItem>>;

/// `GET /v1/me/enrolments/{id}/certificate` (#30): the certificate URL of a completed enrolment (the endpoint redirects).
public sealed record CertificateQuery(Guid EnrolmentId, string Locale) : IQuery<string>;

public sealed record BooksQuery : IQuery<BooksResponse>;

public sealed record ListMyOrdersQuery(int Page, int PageSize) : IQuery<PagedResult<OrderSummary>>;

public sealed record GetMyOrderQuery(string Reference) : IQuery<OrderDetailResponse>;

/// `GET /v1/me/documents/{number}` (#37, RUL-59): the private PDF of one of the customer's documents.
public sealed record GetMyDocumentQuery(string Number) : IQuery<DocumentFile>;

internal sealed class MeQueryHandlers(IApplicationDbContext db, ICurrentActor actor, IFileUrls files, IFileStorage storage, IWebLinks links,
    OrderReader orders, IClock clock) :
    IQueryHandler<GetProfileQuery, ProfileResponse>,
    IQueryHandler<ListAddressesQuery, IReadOnlyList<AddressResponse>>,
    IQueryHandler<ListEnrolmentsQuery, IReadOnlyList<EnrolmentItem>>,
    IQueryHandler<CertificateQuery, string>,
    IQueryHandler<BooksQuery, BooksResponse>,
    IQueryHandler<ListMyOrdersQuery, PagedResult<OrderSummary>>,
    IQueryHandler<GetMyOrderQuery, OrderDetailResponse>,
    IQueryHandler<GetMyDocumentQuery, DocumentFile>
{
    private Guid Me => actor.Id ?? throw new InvalidOperationException("customer route without a customer");

    public async Task<Result<ProfileResponse>> HandleAsync(GetProfileQuery query, CancellationToken ct = default)
    {
        var c = await db.Customers.AsNoTracking().FirstAsync(x => x.Id == Me, ct);
        return new ProfileResponse(c.Id, c.Mobile, c.FullName, c.Email, c.Locale, c.AnonymousDefault, c.MarketingOptIn, c.PdplConsentAt, c.CreatedAt);
    }

    public async Task<Result<IReadOnlyList<AddressResponse>>> HandleAsync(ListAddressesQuery query, CancellationToken ct = default)
    {
        var rows = await db.CustomerAddresses.AsNoTracking().Where(a => a.CustomerId == Me)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt).ToListAsync(ct);
        return rows.Select(MeMapping.Address).ToList();
    }

    public async Task<Result<IReadOnlyList<EnrolmentItem>>> HandleAsync(ListEnrolmentsQuery query, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var rows = await (from e in db.Enrolments.AsNoTracking()
                          join p in db.Products.AsNoTracking() on e.ProductId equals p.Id
                          join m in db.MediaAssets.AsNoTracking() on p.CoverAssetId equals m.Id into covers
                          from m in covers.DefaultIfEmpty()
                          where e.CustomerId == Me && e.Status != EnrolmentStatus.Revoked
                          orderby e.GrantedAt descending
                          select new { e, p.Slug, p.TitleAr, p.TitleEn, Cover = m == null ? null : m.StorageKey }).ToListAsync(ct);
        return rows.Select(r => new EnrolmentItem(r.e.Id, new EnrolmentProduct(r.Slug, r.TitleAr, r.TitleEn, r.Cover is null ? null : files.Public(r.Cover)),
            r.e.Source.ToWire(), r.e.Status.ToWire(), r.e.ProgressPct, r.e.GrantedAt, r.e.ExpiresAt, r.e.StartedAt, r.e.CompletedAt, r.e.CertificateNo,
            r.e.CanLaunch(now))).ToList();
    }

    public async Task<Result<string>> HandleAsync(CertificateQuery query, CancellationToken ct = default)
    {
        var e = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.EnrolmentId && x.CustomerId == Me, ct);
        if (e is null)
        {
            return GeneralErrors.NotFound;
        }

        if (e.Status != EnrolmentStatus.Completed)
        {
            return EnrolmentErrors.NotActive;
        }

        return e.CertificateNo is null ? GeneralErrors.NotFound : links.Certificate(query.Locale, e.CertificateNo);
    }

    public async Task<Result<BooksResponse>> HandleAsync(BooksQuery query, CancellationToken ct = default)
    {
        var downloads = await (from g in db.DownloadGrants.AsNoTracking()
                               join l in db.OrderLines.AsNoTracking() on g.OrderLineId equals l.Id
                               join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                               join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
                               where g.CustomerId == Me
                               orderby g.CreatedAt descending
                               select new BookDownloadItem(g.Id, p.Slug, p.TitleAr, p.TitleEn, o.Reference, g.DownloadCount, g.MaxDownloads, g.RevokedAt != null))
            .ToListAsync(ct);
        var shipments = await (from s in db.Shipments.AsNoTracking()
                               join o in db.Orders.AsNoTracking() on s.OrderId equals o.Id
                               where o.CustomerId == Me
                               orderby o.PlacedAt descending
                               select new { s, o.Reference }).ToListAsync(ct);
        var orderIds = shipments.Select(x => x.s.OrderId).ToList();
        var lines = await (from l in db.OrderLines.AsNoTracking()
                           join f in db.BookFormats.AsNoTracking() on l.BookFormatId equals f.Id
                           where orderIds.Contains(l.OrderId) && f.Kind == BookFormatKind.Print
                           select new { l.OrderId, l.TitleAr, l.TitleEn, l.Quantity }).ToListAsync(ct);
        return new BooksResponse(downloads, [.. shipments.Select(x => new BookShipmentItem(x.Reference, x.s.Status.ToWire(), x.s.Carrier, x.s.TrackingNo,
            x.s.ShippedAt, x.s.DeliveredAt, [.. lines.Where(l => l.OrderId == x.s.OrderId).Select(l => new BookShipmentLine(l.TitleAr, l.TitleEn, l.Quantity))]))]);
    }

    public async Task<Result<PagedResult<OrderSummary>>> HandleAsync(ListMyOrdersQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.Orders.AsNoTracking().Where(o => o.CustomerId == Me);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.PlacedAt).Skip(page.Skip).Take(page.Take)
            .Select(o => new
            {
                o.Reference,
                o.PlacedAt,
                o.Status,
                o.GrandTotal,
                Lines = db.OrderLines.Count(l => l.OrderId == o.Id),
                Invoice = db.Invoices.Any(i => i.OrderId == o.Id),
                Receipt = db.DonationReceipts.Any(r => r.OrderId == o.Id),
            }).ToListAsync(ct);
        return new PagedResult<OrderSummary>([.. rows.Select(r => new OrderSummary(r.Reference, r.PlacedAt, r.Status.ToWire(), r.GrandTotal.ToWire(), r.Lines,
            r.Invoice, r.Receipt))], total, page.SafePage, page.Take);
    }

    public async Task<Result<OrderDetailResponse>> HandleAsync(GetMyOrderQuery query, CancellationToken ct = default)
    {
        var order = await orders.FindAsync(query.Reference, ct);
        return order is null || order.CustomerId != Me ? CheckoutErrors.OrderNotFound : await orders.DetailAsync(order, ct);
    }

    public async Task<Result<DocumentFile>> HandleAsync(GetMyDocumentQuery query, CancellationToken ct = default)
    {
        var number = query.Number.Trim().ToUpperInvariant();
        var doc = await (from i in db.Invoices.AsNoTracking()
                         join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id
                         where i.Number == number && o.CustomerId == Me
                         select new { i.Number, i.PdfAssetId }).FirstOrDefaultAsync(ct)
                  ?? await (from r in db.DonationReceipts.AsNoTracking()
                            join o in db.Orders.AsNoTracking() on r.OrderId equals o.Id
                            where r.Number == number && o.CustomerId == Me
                            select new { r.Number, r.PdfAssetId }).FirstOrDefaultAsync(ct);
        if (doc is null)
        {
            return DocumentErrors.NotFound;
        }

        var key = doc.PdfAssetId is { } assetId
            ? await db.MediaAssets.AsNoTracking().Where(m => m.Id == assetId).Select(m => m.StorageKey).FirstOrDefaultAsync(ct)
            : null;
        var stream = key is null ? null : await storage.OpenReadAsync(key, isPublic: false, ct);
        return stream is null ? DocumentErrors.NotReady : new DocumentFile(stream, doc.Number + ".pdf");
    }
}

internal static class MeMapping
{
    public static AddressResponse Address(Domain.Entities.Identity.CustomerAddress a) =>
        new(a.Id, a.RecipientName, SaudiMobile.ToLocal(a.RecipientMobile), a.City, a.District, a.ShortAddress, a.Street, a.BuildingNo, a.PostalCode, a.IsDefault);
}
