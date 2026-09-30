using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Application.Features.AdminOps;

/// Message templates (#120–#122; FR-S77, RUL-63): list; edit — both subject and body must parse as Scriban
/// (`template.render_failed` with line and column), audited; test send of the saved text to one recipient.
public sealed record ListTemplatesQuery : IQuery<IReadOnlyList<TemplateResponse>>;

public sealed record UpdateTemplateCommand(string Key, string Channel, string Locale, TemplateUpdateRequest Input) : ICommand<TemplateResponse>;

public sealed record TestTemplateCommand(string Key, string Channel, string Locale, TemplateTestRequest Input) : ICommand<TemplateTestResponse>;

internal sealed partial class TemplateHandlers(IApplicationDbContext db, IGenericRepository<MessageTemplate> templates, ITemplateRenderer renderer,
    ISmsSender sms, IEmailSender email, IUnitOfWork uow, IClock clock) :
    IQueryHandler<ListTemplatesQuery, IReadOnlyList<TemplateResponse>>,
    ICommandHandler<UpdateTemplateCommand, TemplateResponse>,
    ICommandHandler<TestTemplateCommand, TemplateTestResponse>
{
    public async Task<Result<IReadOnlyList<TemplateResponse>>> HandleAsync(ListTemplatesQuery query, CancellationToken ct = default) =>
        (await db.MessageTemplates.AsNoTracking().OrderBy(t => t.Key).ThenBy(t => t.Channel).ThenBy(t => t.Locale).ToListAsync(ct)).Select(Map).ToList();

    public async Task<Result<TemplateResponse>> HandleAsync(UpdateTemplateCommand command, CancellationToken ct = default)
    {
        var t = await FindAsync(command.Key, command.Channel, command.Locale, ct);
        if (t is null)
        {
            return GeneralErrors.NotFound;
        }

        var body = command.Input.Body!;
        if (renderer.Check(body) is { } bodyError)
        {
            return GivingErrors.TemplateRenderFailed(bodyError.Message, bodyError.Line, bodyError.Column).With("field", "body");
        }

        if (command.Input.Subject is { } subject && renderer.Check(subject) is { } subjectError)
        {
            return GivingErrors.TemplateRenderFailed(subjectError.Message, subjectError.Line, subjectError.Column).With("field", "subject");
        }

        t.Edit(t.Channel == MessageChannel.Email ? command.Input.Subject : null, body, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return Map(t);
    }

    public async Task<Result<TemplateTestResponse>> HandleAsync(TestTemplateCommand command, CancellationToken ct = default)
    {
        var t = await FindAsync(command.Key, command.Channel, command.Locale, ct);
        if (t is null)
        {
            return GeneralErrors.NotFound;
        }

        var recipient = command.Input.Recipient!.Trim();
        if (t.Channel == MessageChannel.Sms ? !SaudiMobile.TryNormalize(recipient, out recipient) : !recipient.Contains('@', StringComparison.Ordinal))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["recipient"] = [t.Channel == MessageChannel.Sms ? "must be a Saudi mobile" : "must be an e-mail address"] });
        }

        var model = Variables(t).ToDictionary(v => v, v => (object?)$"[{v}]", StringComparer.Ordinal);
        foreach (var (k, v) in command.Input.SampleData ?? new Dictionary<string, string>())
        {
            model[k] = v;
        }

        var rendered = new TemplateRendered(t.Subject is null ? null : renderer.RenderText(t.Subject, model), renderer.RenderText(t.Body, model));
        if (t.Channel == MessageChannel.Sms)
        {
            await sms.SendAsync(recipient, rendered.Body, ct);
        }
        else
        {
            await email.SendAsync(new EmailMessage(recipient, rendered.Subject ?? t.Key, rendered.Body, []), ct);
        }

        return new TemplateTestResponse(rendered);
    }

    private async Task<MessageTemplate?> FindAsync(string key, string channel, string locale, CancellationToken ct) =>
        EnumWire.TryParse<MessageChannel>(channel, out var c)
            ? await templates.FirstOrDefaultAsync(t => t.Key == key && t.Channel == c && t.Locale == locale, ct)
            : null;

    private static TemplateResponse Map(MessageTemplate t) =>
        new(t.Key, t.Channel.ToWire(), t.Locale, t.Subject, t.Body, t.UpdatedAt, Variables(t));

    private static List<string> Variables(MessageTemplate t) =>
        [.. Placeholder().Matches((t.Subject ?? string.Empty) + " " + t.Body).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\{\s*([a-z_][a-z0-9_]*)\s*\}\}")]
    private static partial Regex Placeholder();
}

internal sealed class UpdateTemplateCommandValidator : AbstractValidator<UpdateTemplateCommand>
{
    public UpdateTemplateCommandValidator()
    {
        RuleFor(c => c.Input.Body).NotEmpty().MaximumLength(4000);
        RuleFor(c => c.Input.Subject).MaximumLength(200);
    }
}

internal sealed class TestTemplateCommandValidator : AbstractValidator<TestTemplateCommand>
{
    public TestTemplateCommandValidator() => RuleFor(c => c.Input.Recipient).NotEmpty();
}
