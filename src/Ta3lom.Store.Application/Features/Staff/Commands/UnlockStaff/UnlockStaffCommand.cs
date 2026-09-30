namespace Ta3lom.Store.Application.Features.Staff.Commands.UnlockStaff;

/// `POST /v1/admin/staff/{id}/unlock` (#128).
public sealed record UnlockStaffCommand(Guid Id) : ICommand;

internal sealed class UnlockStaffCommandHandler(IStaffUserRepository staff, IUnitOfWork uow) : ICommandHandler<UnlockStaffCommand>
{
    public async Task<Result> HandleAsync(UnlockStaffCommand command, CancellationToken ct = default)
    {
        var user = await staff.GetByIdAsync(command.Id, ct);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        user.Unlock();
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
