using System.Text.Json;
using Ta3lom.Store.Application.DTOs.Settings;
using Ta3lom.Store.Domain.Entities.Settings;

namespace Ta3lom.Store.Application.Features.Settings.Commands.UpdateSetting;

/// `PUT /v1/admin/settings/{key}` : typed per key; the 60-second settings cache is invalidated and
/// the change is audited (staff actor).
public sealed record UpdateSettingCommand(string Key, JsonElement Value) : ICommand<SettingResponse>;

internal sealed class UpdateSettingCommandHandler(
    IGenericRepository<Setting> settings,
    IStoreSettings cache,
    ICurrentActor actor,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpdateSettingCommand, SettingResponse>
{
    public async Task<Result<SettingResponse>> HandleAsync(UpdateSettingCommand command, CancellationToken ct = default)
    {
        if (!SettingRules.IsKnown(command.Key))
        {
            return SettingErrors.UnknownKey;
        }

        if (!SettingRules.IsValid(command.Key, command.Value))
        {
            return SettingErrors.InvalidValue;
        }

        var now = clock.UtcNow;
        var json = command.Value.GetRawText();
        var setting = await settings.FirstOrDefaultAsync(s => s.Key == command.Key, ct);
        if (setting is null)
        {
            setting = new Setting(command.Key, json, now);
            settings.Add(setting);
        }

        setting.SetValue(json, actor.Name, now);
        await uow.SaveChangesAsync(ct);
        cache.Invalidate();
        return new SettingResponse(setting.Key, command.Value.Clone(), setting.UpdatedAt, setting.UpdatedBy);
    }
}
