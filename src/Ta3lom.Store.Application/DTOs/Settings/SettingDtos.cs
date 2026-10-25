using System.Text.Json;

namespace Ta3lom.Store.Application.DTOs.Settings;

/// A setting: `value` is the JSON value, typed per key (money as a "10.00" string).
public sealed record SettingResponse(string Key, JsonElement Value, DateTimeOffset UpdatedAt, string? UpdatedBy);

/// `PUT /v1/admin/settings/{key}` body.
public sealed record SettingInput(JsonElement Value);
