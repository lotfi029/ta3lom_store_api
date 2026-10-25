using FluentValidation;
using MapsterMapper;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.Staff.Commands.UpdateStaff;

/// `PUT /v1/admin/staff/{id}`: name, roles, active flag, with the `version` read before (409 on a stale one). The last active
/// admin cannot lose the role or be deactivated (D-12): the active admins are locked first, so two admins demoting each other
/// at the same time cannot both succeed. Deactivation revokes every session.
public sealed record UpdateStaffCommand(Guid Id, string Name, IReadOnlyList<string> Roles, bool IsActive, uint Version) : ICommand<StaffUserResponse>;

internal sealed class UpdateStaffCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    IMapper mapper,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpdateStaffCommand, StaffUserResponse>
{
    public Task<Result<StaffUserResponse>> HandleAsync(UpdateStaffCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => UpdateAsync(command, token), ct);

    private async Task<Result<StaffUserResponse>> UpdateAsync(UpdateStaffCommand command, CancellationToken ct)
    {
        var admins = await staff.LockActiveAdminIdsAsync(ct);
        var user = await staff.GetByIdAsync(command.Id, ct);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        if (user.Version != command.Version)
        {
            return GeneralErrors.ConcurrencyConflict;
        }

        var staysAdmin = command.IsActive && command.Roles.Contains(Roles.Admin, StringComparer.Ordinal);
        if (admins.Contains(user.Id) && !staysAdmin && admins.Count == 1)
        {
            return StaffErrors.LastAdmin;
        }

        user.Update(command.Name, command.Roles, command.IsActive);
        if (!command.IsActive)
        {
            await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Staff, user.Id, clock.UtcNow, ct);
        }

        await uow.SaveChangesAsync(ct);
        return mapper.Map<StaffUserResponse>(user);
    }
}

internal sealed class UpdateStaffCommandValidator : AbstractValidator<UpdateStaffCommand>
{
    public UpdateStaffCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Roles).NotEmpty();
        RuleForEach(c => c.Roles).Must(Roles.IsValid).WithMessage("Unknown role.");
    }
}
