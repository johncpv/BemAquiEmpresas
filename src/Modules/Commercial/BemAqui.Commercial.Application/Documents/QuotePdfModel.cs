using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Commercial.Domain;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// O que o PDF mostra (COM-03 CA 1 e 2): montado do DTO sem custo; não existe campo de custo, markup, margem, cargo ou
/// custo-hora aqui, para nenhum usuário.
/// </summary>
public sealed record QuotePdfModel(
    QuotePdfCompany Company, string Number, int Revision, DateOnly IssueDate, QuotePdfCustomer Customer, IReadOnlyList<QuotePdfItem> Items, decimal Total,
    IReadOnlyList<QuotePdfMilestone> Milestones, DateOnly? ValidUntil, int? DeliveryDays, string? Notes, PdfMark Mark = PdfMark.None, string? Notice = null);

/// <summary>Marca e faixa do PDF do orçamento pela situação da revisão (EXP-03 CA 8), sem banco.</summary>
public static class QuotePdf
{
    public const string RejectedNotice = "Orçamento recusado";

    /// <summary>Vencido: só a faixa com a data (sem marca d'água); recusado: "Cancelado" e a faixa; rascunho, enviado e aprovado: nada.</summary>
    public static (PdfMark Mark, string? Notice) Mark(QuoteStatus status, DateOnly? validUntil, DateTimeOffset? expiredAt) => status switch
    {
        QuoteStatus.Expired => (PdfMark.None,
            $"Validade vencida em {(validUntil is { } v ? PdfTheme.Date(v) : expiredAt is { } e ? PdfTheme.DateTime(e)[..10] : "—")}: reabra com uma nova revisão."),
        QuoteStatus.Rejected => (PdfMark.Cancelled, RejectedNotice),
        _ => (PdfMark.None, null),
    };
}

public sealed record QuotePdfCompany(string Name, string LegalName, string? Document, string? Address, string? Contact, byte[]? Logo);

public sealed record QuotePdfCustomer(string Name, string? Document, string? SiteAddress, string? Contact);

public sealed record QuotePdfItem(int Sequence, string Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal Total);

public sealed record QuotePdfMilestone(string Description, decimal Percent, decimal Amount, string When);
