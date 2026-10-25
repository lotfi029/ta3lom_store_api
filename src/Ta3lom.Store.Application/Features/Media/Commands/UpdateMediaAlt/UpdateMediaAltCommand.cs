using Ta3lom.Store.Application.DTOs.Media;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.Media.Commands.UpdateMediaAlt;

/// `PATCH /v1/admin/media/{id}` (#77, RUL-03): images keep a non-empty Arabic alt text.
public sealed record UpdateMediaAltCommand(Guid Id, string? AltAr, string? AltEn) : ICommand<MediaAssetResponse>;

internal sealed class UpdateMediaAltCommandHandler(IGenericRepository<MediaAsset> media, IFileUrls urls, IUnitOfWork uow)
    : ICommandHandler<UpdateMediaAltCommand, MediaAssetResponse>
{
    public async Task<Result<MediaAssetResponse>> HandleAsync(UpdateMediaAltCommand command, CancellationToken ct = default)
    {
        var asset = await media.GetByIdAsync(command.Id, ct);
        if (asset is null)
        {
            return GeneralErrors.NotFound;
        }

        if (asset.Kind == MediaKind.Image && string.IsNullOrWhiteSpace(command.AltAr))
        {
            return MediaErrors.AltRequired;
        }

        asset.SetAlt(command.AltAr, command.AltEn);
        await uow.SaveChangesAsync(ct);
        return MediaMapping.ToResponse(asset, urls);
    }
}
