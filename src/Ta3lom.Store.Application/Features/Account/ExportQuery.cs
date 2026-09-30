using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.DTOs.Orders;
using Ta3lom.Store.Application.Features.Orders;

namespace Ta3lom.Store.Application.Features.Account;

/// `GET /v1/me/export` (#38): profile, addresses, orders with lines, giving, enrolments, download grants and the dedications
/// the customer sent — composed from the same reads the account pages use.
public sealed record ExportMeQuery : IQuery<MeExport>;

internal sealed class ExportMeQueryHandler(
    IApplicationDbContext db,
    ICurrentActor actor,
    OrderReader orders,
    IQueryHandler<GetProfileQuery, ProfileResponse> profile,
    IQueryHandler<ListAddressesQuery, IReadOnlyList<AddressResponse>> addresses,
    IQueryHandler<ListEnrolmentsQuery, IReadOnlyList<EnrolmentItem>> enrolments,
    IQueryHandler<BooksQuery, BooksResponse> books,
    IQueryHandler<MyGivingQuery, GivingResponse> giving,
    IClock clock) : IQueryHandler<ExportMeQuery, MeExport>
{
    public async Task<Result<MeExport>> HandleAsync(ExportMeQuery query, CancellationToken ct = default)
    {
        var me = actor.Id ?? throw new InvalidOperationException("customer route without a customer");
        var details = new List<OrderDetailResponse>();
        foreach (var reference in await db.Orders.AsNoTracking().Where(o => o.CustomerId == me).OrderBy(o => o.PlacedAt).Select(o => o.Reference).ToListAsync(ct))
        {
            details.Add(await orders.DetailAsync((await orders.FindAsync(reference, ct))!, ct));
        }

        var dedications = await (from d in db.Dedications.AsNoTracking()
                                 join l in db.OrderLines.AsNoTracking() on d.OrderLineId equals l.Id
                                 join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                                 where o.CustomerId == me
                                 select new { o.Reference, d }).ToListAsync(ct);
        var givingItems = (await giving.HandleAsync(new MyGivingQuery(1, PageRequest.MaxPageSize), ct)).Value.Items.ToList();
        for (var page = 2; givingItems.Count == (page - 1) * PageRequest.MaxPageSize; page++)
        {
            givingItems.AddRange((await giving.HandleAsync(new MyGivingQuery(page, PageRequest.MaxPageSize), ct)).Value.Items);
        }

        return new MeExport(clock.UtcNow,
            (await profile.HandleAsync(new GetProfileQuery(), ct)).Value,
            (await addresses.HandleAsync(new ListAddressesQuery(), ct)).Value,
            details, givingItems,
            (await enrolments.HandleAsync(new ListEnrolmentsQuery(), ct)).Value,
            (await books.HandleAsync(new BooksQuery(), ct)).Value.Downloads,
            [.. dedications.Select(x => new ExportDedication(x.Reference, x.d.ToName, x.d.ToMobile, x.d.FromName, x.d.TemplateKey, x.d.CustomText,
                x.d.SmsStatus.ToWire(), x.d.SentAt))]);
    }
}
