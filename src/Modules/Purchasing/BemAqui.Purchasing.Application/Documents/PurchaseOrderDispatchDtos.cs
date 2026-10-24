using BemAqui.BuildingBlocks.Attachments;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// Envio do pedido na aba Documentos (CMP-03b CA 3): destinatário e cópia (mascarados sem <c>parties.view</c>), quem e quando, o PDF
/// enviado e a situação do e-mail (<c>Queued</c>, <c>Sent</c>, <c>Failed</c>, <c>Skipped</c>), com o motivo da falha e se ainda espera a
/// verificação do anexo.
/// </summary>
public sealed record PurchaseOrderDispatchView(
    Guid Id, string To, string? Cc, bool RecipientConfirmed, bool IsResend, string? SentByName, DateTimeOffset CreatedAt, Guid AttachmentId, string? AttachmentName, string Status,
    string? Error, bool WaitingScan, DateTimeOffset? SentAt);

/// <summary>Aba Documentos (CMP-03b CA 3): os PDFs gerados nos envios, os envios e os anexos do usuário.</summary>
public sealed record PurchaseOrderDocuments(IReadOnlyList<AttachmentDto> Generated, IReadOnlyList<AttachmentDto> Uploaded, IReadOnlyList<PurchaseOrderDispatchView> Dispatches, bool CompanyComplete);

/// <summary>
/// Diálogo "Enviar ao fornecedor" já preenchido (CMP-03b CA 2, R-PRP-1): o destinatário padrão (o contato que recebe cobrança ou o
/// primeiro com e-mail), o assunto, os e-mails dos contatos do fornecedor (para o aviso de fora dos contatos) e se é reenvio.
/// </summary>
public sealed record PurchaseOrderDispatchDraft(string? To, string Subject, string SupplierName, IReadOnlyList<string> SupplierEmails, bool IsResend, bool CompanyComplete);

/// <summary>Envio ou reenvio (CMP-03b CA 2, 4 e 6): destinatário e até 3 cópias (endereços puros), a mensagem opcional e a confirmação.</summary>
public sealed record PurchaseOrderDispatchInput(string To, IReadOnlyList<string>? Cc, string? Message, bool ConfirmRecipient, uint Version);
