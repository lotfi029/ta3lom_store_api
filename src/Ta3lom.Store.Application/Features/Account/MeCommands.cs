using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.DTOs.Checkout;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Account;

/// `PATCH /v1/me` (#22, FR-S22, RUL-61): only the fields sent change; the mobile is the login and never changes in S1.
public sealed record UpdateProfileCommand(UpdateProfileRequest Input) : ICommand<ProfileResponse>;

/// Addresses (#24–#27, FR-S22, RUL-26): Saudi National Address; the first one becomes the default; up to 10.
public sealed record SaveAddressCommand(Guid? AddressId, AddressInput Input) : ICommand<AddressResponse>;

public sealed record DeleteAddressCommand(Guid AddressId) : ICommand;

public sealed record SetDefaultAddressCommand(Guid AddressId) : ICommand<AddressResponse>;

/// `DELETE /v1/me` (#39, FR-S27, RUL-61, database/03 §7): anonymises the account — name/email cleared, the mobile replaced,
/// addresses deleted, every refresh token revoked, the orders' contact and recipient replaced with "محذوف / deleted",
/// purchased enrolments revoked (sponsored ones stay). Financial rows are kept. The endpoint clears the refresh cookie.
public sealed record DeleteAccountCommand(bool Confirm) : ICommand;

internal sealed class MeCommandHandlers(
    IApplicationDbContext db,
    ICustomerRepository customers,
    IGenericRepository<CustomerAddress> addresses,
    IRefreshTokenRepository tokens,
    IOrderRepository orders,
    IGenericRepository<Shipment> shipments,
    IGenericRepository<Enrolment> enrolments,
    IOutbox outbox,
    ICurrentActor actor,
    IUnitOfWork uow,
    IClock clock) :
    ICommandHandler<UpdateProfileCommand, ProfileResponse>,
    ICommandHandler<SaveAddressCommand, AddressResponse>,
    ICommandHandler<DeleteAddressCommand>,
    ICommandHandler<SetDefaultAddressCommand, AddressResponse>,
    ICommandHandler<DeleteAccountCommand>
{
    public const int MaxAddresses = 10;

    private Guid Me => actor.Id ?? throw new InvalidOperationException("customer route without a customer");

    public async Task<Result<ProfileResponse>> HandleAsync(UpdateProfileCommand command, CancellationToken ct = default)
    {
        var i = command.Input;
        var c = (await customers.GetByIdAsync(Me, ct))!;
        var now = clock.UtcNow;
        c.UpdateProfile(i.FullName, i.Email, i.Locale, i.AnonymousDefault, i.MarketingOptIn, now);
        if (i.PdplConsent == true)
        {
            c.RecordPdplConsent(now);
        }

        await uow.SaveChangesAsync(ct);
        return new ProfileResponse(c.Id, c.Mobile, c.FullName, c.Email, c.Locale, c.AnonymousDefault, c.MarketingOptIn, c.PdplConsentAt, c.CreatedAt);
    }

    public async Task<Result<AddressResponse>> HandleAsync(SaveAddressCommand command, CancellationToken ct = default)
    {
        var i = command.Input;
        SaudiMobile.TryNormalize(i.RecipientMobile, out var mobile);
        var value = new ShippingAddress(i.RecipientName.Trim(), mobile, i.City.Trim(), Clean(i.District), Clean(i.ShortAddress)?.ToUpperInvariant(),
            Clean(i.Street), Clean(i.BuildingNo), Clean(i.PostalCode));
        if (command.AddressId is { } id)
        {
            var existing = await addresses.FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == Me, ct);
            if (existing is null)
            {
                return GeneralErrors.NotFound;
            }

            existing.Update(value);
            await uow.SaveChangesAsync(ct);
            return MeMapping.Address(existing);
        }

        var count = await db.CustomerAddresses.CountAsync(a => a.CustomerId == Me, ct);
        if (count >= MaxAddresses)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["address"] = [$"up to {MaxAddresses} saved addresses"] });
        }

        var address = CustomerAddress.Create(Me, value, clock.UtcNow);
        address.SetDefault(count == 0);
        addresses.Add(address);
        await uow.SaveChangesAsync(ct);
        return MeMapping.Address(address);
    }

    public async Task<Result> HandleAsync(DeleteAddressCommand command, CancellationToken ct = default)
    {
        var address = await addresses.FirstOrDefaultAsync(a => a.Id == command.AddressId && a.CustomerId == Me, ct);
        if (address is null)
        {
            return GeneralErrors.NotFound;
        }

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            addresses.Remove(address);
            await uow.SaveChangesAsync(token);
            if (address.IsDefault
                && await addresses.ListAsync(a => a.CustomerId == Me, token) is { Count: > 0 } rest)
            {
                rest.OrderByDescending(a => a.CreatedAt).First().SetDefault(true);                   // the newest becomes the default
                await uow.SaveChangesAsync(token);
            }

            return Result.Success();
        }, ct);
    }

    public async Task<Result<AddressResponse>> HandleAsync(SetDefaultAddressCommand command, CancellationToken ct = default)
    {
        var mine = await addresses.ListAsync(a => a.CustomerId == Me, ct);
        var target = mine.FirstOrDefault(a => a.Id == command.AddressId);
        if (target is null)
        {
            return GeneralErrors.NotFound;
        }

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            foreach (var a in mine.Where(a => a.IsDefault && a.Id != target.Id))
            {
                a.SetDefault(false);
            }

            await uow.SaveChangesAsync(token);                                                          // ux_address_default: clear first
            target.SetDefault(true);
            await uow.SaveChangesAsync(token);
            return Result.Success(MeMapping.Address(target));
        }, ct);
    }

    public async Task<Result> HandleAsync(DeleteAccountCommand command, CancellationToken ct = default)
    {
        if (!command.Confirm)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["confirm"] = ["must be true"] });
        }

        var me = Me;
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var now = clock.UtcNow;
            var customer = (await customers.GetByIdAsync(me, token))!;
            customer.Anonymise(now);
            foreach (var a in await addresses.ListAsync(a => a.CustomerId == me, token))
            {
                addresses.Remove(a);
            }

            await tokens.RevokeAllForSubjectAsync(TokenSubjectType.Customer, me, now, token);
            var orderIds = await db.Orders.AsNoTracking().Where(o => o.CustomerId == me).Select(o => o.Id).ToListAsync(token);
            foreach (var id in orderIds)
            {
                (await orders.GetByIdAsync(id, token))?.AnonymiseContact();
            }

            foreach (var s in await shipments.ListAsync(s => orderIds.Contains(s.OrderId), token))
            {
                s.AnonymiseRecipient();
            }

            foreach (var e in await enrolments.ListAsync(e => e.CustomerId == me && e.Source != EnrolmentSource.Sponsored
                         && (e.Status == EnrolmentStatus.PendingSync || e.Status == EnrolmentStatus.Active || e.Status == EnrolmentStatus.Completed), token))
            {
                e.Revoke();
                outbox.Enqueue(new LmsRevoke(e.Id));
            }

            await uow.SaveChangesAsync(token);
            return Result.Success();
        }, ct);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

