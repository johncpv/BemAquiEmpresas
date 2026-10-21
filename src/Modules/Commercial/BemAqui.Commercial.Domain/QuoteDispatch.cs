using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>Envio do orçamento por e-mail (<c>commercial.quote_dispatches</c>, COM-03 CA 8): para quem, a cópia, o PDF e o e-mail da fila.</summary>
public sealed class QuoteDispatch : Entity, IAuditable
{
    public Guid QuoteId { get; init; }

    public Guid EmailLogId { get; init; }

    public Guid AttachmentId { get; init; }

    [PersonalData]
    public string To { get; init; } = string.Empty;

    [PersonalData]
    public string? Cc { get; init; }

    public Guid? SentBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
