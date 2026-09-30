namespace Ta3lom.Store.Application.Interfaces;

/// Renders `message_template` rows (Scriban, RUL-63). Missing locale falls back to Arabic.
public interface ITemplateRenderer
{
    Task<RenderedMessage> RenderAsync(string templateKey, MessageChannel channel, string locale, object model, CancellationToken ct = default);
}

public sealed record RenderedMessage(string? Subject, string Body);
