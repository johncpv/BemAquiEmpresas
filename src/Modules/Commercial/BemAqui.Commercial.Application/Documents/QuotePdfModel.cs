namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// O que o PDF mostra (COM-03 CA 1 e 2): montado do DTO sem custo; não existe campo de custo, markup, margem, cargo ou
/// custo-hora aqui, para nenhum usuário.
/// </summary>
public sealed record QuotePdfModel(
    QuotePdfCompany Company, string Number, int Revision, DateOnly IssueDate, QuotePdfCustomer Customer, IReadOnlyList<QuotePdfItem> Items, decimal Total,
    IReadOnlyList<QuotePdfMilestone> Milestones, DateOnly? ValidUntil, int? DeliveryDays, string? Notes);

public sealed record QuotePdfCompany(string Name, string LegalName, string? Document, string? Address, string? Contact, byte[]? Logo);

public sealed record QuotePdfCustomer(string Name, string? Document, string? SiteAddress, string? Contact);

public sealed record QuotePdfItem(int Sequence, string Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal Total);

public sealed record QuotePdfMilestone(string Description, decimal Percent, decimal Amount, string When);
