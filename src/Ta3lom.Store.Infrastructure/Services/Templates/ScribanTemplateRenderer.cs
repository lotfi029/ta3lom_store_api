using Microsoft.EntityFrameworkCore;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Templates;

/// Renders `message_template` with Scriban (RUL-63). The requested locale falls back to Arabic; model members
/// are exposed with their C# names in snake_case and as-is (`{{ code }}`, `{{ order_ref }}`).
internal sealed class ScribanTemplateRenderer(StoreDbContext db) : ITemplateRenderer
{
    public async Task<RenderedMessage> RenderAsync(string templateKey, MessageChannel channel, string locale, object model, CancellationToken ct = default)
    {
        var candidates = await db.MessageTemplates.AsNoTracking()
            .Where(t => t.Key == templateKey && t.Channel == channel && (t.Locale == locale || t.Locale == "ar"))
            .ToListAsync(ct);
        var template = candidates.FirstOrDefault(t => t.Locale == locale) ?? candidates.FirstOrDefault()
            ?? throw new InvalidOperationException($"Template '{templateKey}/{channel}' is missing (seed).");

        return new RenderedMessage(
            template.Subject is null ? null : Render(template.Subject, model),
            Render(template.Body, model));
    }

    public TemplateSyntaxError? Check(string text)
    {
        var parsed = Template.Parse(text);
        if (parsed.HasErrors)
        {
            var first = parsed.Messages[0];
            return new TemplateSyntaxError(first.Message, first.Span.Start.Line + 1, first.Span.Start.Column + 1);
        }

        // An unclosed `{{` parses as code up to the end of the text without an error: find the unmatched code block.
        var open = (TextPosition?)null;
        foreach (var token in new Lexer(text))
        {
            if (token.Type == TokenType.CodeEnter)
            {
                open = token.Start;
            }
            else if (token.Type == TokenType.CodeExit)
            {
                open = null;
            }
        }

        if (open is { } start)
        {
            return new TemplateSyntaxError("A code block opened with '{{' is not closed with '}}'.", start.Line + 1, start.Column + 1);
        }

        // Some errors (an unknown function, a bad call) only appear when rendered: try once with no values.
        try
        {
            parsed.Render(new TemplateContext { StrictVariables = false });
            return null;
        }
        catch (Scriban.Syntax.ScriptRuntimeException e)
        {
            return new TemplateSyntaxError(e.OriginalMessage, e.Span.Start.Line + 1, e.Span.Start.Column + 1);
        }
    }

    public string RenderText(string text, IReadOnlyDictionary<string, object?> model)
    {
        var parsed = Template.Parse(text);
        if (parsed.HasErrors)
        {
            throw new InvalidOperationException("Template syntax error: " + string.Join("; ", parsed.Messages));
        }

        var globals = new ScriptObject();
        foreach (var (k, v) in model)
        {
            globals[k] = v;
        }

        var context = new TemplateContext { StrictVariables = false };
        context.PushGlobal(globals);
        return parsed.Render(context);
    }

    public static string Render(string text, object model)
    {
        var parsed = Template.Parse(text);
        if (parsed.HasErrors)
        {
            throw new InvalidOperationException("Template syntax error: " + string.Join("; ", parsed.Messages));
        }

        var globals = new ScriptObject();
        globals.Import(model, renamer: member => ToSnake(member.Name));
        globals.Import(model);
        var context = new TemplateContext { StrictVariables = false, MemberRenamer = member => ToSnake(member.Name) };
        context.PushGlobal(globals);
        return parsed.Render(context);
    }

    private static string ToSnake(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
