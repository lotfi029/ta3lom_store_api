namespace Ta3lom.Store.Domain.Entities.Settings;

/// Table `setting` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
