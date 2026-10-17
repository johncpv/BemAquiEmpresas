using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;

namespace BemAqui.BuildingBlocks.Email;

public interface IEmailSender
{
    /// <summary>
    /// Enfileira o e-mail na transação do comando: grava <c>email_logs</c> (Queued) e o evento no outbox; o envio
    /// acontece depois, no job da fila <c>email</c> (FND-08 CA 1). Devolve o id do log.
    /// </summary>
    Guid Enqueue(string template, string to, IReadOnlyDictionary<string, object?> model, string? relatedType = null, Guid? relatedId = null);
}

public sealed class OutboxEmailSender(BemAquiDbContext db, IEventPublisher events, TimeProvider clock) : IEmailSender
{
    public Guid Enqueue(string template, string to, IReadOnlyDictionary<string, object?> model, string? relatedType = null, Guid? relatedId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        if (!EmailTemplateRenderer.SystemTemplates.Contains(template))
        {
            throw new ArgumentException("Template de e-mail desconhecido: " + template, nameof(template));
        }

        var log = new EmailLog
        {
            To = to.Trim(),
            Template = template,
            Model = JsonSerializer.Serialize(model),
            RelatedType = relatedType,
            RelatedId = relatedId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.EmailLogs.Add(log);
        events.Publish(new EmailQueued(log.Id));
        return log.Id;
    }
}
