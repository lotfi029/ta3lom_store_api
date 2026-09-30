using FluentValidation;
using MapsterMapper;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.Staff.Commands.UpdateStaff;

/// `PUT /v1/admin/staff/{id}` (#125): name, roles, active flag. Deactivation revokes every session.
public sealed record UpdateStaffCommand(Guid Id, string Name, IReadOnlyList<string> Roles, bool IsActive) : ICommand<StaffUserResponse>;

internal sealed class UpdateStaffCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    IMapper mapper,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpdateStaffCommand, StaffUserResponse>
{
    public async Task<Result<StaffUserResponse>> HandleAsync(UpdateStaffCommand command, CancellationToken ct = default)
    {
        var user = await staff.GetByIdAsync(command.Id, ct);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        user.Update(command.Name, command.Roles, command.IsActive);
        await uow.SaveChangesAsync(ct);
        if (!command.IsActive)
        {
            await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Staff, user.Id, clock.UtcNow, ct);
        }

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
