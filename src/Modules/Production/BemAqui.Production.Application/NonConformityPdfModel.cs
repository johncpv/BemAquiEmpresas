using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>
/// O que a NC impressa mostra (EXP-04 CA 14): dados da NC, da OS e da inspeção, textos, o checklist da tentativa reprovada, os anexos só
/// listados e o custo estimado, que só vem preenchido com <c>projects.view_cost</c> (<see cref="CostVisible"/>; sem a ação, nulo e a linha
/// não é desenhada).
/// </summary>
public sealed record NonConformityPdfModel(
    string Number, NonConformityStatus Status, string StatusLabel, string OrderNumber, string ProjectNumber, string ProjectName, string PartyName, string StepName, int Attempt,
    int AgeDays, string ResponsibleName, string ReturnToStepName, string Description, string? Cause, string? Action, string? Result, string? OpenedByName, DateTimeOffset OpenedAt,
    string? ClosedByName, DateTimeOffset? ClosedAt, IReadOnlyList<NonConformityPdfCheck> Checklist, IReadOnlyList<NonConformityPdfFile> Files, decimal? EstimatedCost,
    bool CostVisible, DateOnly IssueDate);

public sealed record NonConformityPdfCheck(string Text, string? ResultLabel);

public sealed record NonConformityPdfFile(string FileName, DateTimeOffset CreatedAt);

/// <summary>Regras puras do PDF da NC (EXP-04 CA 14).</summary>
public static class NonConformityPdf
{
    public const int MaxLines = 300;
    public const string OpenNotice = "Em aberto";

    public static string StatusLabel(NonConformityStatus status) => status switch
    {
        NonConformityStatus.Open => "Aberta",
        NonConformityStatus.InAction => "Em ação",
        _ => "Encerrada",
    };

    /// <summary>Aberta ou em ação: a faixa "Em aberto"; encerrada sem marca.</summary>
    public static string? Notice(NonConformityStatus status) => status == NonConformityStatus.Closed ? null : OpenNotice;

    /// <summary>O custo só entra no modelo com a ação; sem ela, nulo (mesmo que o detalhe traga algo por engano).</summary>
    public static decimal? Cost(decimal? cost, bool visible) => visible ? cost : null;

    public static PdfDocumentHeader Header(NonConformityPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Não conformidade", model.Number, model.IssueDate, Notice: Notice(model.Status));
    }
}
