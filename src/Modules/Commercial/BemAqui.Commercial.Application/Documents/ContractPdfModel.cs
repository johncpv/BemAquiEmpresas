using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// O que o resumo do contrato mostra (EXP-04 CA 4 e 6): cliente, projeto, origem, valor, itens com preço, marcos com a situação da parcela,
/// data prometida, endereço da obra e aditivos aprovados. <b>Não existe</b> campo de custo nem de margem aqui, para nenhum usuário (CA 3,
/// conferido por teste no record): o papel vai para o cliente assinar.
/// </summary>
public sealed record ContractPdfModel(
    string Number, ContractStatus Status, string StatusLabel, string PartyLegalName, string? PartyDocument, string? PartyAddress, string? ProjectNumber, string? ProjectName,
    string QuoteNumber, int QuoteRevision, DateOnly SignedAt, decimal TotalValue, decimal CurrentValue, DateOnly? PromisedDate, int? DeliveryDays, string? SiteAddress,
    bool IsOnboarding, string? CancelReason, IReadOnlyList<ContractPdfItem> Items, IReadOnlyList<ContractPdfMilestone> Milestones, IReadOnlyList<ContractPdfAmendment> Amendments,
    DateOnly IssueDate);

public sealed record ContractPdfItem(int Sequence, string Description, decimal Quantity, decimal UnitPrice, decimal Total);

/// <param name="Due">"dd/MM/aaaa" do marco por data, ou "{n} dias após {evento}".</param>
/// <param name="Parcel">Situação da parcela: "Prevista", "Em aberto", "Paga", "Cancelada", "Recebido antes da implantação"; "Parcela gerada" sem
/// <c>finance.view</c> (como a tela); "—" sem parcela.</param>
public sealed record ContractPdfMilestone(int Sequence, string Description, decimal Percent, decimal Amount, string Due, string Parcel);

public sealed record ContractPdfAmendment(int Number, DateOnly? ApprovedOn, string Description, decimal ValueDelta, int DaysDelta);

/// <summary>
/// O aditivo em PDF (EXP-04 CA 5): contrato de origem, motivo, antes → depois de valor e prazo, marcos do acréscimo, títulos reduzidos, a
/// aprovação e as assinaturas. O orçado por natureza (custo) <b>nunca</b> entra, para ninguém.
/// </summary>
public sealed record AmendmentPdfModel(
    string ContractNumber, int Number, AmendmentStatus Status, string StatusLabel, string PartyLegalName, string? PartyDocument, string? ProjectNumber, string Description,
    decimal ValueBefore, decimal ValueAfter, DateOnly? PromisedBefore, DateOnly? PromisedAfter, int DaysDelta, IReadOnlyList<ContractPdfMilestone> Milestones,
    IReadOnlyList<AmendmentPdfReduction> Reductions, string? CreatedBy, string? ApprovedBy, DateTimeOffset? ApprovedAt, bool SelfApproved, string? RejectionComment, DateOnly IssueDate);

public sealed record AmendmentPdfReduction(string Number, decimal? CurrentAmount, decimal NewAmount);

/// <summary>Montagem pura do contrato e do aditivo em PDF (EXP-04), testada sem banco: marcas, textos de prazo e de parcela, limite de linhas.</summary>
public static class ContractPdf
{
    public const int MaxLines = 300;
    public const string OnboardingNotice = "Contrato em andamento antes do BemAqui";
    public const string ReceivedBefore = "Recebido antes da implantação";
    public const string LinesMessage = "O PDF aceita até 300 linhas (itens, marcos e aditivos). Fale com o suporte para um contrato maior.";

    /// <summary>Cancelado: "Cancelado" com o motivo; implantação: a faixa; ativo e concluído: nada (CA 4 e 6).</summary>
    public static (PdfMark Mark, string? Notice) Mark(ContractStatus status, bool onboarding, string? cancelReason) => status switch
    {
        ContractStatus.Cancelled => (PdfMark.Cancelled, string.IsNullOrWhiteSpace(cancelReason) ? null : "Motivo: " + cancelReason.Trim()),
        _ when onboarding => (PdfMark.None, OnboardingNotice),
        _ => (PdfMark.None, null),
    };

    /// <summary>Rascunho e aguardando aprovação: "Rascunho"; reprovado: "Cancelado" com o comentário; aprovado: nada (CA 5).</summary>
    public static (PdfMark Mark, string? Notice) Mark(AmendmentStatus status, string? rejection) => status switch
    {
        AmendmentStatus.Draft => (PdfMark.Draft, null),
        AmendmentStatus.PendingApproval => (PdfMark.Draft, "Aguardando aprovação"),
        AmendmentStatus.Rejected => (PdfMark.Cancelled, string.IsNullOrWhiteSpace(rejection) ? "Aditivo reprovado" : "Aditivo reprovado: " + rejection.Trim()),
        _ => (PdfMark.None, null),
    };

    public static string StatusLabel(ContractStatus status) => status switch
    {
        ContractStatus.Active => "Ativo",
        ContractStatus.Completed => "Concluído",
        _ => "Cancelado",
    };

