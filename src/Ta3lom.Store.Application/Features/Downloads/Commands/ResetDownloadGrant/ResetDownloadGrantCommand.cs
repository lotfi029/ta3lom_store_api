using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Application.Features.Downloads.Commands.ResetDownloadGrant;

/// `POST /v1/admin/download-grants/{id}/reset` (#94, RUL-41): staff give the buyer the full limit again (the change is
/// audited through the staff actor); a revoked grant cannot be reset.
public sealed record ResetDownloadGrantCommand(Guid GrantId, string Reason) : ICommand<DownloadGrantResponse>;

internal sealed class ResetDownloadGrantCommandHandler(IGenericRepository<DownloadGrant> grants, IApplicationDbContext db, IUnitOfWork uow)
    : ICommandHandler<ResetDownloadGrantCommand, DownloadGrantResponse>
{
    public async Task<Result<DownloadGrantResponse>> HandleAsync(ResetDownloadGrantCommand command, CancellationToken ct = default)
    {
        var grant = await grants.GetByIdAsync(command.GrantId, ct);
        if (grant is null)
        {
            return GeneralErrors.NotFound;
        }

        if (!grant.Reset())
        {
            return DownloadErrors.Revoked;
        }

        await uow.SaveChangesAsync(ct);
        var info = await (from l in db.OrderLines.AsNoTracking()
                          join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                          join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
                          where l.Id == grant.OrderLineId
                          select new { o.Reference, p.Slug }).FirstAsync(ct);
        return new DownloadGrantResponse(grant.Id, info.Reference, info.Slug, grant.DownloadCount, grant.MaxDownloads, grant.IsRevoked, grant.CreatedAt);
    }
}

internal sealed class ResetDownloadGrantCommandValidator : AbstractValidator<ResetDownloadGrantCommand>
{
    public ResetDownloadGrantCommandValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
}
