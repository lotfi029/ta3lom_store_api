using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Beneficiaries;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Beneficiaries.Commands;

/// `POST /v1/admin/beneficiaries` (#101, RUL-51): a `pending` beneficiary with a `B-000123` code; PII encrypted at the
/// converter; a mobile already registered (by `mobile_hash`) → `beneficiary.mobile_exists`. Audited (staff actor).
public sealed record CreateBeneficiaryCommand(BeneficiaryInput Input) : ICommand<BeneficiaryRow>;

/// `PUT /v1/admin/beneficiaries/{id}` (#103).
public sealed record UpdateBeneficiaryCommand(Guid Id, BeneficiaryInput Input) : ICommand<BeneficiaryRow>;

internal sealed class SaveBeneficiaryCommandHandler(IBeneficiaryRepository beneficiaries, IPseudonymizer hasher, ICurrentActor actor, IUnitOfWork uow,
    IClock clock) : ICommandHandler<CreateBeneficiaryCommand, BeneficiaryRow>, ICommandHandler<UpdateBeneficiaryCommand, BeneficiaryRow>
{
    public async Task<Result<BeneficiaryRow>> HandleAsync(CreateBeneficiaryCommand command, CancellationToken ct = default)
    {
        var details = BeneficiaryRules.TryParse(command.Input, hasher, out var errors);
        if (details is null)
        {
            return Error.Validation(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
        }

        if (details.MobileHash is { } hash && await beneficiaries.ExistsAsync(b => b.MobileHash == hash, ct))
        {
            return BeneficiaryErrors.MobileExists;
        }

        var b = Beneficiary.Register(await beneficiaries.NextCodeAsync(ct), details, actor.Name ?? "staff", clock.UtcNow);
        beneficiaries.Add(b);
        await uow.SaveChangesAsync(ct);
        return BeneficiaryRules.ToRow(b);
    }

    public async Task<Result<BeneficiaryRow>> HandleAsync(UpdateBeneficiaryCommand command, CancellationToken ct = default)
    {
        var b = await beneficiaries.GetByIdAsync(command.Id, ct);
        if (b is null)
        {
            return BeneficiaryErrors.NotFound;
        }

        var details = BeneficiaryRules.TryParse(command.Input, hasher, out var errors);
        if (details is null)
        {
            return Error.Validation(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
        }

        if (details.MobileHash is { } hash && await beneficiaries.ExistsAsync(x => x.MobileHash == hash && x.Id != b.Id, ct))
        {
            return BeneficiaryErrors.MobileExists;
        }

        b.Update(details);
        await uow.SaveChangesAsync(ct);
        return BeneficiaryRules.ToRow(b);
    }
}

/// `POST /v1/admin/beneficiaries/{id}/eligibility` (#104, RUL-51): approve, reject or deactivate with verifier and time.
public sealed record SetEligibilityCommand(Guid Id, EligibilityInput Input) : ICommand<BeneficiaryRow>;

internal sealed class SetEligibilityCommandHandler(IBeneficiaryRepository beneficiaries, ICurrentActor actor, IUnitOfWork uow, IClock clock)
    : ICommandHandler<SetEligibilityCommand, BeneficiaryRow>
{
    public async Task<Result<BeneficiaryRow>> HandleAsync(SetEligibilityCommand command, CancellationToken ct = default)
    {
        var b = await beneficiaries.GetByIdAsync(command.Id, ct);
        if (b is null)
        {
            return BeneficiaryErrors.NotFound;
        }

        b.SetEligibility(EnumWire.Parse<EligibilityStatus>(command.Input.Status!), command.Input.ZakatEligible, command.Input.Notes,
            actor.Name ?? "staff", clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return BeneficiaryRules.ToRow(b);
    }
}

internal sealed class SetEligibilityCommandValidator : AbstractValidator<SetEligibilityCommand>
{
    public SetEligibilityCommandValidator()
    {
        RuleFor(c => c.Input.Status).Must(s => s is "approved" or "rejected" or "inactive").WithMessage("status must be approved, rejected or inactive.");
        RuleFor(c => c.Input.Notes).MaximumLength(BeneficiaryRules.NotesMax);
    }
}

/// `POST /v1/admin/beneficiaries/{id}/reveal` (#105, FR-S52, RUL-62): plaintext PII for `allocation_officer` only, written
/// to `audit_log` as `beneficiary.reveal` with the reason (never the revealed values).
public sealed record RevealBeneficiaryCommand(Guid Id, string Reason) : ICommand<BeneficiaryPii>;

internal sealed class RevealBeneficiaryCommandHandler(IApplicationDbContext db, IGenericRepository<AuditLogEntry> audit, ICurrentActor actor,
    IUnitOfWork uow, IClock clock) : ICommandHandler<RevealBeneficiaryCommand, BeneficiaryPii>
{
    public async Task<Result<BeneficiaryPii>> HandleAsync(RevealBeneficiaryCommand command, CancellationToken ct = default)
    {
        var b = await db.Beneficiaries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (b is null)
        {
            return BeneficiaryErrors.NotFound;
        }

        var now = clock.UtcNow;
        audit.Add(new AuditLogEntry(actor.Name ?? "staff", "beneficiary.reveal", "beneficiary", now, b.Id.ToString(),
            JsonSerializer.Serialize(new { code = b.Code, reason = command.Reason.Trim() }), actor.IpHash));
        await uow.SaveChangesAsync(ct);
        return new BeneficiaryPii(b.Code, b.FullName, b.Mobile, b.GuardianName, now);
    }
}

internal sealed class RevealBeneficiaryCommandValidator : AbstractValidator<RevealBeneficiaryCommand>
{
    public RevealBeneficiaryCommandValidator() => RuleFor(c => c.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
}

/// `POST /v1/admin/beneficiaries/{id}/invite` (#106, RUL-52): re-sends the SMS invitation for a live sponsored course seat
/// (`BeneficiaryInvite`, sent from S-25); none → `beneficiary.no_live_seat`.
public sealed record InviteBeneficiaryCommand(Guid Id) : ICommand;

internal sealed class InviteBeneficiaryCommandHandler(IApplicationDbContext db, IOutbox outbox, IUnitOfWork uow) : ICommandHandler<InviteBeneficiaryCommand>
{
    public async Task<Result> HandleAsync(InviteBeneficiaryCommand command, CancellationToken ct = default)
    {
        if (!await db.Beneficiaries.AnyAsync(b => b.Id == command.Id, ct))
        {
            return BeneficiaryErrors.NotFound;
        }

        var enrolmentId = await (from u in db.SponsoredUnits.AsNoTracking()
                                 join e in db.Enrolments.AsNoTracking() on u.Id equals e.SponsoredUnitId
                                 where u.BeneficiaryId == command.Id && u.Status == UnitStatus.Allocated
                                       && (e.Status == EnrolmentStatus.PendingSync || e.Status == EnrolmentStatus.Active)
                                 orderby u.AllocatedAt descending
                                 select (Guid?)e.Id).FirstOrDefaultAsync(ct);
        if (enrolmentId is null)
        {
            return BeneficiaryErrors.NoLiveSeat;
        }

        outbox.Enqueue(new BeneficiaryInvite(command.Id, enrolmentId.Value));
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
