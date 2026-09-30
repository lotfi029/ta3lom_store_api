using Mapster;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Mapping;

internal sealed class CustomerMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Customer, CustomerSessionResponse>()
            .Map(d => d.Id, s => s.Id)
            .Map(d => d.Mobile, s => s.Mobile)
            .Map(d => d.FullName, s => s.FullName)
            .Map(d => d.Email, s => s.Email)
            .Map(d => d.Locale, s => s.Locale)
            .Map(d => d.AnonymousDefault, s => s.AnonymousDefault)
            .Map(d => d.IsNew, s => false)
            .Map(d => d.ProfileComplete, s => s.ProfileComplete);
    }
}
