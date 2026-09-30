namespace Ta3lom.Store.Domain.ValueObjects;

/// `cart_line.dedication` as an owned JSON type (RUL-21): mobile stored as E.164.
public sealed class CartDedication
{
    public CartDedication(string toName, string toMobile, string? fromName, string? templateKey, string? text)
    {
        ToName = toName;
        ToMobile = toMobile;
        FromName = fromName;
        TemplateKey = templateKey;
        Text = text;
    }

    private CartDedication()
    {
    }

    public string ToName { get; private set; } = null!;

    public string ToMobile { get; private set; } = null!;

    public string? FromName { get; private set; }

    public string? TemplateKey { get; private set; }

    public string? Text { get; private set; }

    public CartDedication Copy() => new(ToName, ToMobile, FromName, TemplateKey, Text);
}
