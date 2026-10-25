using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Domain;

/// <summary>
/// Envio do pedido ao fornecedor por e-mail (<c>purchasing.purchase_order_dispatches</c>, CMP-03b CA 2 a 4): o e-mail da fila, o PDF
/// guardado, para quem e a cópia (dado pessoal), se o destinatário fora dos contatos foi confirmado, quem e quando. Cada envio e cada
/// reenvio é um registro; nunca é apagado nem alterado.
/// </summary>
[DocumentName("O envio do pedido")]
public sealed class PurchaseOrderDispatch : Entity, IAuditable
{
    /// <summary>O pedido (coluna <c>purchase_order_id</c>); a propriedade chama <c>OrderId</c> para o histórico do pedido achar o envio.</summary>
    public Guid OrderId { get; init; }

    public Guid EmailLogId { get; init; }

    public Guid AttachmentId { get; init; }

    [PersonalData]
    public string To { get; init; } = string.Empty;

    /// <summary>Até 3 cópias, separadas por vírgula.</summary>
    [PersonalData]
    public string? Cc { get; init; }

    /// <summary>Algum endereço não estava nos contatos do fornecedor e o usuário marcou "Confirmo o destinatário" (CA 6).</summary>
    public bool RecipientConfirmed { get; init; }

    /// <summary>Reenvio: o pedido já tinha sido enviado (CA 4).</summary>
    public bool IsResend { get; init; }

    public Guid? SentBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
