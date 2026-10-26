using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Application.Features.AdminOps;

/// `POST /v1/admin/customers/{id}/block` (D-13; admin, store_manager): the donor's sessions are revoked at once and the
/// block is audited (`customer.blocked`). An anonymised donor is not found.
public sealed record BlockCustomerCommand(Guid Id) : ICommand;

internal sealed class BlockCustomerCommandHandler(ICustomerRepository customers, IRefreshTokenRepository refreshTokens, IAuditTrail audit,
    IUnitOfWork uow, IClock clock) : ICommandHandler<BlockCustomerCommand>
{
    public Task<Result> HandleAsync(BlockCustomerCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (await customers.GetByIdAsync(command.Id, token) is not { IsAnonymised: false } customer)
            {
                return Result.Failure(GeneralErrors.NotFound);
            }

            var now = clock.UtcNow;
            customer.Block(now);
            await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Customer, customer.Id, now, token);
            audit.Record(AuditActions.CustomerBlocked, "customer", customer.Id.ToString());
            return Result.Success();
        }, ct);
}

/// `POST /v1/admin/customers/{id}/unblock` (admin, store_manager), audited (`customer.unblocked`).
public sealed record UnblockCustomerCommand(Guid Id) : ICommand;

internal sealed class UnblockCustomerCommandHandler(ICustomerRepository customers, IAuditTrail audit, IUnitOfWork uow, IClock clock)
    : ICommandHandler<UnblockCustomerCommand>
{
    public async Task<Result> HandleAsync(UnblockCustomerCommand command, CancellationToken ct = default)
    {
        if (await customers.GetByIdAsync(command.Id, ct) is not { IsAnonymised: false } customer)
        {
            return GeneralErrors.NotFound;
        }

        customer.Unblock(clock.UtcNow);
        audit.Record(AuditActions.CustomerUnblocked, "customer", customer.Id.ToString());
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// `POST /v1/admin/orders/{reference}/cancel` (D-09; admin, finance): only a pending order; audited (`order.cancelled`)
/// and `OrderCancelled` is queued.
public sealed record CancelOrderCommand(string Reference) : ICommand<AdminOrderRow>;

internal sealed class CancelOrderCommandHandler(IGenericRepository<Order> orders, ICurrentActor actor, IAuditTrail audit, IUnitOfWork uow, IClock clock)
    : ICommandHandler<CancelOrderCommand, AdminOrderRow>
{
    public async Task<Result<AdminOrderRow>> HandleAsync(CancelOrderCommand command, CancellationToken ct = default)
    {
        var reference = Order.NormalizeReference(command.Reference);                 // L-17/API-2: references are stored upper-case
        var order = await orders.FirstOrDefaultAsync(o => o.Reference == reference, ct);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        var cancelled = order.Cancel(actor.Name ?? "staff", clock.UtcNow);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        audit.Record(AuditActions.OrderCancelled, "orders", order.Id.ToString(), details: new { order.Reference });
        await uow.SaveChangesAsync(ct);
        return AdminOrderMapping.ToRow(order);
    }
}

/// `GET /v1/admin/templates` (admin, store_manager).
public sealed record ListTemplatesQuery : IQuery<IReadOnlyList<TemplateResponse>>;

internal sealed class ListTemplatesQueryHandler(IApplicationDbContext db) : IQueryHandler<ListTemplatesQuery, IReadOnlyList<TemplateResponse>>
{
    public async Task<Result<IReadOnlyList<TemplateResponse>>> HandleAsync(ListTemplatesQuery query, CancellationToken ct = default)
    {
        var rows = await db.MessageTemplates.AsNoTracking().OrderBy(t => t.Key).ThenBy(t => t.Channel).ThenBy(t => t.Locale).ToListAsync(ct);
        return rows.Select(TemplateMapping.ToResponse).ToList();
    }
}

internal static class TemplateMapping
{
    public static TemplateResponse ToResponse(MessageTemplate t) => new(t.Key, t.Channel.ToWire(), t.Locale, t.Subject, t.Body, t.UpdatedAt, t.UpdatedBy);

    public static Error? SyntaxError(ITemplateRenderer renderer, string? subject, string body)
    {
        var error = (subject is null ? null : renderer.Check(subject)) ?? renderer.Check(body);
        return error is null
            ? null
            : Error.Unprocessable(ErrorCodes.TemplateRenderFailed, error.Message).With("line", error.Line).With("column", error.Column);
    }
}

/// `PUT /v1/admin/templates/{key}/{channel}/{locale}`: edits an existing template; the text must parse.
public sealed record UpdateTemplateCommand(string Key, string Channel, string Locale, TemplateInput Input) : ICommand<TemplateResponse>;

internal sealed class UpdateTemplateCommandHandler(IGenericRepository<MessageTemplate> templates, ITemplateRenderer renderer, ICurrentActor actor,
    IUnitOfWork uow, IClock clock) : ICommandHandler<UpdateTemplateCommand, TemplateResponse>
{
    public async Task<Result<TemplateResponse>> HandleAsync(UpdateTemplateCommand command, CancellationToken ct = default)
    {
        if (!EnumWire.TryParse<MessageChannel>(command.Channel, out var channel))
        {
            return GeneralErrors.NotFound;
        }

        var template = await templates.FirstOrDefaultAsync(t => t.Key == command.Key && t.Channel == channel && t.Locale == command.Locale, ct);
        if (template is null)
        {
            return GeneralErrors.NotFound;
        }

        if (TemplateMapping.SyntaxError(renderer, command.Input.Subject, command.Input.Body) is { } error)
        {
            return error;
        }

        template.Edit(channel == MessageChannel.Email ? command.Input.Subject : null, command.Input.Body, actor.Name, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return TemplateMapping.ToResponse(template);
    }
}

internal sealed class UpdateTemplateCommandValidator : AbstractValidator<UpdateTemplateCommand>
{
    public UpdateTemplateCommandValidator()
    {
        RuleFor(c => c.Input.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(c => c.Input.Subject).MaximumLength(300);
    }
}

/// `POST /v1/admin/templates/test`: renders an unsaved text with sample values; an e-mail goes to the calling staff member,
/// an SMS to `to`. Nothing is stored.
public sealed record TestTemplateCommand(TemplateTestRequest Request) : ICommand<TemplateTestResponse>;

internal sealed class TestTemplateCommandHandler(ITemplateRenderer renderer, IEmailSender email, ISmsSender sms, ICurrentActor actor)
    : ICommandHandler<TestTemplateCommand, TemplateTestResponse>
{
    public static readonly IReadOnlyDictionary<string, object?> Sample = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["code"] = "123456",
        ["subject"] = "Test",
        ["body"] = "Test message",
        ["reference"] = "T3-26-000001",
        ["amount"] = "50.00",
        ["name"] = "Test",
    };

    public async Task<Result<TemplateTestResponse>> HandleAsync(TestTemplateCommand command, CancellationToken ct = default)
    {
        var r = command.Request;
        if (TemplateMapping.SyntaxError(renderer, r.Subject, r.Body) is { } error)
        {
            return error;
        }

        var subject = r.Subject is null ? null : renderer.RenderText(r.Subject, Sample);
        var body = renderer.RenderText(r.Body, Sample);
        bool sent;
        if (r.Channel == "sms")
        {
            SaudiMobile.TryNormalize(r.To, out var mobile);
            sent = (await sms.SendAsync(mobile, body, ct)).Sent;
        }
        else
        {
            sent = actor.Name is { } to && (await email.SendAsync(new EmailMessage(to, subject ?? "(test)", body, []), ct)).Sent;
        }

        return new TemplateTestResponse(subject, body, sent);
    }
}

internal sealed class TestTemplateCommandValidator : AbstractValidator<TestTemplateCommand>
{
    public TestTemplateCommandValidator()
    {
        RuleFor(c => c.Request.Channel).Must(c => c is "sms" or "email").WithMessage("channel must be sms or email.");
        RuleFor(c => c.Request.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(c => c.Request.Subject).MaximumLength(300);
        RuleFor(c => c.Request.To).Must(t => SaudiMobile.TryNormalize(t, out _)).When(c => c.Request.Channel == "sms")
            .WithMessage("to must be a Saudi mobile.");
    }
}
