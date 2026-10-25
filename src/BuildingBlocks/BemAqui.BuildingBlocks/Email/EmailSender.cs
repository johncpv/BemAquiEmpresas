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

    /// <summary>Com anexos (ids de <c>shared.attachments</c> do mesmo documento), cópia, responder-para e nome do remetente (COM-03).</summary>
    Guid Enqueue(EmailRequest request);
}

public sealed record EmailRequest(
    string Template, string To, IReadOnlyDictionary<string, object?> Model, string? RelatedType = null, Guid? RelatedId = null, IReadOnlyList<Guid>? AttachmentIds = null,
    IReadOnlyList<string>? Cc = null, string? ReplyTo = null, string? FromName = null, Guid? RequestedBy = null);

public sealed class OutboxEmailSender(BemAquiDbContext db, IEventPublisher events, TimeProvider clock) : IEmailSender
{
    public Guid Enqueue(string template, string to, IReadOnlyDictionary<string, object?> model, string? relatedType = null, Guid? relatedId = null) =>
        Enqueue(new EmailRequest(template, to, model, relatedType, relatedId));

    public Guid Enqueue(EmailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (template, to, model) = (request.Template, request.To, request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.To, nameof(request));
        if (!EmailTemplateRenderer.SystemTemplates.Contains(template))
        {
            throw new ArgumentException("Template de e-mail desconhecido: " + template, nameof(request));
        }

        var log = new EmailLog
        {
            To = to.Trim(),
            Template = template,
            Model = JsonSerializer.Serialize(model),
            RelatedType = request.RelatedType,
            RelatedId = request.RelatedId,
            AttachmentIds = request.AttachmentIds?.ToArray() ?? [],
            Cc = request.Cc is { Count: > 0 } cc ? string.Join(",", cc) : null,
            ReplyTo = request.ReplyTo,
            FromName = request.FromName,
            RequestedBy = request.RequestedBy,
            CreatedAt = clock.GetUtcNow(),
        };
        db.EmailLogs.Add(log);
        events.Publish(new EmailQueued(log.Id));
        return log.Id;
    }
}
