using Microsoft.EntityFrameworkCore;
using Scriban;
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
