using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;

namespace BemAqui.Commercial.Application.Documents;

public sealed record QuoteDispatchView(Guid Id, string To, string? Cc, string? SentByName, DateTimeOffset CreatedAt, Guid AttachmentId, string Status, string? Error);

/// <summary>Aba Documentos (CA 3, 13 e 15): PDFs gerados, envios com a situação do e-mail e anexos do usuário.</summary>
public sealed record QuoteDocuments(IReadOnlyList<AttachmentDto> Generated, IReadOnlyList<AttachmentDto> Uploaded, IReadOnlyList<QuoteDispatchView> Dispatches, bool CompanyComplete);

/// <summary>Envio (CA 7 e 9): destinatário e até 3 cópias, endereços puros; fora dos contatos do cliente, exige confirmar.</summary>
public sealed record QuoteSendInput(string To, IReadOnlyList<string>? Cc, string? Message, bool ConfirmRecipient, uint Version);

/// <summary>Sugestões do diálogo de envio (CA 7): destinatário, assunto e mensagem padrão.</summary>
public sealed record QuoteSendDraft(string? To, string Subject, string Message, IReadOnlyList<string> CustomerEmails);
