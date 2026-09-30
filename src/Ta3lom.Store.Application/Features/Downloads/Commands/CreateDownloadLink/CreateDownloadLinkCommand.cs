using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Application.Features.Downloads.Commands.CreateDownloadLink;

/// `POST /v1/me/downloads/{grantId}/link` (#32, RUL-41): a signed link valid 10 minutes for the grant's owner.
public sealed record CreateDownloadLinkCommand(Guid GrantId) : ICommand<DownloadLinkResponse>;

internal sealed class CreateDownloadLinkCommandHandler(ICurrentActor actor, IGenericRepository<DownloadGrant> grants, IDownloadLinks links,
    IWebLinks web, IClock clock) : ICommandHandler<CreateDownloadLinkCommand, DownloadLinkResponse>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<Result<DownloadLinkResponse>> HandleAsync(CreateDownloadLinkCommand command, CancellationToken ct = default)
    {
        var grant = await grants.FirstOrDefaultAsync(g => g.Id == command.GrantId, ct);
        if (grant is null || actor.Kind != ActorKind.Customer || grant.CustomerId != actor.Id)
        {
            return GeneralErrors.NotFound;
        }

        if (grant.IsRevoked)
        {
            return DownloadErrors.Revoked;
        }

        if (grant.Remaining == 0)
        {
            return DownloadErrors.LimitReached;
        }

        var expiresAt = clock.UtcNow + Lifetime;
        return new DownloadLinkResponse(web.ApiDownload(links.Create(grant.Id, grant.CustomerId, expiresAt)), expiresAt, grant.Remaining);
    }
}
