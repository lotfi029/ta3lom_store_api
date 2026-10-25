using Mapster;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Mapping;

/// Explicit staff maps (every member named, so a new DTO field fails the start-up compile check).
internal sealed class StaffMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<StaffUser, StaffMeResponse>()
            .Map(d => d.Id, s => s.Id)
            .Map(d => d.Email, s => s.Email)
            .Map(d => d.Name, s => s.Name)
            .Map(d => d.Roles, s => s.Roles)
            .Map(d => d.MustChangePassword, s => s.MustChangePassword)
            .Map(d => d.LastLoginAt, s => s.LastLoginAt);

        config.NewConfig<StaffUser, StaffUserResponse>()
            .Map(d => d.Id, s => s.Id)
            .Map(d => d.Email, s => s.Email)
            .Map(d => d.Name, s => s.Name)
            .Map(d => d.Roles, s => s.Roles)
            .Map(d => d.IsActive, s => s.IsActive)
            .Map(d => d.TotpEnrolled, s => s.TotpSecretEnc != null)
            .Map(d => d.MustChangePassword, s => s.MustChangePassword)
            .Map(d => d.LockedUntil, s => s.LockedUntil)
            .Map(d => d.LastLoginAt, s => s.LastLoginAt)
            .Map(d => d.CreatedAt, s => s.CreatedAt)
            .Map(d => d.Version, s => s.Version);
    }
}
