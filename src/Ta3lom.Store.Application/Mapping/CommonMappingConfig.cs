using Mapster;

namespace Ta3lom.Store.Application.Mapping;

/// Wire formats shared by every response: money is a two-decimal string ("350.00"),
/// never a number. Enums are mapped per DTO with <c>EnumWire.ToWire()</c> so the JSON value equals the DDL value.
internal sealed class CommonMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Money, string>().MapWith(m => m.ToWire());
        config.NewConfig<Money?, string?>().MapWith(m => m.HasValue ? m.Value.ToWire() : null);
    }
}
