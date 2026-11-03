using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>
/// O que a OS de instalação impressa mostra (EXP-04 CA 7): dados da OS, cliente, obra, instalador, previsão, etapas e o checklist da entrega
/// técnica. <b>Não existe</b> o valor combinado com o instalador aqui (é dado do CP), para nenhum usuário (conferido por teste no record).
/// </summary>
public sealed record InstallationPdfModel(
    string Number, ProductionOrderStatus Status, string StatusLabel, string ProjectNumber, string ProjectName, string CustomerName, string? CustomerDocument, string? SiteAddress,
    string InstallerName, DateOnly PlannedStart, DateOnly PlannedEnd, DateOnly? StartedOn, DateOnly? AcceptedOn, string? AcceptedByName, string? CancelReason,
    IReadOnlyList<string> Letters, int CurrentIndex, IReadOnlyList<InstallationPdfStep> Steps, IReadOnlyList<InstallationPdfCheck> Checklist, DateOnly IssueDate);

public sealed record InstallationPdfStep(int Sequence, string Name, string StatusLabel, DateOnly? CompletedOn);

/// <param name="ResultLabel">"Conforme", "Não conforme" ou "Não se aplica"; null deixa a caixa em branco.</param>
public sealed record InstallationPdfCheck(string Text, string? ResultLabel);

/// <summary>
/// Termo de aceite técnico (EXP-04 CA 8): identificação do cliente, do projeto e da obra, o equipamento (do romaneio expedido, com as séries;
/// sem romaneio, os itens da OS com "Série: ____"), o checklist com "Conforme / Não conforme / Observação" e as assinaturas. Em branco até o
/// aceite; com a OS concluída, a data e quem aceitou. Sem valor nem custo.
/// </summary>
public sealed record AcceptanceTermPdfModel(
    string Number, ProductionOrderStatus Status, string ProjectNumber, string ProjectName, string CustomerName, string? CustomerDocument, string? SiteAddress, string InstallerName,
    bool FromShipment, IReadOnlyList<AcceptanceTermEquipment> Equipment, IReadOnlyList<AcceptanceTermCheck> Checklist, DateOnly? AcceptedOn, string? AcceptedByName,
    DateOnly IssueDate);

/// <param name="Serials">Séries expedidas; vazio sem romaneio (o PDF deixa "Série: ____").</param>
public sealed record AcceptanceTermEquipment(string Description, decimal Quantity, string? Unit, IReadOnlyList<string> Serials);

/// <param name="Result">Preenchido só no termo da OS concluída.</param>
public sealed record AcceptanceTermCheck(string Text, ChecklistResult? Result);

/// <summary>Regras puras dos PDFs da OS de instalação (EXP-04 CA 7 e 8), testadas sem banco.</summary>
public static class InstallationPdf
{
    public const int MaxLines = 300;

    public const string Declaration = "Declaro que recebi o equipamento instalado e em funcionamento, conforme o checklist acima.";

    public static string StatusLabel(ProductionOrderStatus status) => status switch
    {
        ProductionOrderStatus.Open => "Aberta",
        ProductionOrderStatus.InProgress => "Em instalação",
        ProductionOrderStatus.Completed => "Concluída",
        _ => "Cancelada",
    };

    public static string? ResultLabel(ChecklistResult? result) => result switch
    {
        ChecklistResult.Done => "Conforme",
        ChecklistResult.NonConforming => "Não conforme",
        ChecklistResult.NotApplicable => "Não se aplica",
        _ => null,
    };

    /// <summary>OS cancelada: "Cancelado" com o motivo; demais sem marca (CA 7).</summary>
    public static (PdfMark Mark, string? Notice) Mark(ProductionOrderStatus status, string? cancelReason) => status == ProductionOrderStatus.Cancelled
        ? (PdfMark.Cancelled, string.IsNullOrWhiteSpace(cancelReason) ? null : "Motivo: " + cancelReason.Trim())
        : (PdfMark.None, null);

    /// <summary>Termo: com o aceite registrado, a faixa "Aceite registrado em {data}"; em branco sem faixa (CA 8).</summary>
    public static string? TermNotice(ProductionOrderStatus status, DateOnly? acceptedOn) =>
        status == ProductionOrderStatus.Completed && acceptedOn is { } day ? "Aceite registrado em " + PdfTheme.Date(day) : null;

    /// <summary>Letra de cada etapa para o indicador: a inicial do nome.</summary>
    public static IReadOnlyList<string> Letters(IEnumerable<string> names) =>
        names.Select(n => string.IsNullOrWhiteSpace(n) ? "·" : n.Trim()[..1].ToUpper(PdfTheme.PtBr)).ToList();

    /// <summary>Etapa atual: a primeira não concluída; com todas concluídas, o total (tudo cheio).</summary>
    public static int CurrentIndex(IReadOnlyList<bool> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var index = -1;
        for (var i = 0; i < done.Count; i++)
        {
            if (!done[i])
            {
                index = i;
                break;
            }
        }

        return index < 0 ? done.Count : index;
    }
}
