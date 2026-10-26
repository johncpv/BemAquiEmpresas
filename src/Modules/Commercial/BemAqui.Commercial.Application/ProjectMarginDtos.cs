using System.Text.Json.Serialization;
using BemAqui.Commercial.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

/// <summary>Página do relatório "Orçado × realizado" (DSH-02 CA 9), ordenada do pior para o melhor desvio da margem por padrão.</summary>
public sealed record ProjectMarginRanking(IReadOnlyList<ProjectMarginRow> Items, int Page, int PageSize, int Total);

/// <summary>Coluna da célula do drill-down (DSH-02 CA 13 e 15).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectCostColumn>))]
public enum ProjectCostColumn
{
    Budgeted,
    Realized,
    Committed,
}

/// <summary>
/// Tipo de origem de um lançamento do drill-down (DSH-02 CA 13 e 15): baixa para OS, devolução, apontamento, correção de apontamento, título a
/// pagar, título de compra, a dedução das baixas no comprometido de material, imposto da nota, estorno de imposto, orçamento do contrato,
/// aditivo e, no consolidado, o projeto.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectCostEntryKind>))]
public enum ProjectCostEntryKind
{
    StockIssue,
    StockReturn,
    Labor,
    LaborCorrection,
    Payable,
    PurchasePayable,
    StockIssuedOffset,
    InvoiceTax,
    InvoiceTaxReversal,
    Contract,
    Amendment,
    Project,
}

/// <summary>
/// Lançamento que compõe a célula (DSH-02 CA 13): tipo, documento, data, valor com sinal, horas (só nas horas, agregadas por OS e centro de
/// trabalho) e o link, presente só quando o usuário tem a permissão do módulo dono do documento.
/// </summary>
public sealed record ProjectCostEntry(ProjectCostEntryKind Kind, string Document, DateOnly? Date, decimal Amount, decimal? Hours, string? Link);

/// <summary>
/// Drill-down de uma célula (DSH-02 CA 13 a 15): o total (igual ao da célula), os lançamentos e, no comprometido de material, a conta
/// "títulos de compra − baixas" (<c>PurchasedTotal</c> e <c>IssuedTotal</c>). Sem nome nem custo-hora individual (CA 14).
/// </summary>
public sealed record ProjectCostEntries(
    CostType CostType, ProjectCostColumn Column, decimal Total, IReadOnlyList<ProjectCostEntry> Entries, decimal? PurchasedTotal, decimal? IssuedTotal);

/// <summary>Apontamento com quem trabalhou e o custo-hora do snapshot (DSH-02 CA 14): só com <c>projects.view_cost</c> e <c>hr.view_sensitive</c>.</summary>
public sealed record ProjectLaborWorkerEntry(string WorkerName, string OrderNumber, string? WorkCenterName, DateOnly Date, decimal Hours, decimal? HourlyCost, decimal Amount, string? Link);

/// <summary>Horas do projeto por funcionário (DSH-02 CA 14): o total das horas da razão e os apontamentos.</summary>
public sealed record ProjectLaborWorkers(decimal Total, IReadOnlyList<ProjectLaborWorkerEntry> Entries);
