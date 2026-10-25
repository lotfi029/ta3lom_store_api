namespace Ta3lom.Store.Domain.Entities.Settings;

/// Table `setting`: one JSON value per key, validated by the typed allow-list before it is written.
public sealed class Setting
{
    public Setting(string key, string value, DateTimeOffset now)
    {
        Key = Guard.NotNull(key);
        Value = Guard.NotNull(value);
        UpdatedAt = now;
    }

    private Setting()
    {
    }

    public string Key { get; private set; } = null!;

    public string Value { get; private set; } = null!;

    public DateTimeOffset UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    /// `value` is the JSON text of the setting (jsonb); the handler validates it per key (RUL §M).
    public void SetValue(string json, string? updatedBy, DateTimeOffset now)
    {
        Value = Guard.NotEmpty(json);
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
