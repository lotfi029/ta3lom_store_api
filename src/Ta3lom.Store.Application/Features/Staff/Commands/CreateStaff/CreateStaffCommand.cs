using FluentValidation;
using MapsterMapper;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.Staff.Commands.CreateStaff;

/// `POST /v1/admin/staff` (#124, RUL-62): a one-time password is returned once; TOTP enrolment is forced at
/// the first login (no secret yet). Also used by the `create-admin` CLI.
public sealed record CreateStaffCommand(string Email, string Name, IReadOnlyList<string> Roles) : ICommand<CreateStaffResponse>;

internal sealed class CreateStaffCommandHandler(
    IStaffUserRepository staff,
    IPasswordHasher hasher,
    IMapper mapper,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<CreateStaffCommand, CreateStaffResponse>
{
    public async Task<Result<CreateStaffResponse>> HandleAsync(CreateStaffCommand command, CancellationToken ct = default)
    {
        if (await staff.FindByEmailAsync(command.Email.Trim(), ct) is not null)
        {
            return StaffErrors.EmailTaken;
        }

        var password = hasher.GenerateOneTimePassword();
        var user = StaffUser.Create(command.Email, command.Name, hasher.Hash(password), command.Roles, clock.UtcNow);
        staff.Add(user);
        await uow.SaveChangesAsync(ct);
        return new CreateStaffResponse(mapper.Map<StaffUserResponse>(user), password);
    }
}

internal sealed class CreateStaffCommandValidator : AbstractValidator<CreateStaffCommand>
{
    public CreateStaffCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Roles).NotEmpty();
        RuleForEach(c => c.Roles).Must(Roles.IsValid).WithMessage("Unknown role.");
    }
}
