using System.Text.Json;
using Ta3lom.Store.Application.DTOs.Settings;
using Ta3lom.Store.Domain.Entities.Settings;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Settings.Commands.UpdateSetting;

/// `PUT /v1/admin/settings/{key}` : typed per key; the 60-second settings cache is invalidated and
/// the change is audited (staff actor).
public sealed record UpdateSettingCommand(string Key, JsonElement Value) : ICommand<SettingResponse>;

internal sealed class UpdateSettingCommandHandler(
    IGenericRepository<Setting> settings,
    IStoreSettings cache,
    IOutbox outbox,
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
        var value = SettingRules.Canonical(command.Key, command.Value);
        if (!SettingRules.IsConsistent(command.Key, value, await cache.GetAsync(ct)))
        {
            return SettingErrors.InvalidValue;                                // L-15: would contradict another setting
        }

        if (SettingRules.SiteVisibleKeys.Contains(command.Key, StringComparer.Ordinal))
        {
            outbox.Enqueue(new CatalogChanged("setting", Guid.Empty, command.Key));    // L-14: the static site shows it
        }

        var json = value.GetRawText();
        var setting = await settings.FirstOrDefaultAsync(s => s.Key == command.Key, ct);
        if (setting is null)
        {
            setting = new Setting(command.Key, json, now);
            settings.Add(setting);
        }

        setting.SetValue(json, actor.Name, now);
        await uow.SaveChangesAsync(ct);
        cache.Invalidate();
        return new SettingResponse(setting.Key, value, setting.UpdatedAt, setting.UpdatedBy);
    }
}