    public static string StatusLabel(AmendmentStatus status) => status switch
    {
        AmendmentStatus.Draft => "Rascunho",
        AmendmentStatus.PendingApproval => "Aguardando aprovação",
        AmendmentStatus.Approved => "Aprovado",
        _ => "Reprovado",
    };

    public static string EventLabel(TriggerEvent? trigger) => trigger switch
    {
        TriggerEvent.Signature => "a assinatura",
        TriggerEvent.FabricationDone => "a fabricação concluída",
        TriggerEvent.Delivered => "a entrega",
        TriggerEvent.TechnicalAcceptance => "o aceite técnico",
        _ => "o evento",
    };

    /// <summary>Prazo do marco como a tela: a data, ou "{n} dias após {evento}" (0 dias: "Na/No {evento}").</summary>
    public static string Due(MilestoneTrigger trigger, TriggerEvent? triggerEvent, DateOnly? dueDate, int? daysAfter) =>
        trigger == MilestoneTrigger.Date || triggerEvent is null
            ? dueDate is { } d ? PdfTheme.Date(d) : "—"
            : daysAfter is > 0 ? $"{daysAfter} dias após {EventLabel(triggerEvent)}" : "Após " + EventLabel(triggerEvent);

    /// <summary>Situação da parcela (CA 4): pelo título com <c>finance.view</c>; sem a ação, só "Parcela gerada", como a tela.</summary>
    public static string Parcel(ContractMilestoneView milestone)
    {
        ArgumentNullException.ThrowIfNull(milestone);
        if (milestone.ReceivedBeforeOnboarding)
        {
            return ReceivedBefore;
        }

        return milestone.Receivable?.Status switch
        {
            "Planned" => "Prevista",
            "Open" or "PartiallyPaid" => "Em aberto",
            "Paid" => "Paga",
            "Cancelled" => "Cancelada",
            null when milestone.HasReceivable => "Parcela gerada",
            null => "—",
            var other => other,
        };
    }

    /// <summary>Linhas somadas (itens, marcos e aditivos) para o limite do PDF (CA 1).</summary>
    public static int Lines(ContractDetail contract, int amendments)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return contract.Snapshot.Items.Count + contract.Milestones.Count + amendments;
    }

    public static ContractPdfModel Build(
        ContractDetail contract, string legalName, string? partyAddress, ProjectDetail? project, IReadOnlyList<AmendmentSummary> amendments, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(amendments);
        var items = contract.Snapshot.Items.OrderBy(i => i.Sequence).Select(i => new ContractPdfItem(i.Sequence, i.Description, i.Quantity, i.UnitPrice, i.Total)).ToList();
        var milestones = contract.Milestones.OrderBy(m => m.Sequence)
            .Select(m => new ContractPdfMilestone(m.Sequence, m.Description, m.Percent, m.Amount, Due(m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent), Parcel(m))).ToList();
        var approved = amendments.Where(a => a.Status == AmendmentStatus.Approved).OrderBy(a => a.Number)
            .Select(a => new ContractPdfAmendment(a.Number, a.ApprovedAt is { } at ? DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(at).DateTime) : null, a.Description, a.ValueDelta, a.DaysDelta)).ToList();
        return new ContractPdfModel(
            contract.Number, contract.Status, StatusLabel(contract.Status), legalName, contract.PartyDocument is { Length: > 0 } doc ? BrazilianDocuments.Format(doc) : null, partyAddress,
            contract.ProjectNumber, contract.ProjectName, contract.QuoteNumber, contract.QuoteRevision, contract.SignedAt, contract.TotalValue, contract.CurrentValue ?? contract.TotalValue,
            project?.PromisedDate, contract.Snapshot.DeliveryDays, project?.SiteAddress, contract.IsOnboarding || contract.Snapshot.IsOnboarding, contract.CancelReason, items, milestones,
            approved, today);
    }

    public static AmendmentPdfModel Build(AmendmentDetail amendment, string legalName, string? partyDocument, string? projectNumber, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(amendment);
        return new AmendmentPdfModel(
            amendment.ContractNumber, amendment.Number, amendment.Status, StatusLabel(amendment.Status), legalName,
            partyDocument is { Length: > 0 } doc ? BrazilianDocuments.Format(doc) : null, projectNumber, amendment.Description, amendment.Preview.ValueBefore,
            amendment.Preview.ValueAfter, amendment.Preview.PromisedBefore, amendment.Preview.PromisedAfter, amendment.DaysDelta,
            amendment.Milestones.OrderBy(m => m.Sequence).Select(m => new ContractPdfMilestone(m.Sequence, m.Description, m.Percent, m.Amount, Due(m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent), "—")).ToList(),
            amendment.Reductions.Select(r => new AmendmentPdfReduction(r.Number ?? "Título", r.CurrentAmount, r.NewAmount)).ToList(),
            amendment.CreatedByName, amendment.ApprovedByName, amendment.ApprovedAt, amendment.SelfApproved, amendment.RejectionComment, today);
    }
}
