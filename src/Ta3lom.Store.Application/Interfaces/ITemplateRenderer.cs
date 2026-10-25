namespace Ta3lom.Store.Application.Interfaces;

/// Renders `message_template` rows (Scriban). Missing locale falls back to Arabic.
public interface ITemplateRenderer
{
    Task<RenderedMessage> RenderAsync(string templateKey, MessageChannel channel, string locale, object model, CancellationToken ct = default);

    /// Parses a template text without rendering it: null when valid, else the first error with its position.
    TemplateSyntaxError? Check(string text);

    /// Renders an unsaved text with sample values (template test send, #122).
    string RenderText(string text, IReadOnlyDictionary<string, object?> model);
}

public sealed record TemplateSyntaxError(string Message, int Line, int Column);

public sealed record RenderedMessage(string? Subject, string Body);