internal sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(c => c.Input.FullName).Length(2, 100).When(c => c.Input.FullName is not null);
        RuleFor(c => c.Input.Email).EmailAddress().MaximumLength(200).When(c => c.Input.Email is not null);
        RuleFor(c => c.Input.Locale).Must(l => l is "ar" or "en").When(c => c.Input.Locale is not null).WithMessage("locale must be ar or en.");
    }
}

internal sealed class SaveAddressCommandValidator : AbstractValidator<SaveAddressCommand>
{
    public SaveAddressCommandValidator()
    {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.RecipientName).NotEmpty().Length(2, 100);
        RuleFor(c => c.Input.RecipientMobile).Must(SaudiMobile.IsValid).WithMessage("must be a Saudi mobile (05XXXXXXXX)");
        RuleFor(c => c.Input.City).NotEmpty().MaximumLength(60);
        RuleFor(c => c.Input.ShortAddress).Matches("^[A-Za-z]{4}[0-9]{4}$").When(c => !string.IsNullOrWhiteSpace(c.Input.ShortAddress));
        RuleFor(c => c.Input.PostalCode).Matches("^[0-9]{5}$").When(c => !string.IsNullOrWhiteSpace(c.Input.PostalCode));
        RuleFor(c => c.Input.BuildingNo).MaximumLength(10);
        RuleFor(c => c.Input.District).MaximumLength(100);
        RuleFor(c => c.Input.Street).MaximumLength(150);
    }
}
