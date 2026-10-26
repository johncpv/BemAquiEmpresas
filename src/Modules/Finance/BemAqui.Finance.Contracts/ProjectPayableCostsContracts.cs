using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Como um título a pagar com projeto entra no custo do projeto (DSH-02 CA 2 a 4; ADR 0025 §1 e adendo D8): realizado (<c>Confirmed</c>),
/// comprometido previsto (<c>Planned</c>), compra de item estocável (<c>StockPurchase</c>, comprometido por saldo até a baixa) ou guia de
/// imposto (<c>TaxGuide</c>, nunca é custo do projeto).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayableCostBucket>))]
public enum PayableCostBucket
{
    Confirmed,
    Planned,
    StockPurchase,
    TaxGuide,
}

/// <summary>Regra única de classificação do título no custo do projeto (Finance e Commercial usam a mesma).</summary>
public static class PayableCostRules
{
    /// <summary>
    /// Natureza Impostos é guia (DARF, GARE, DAS): da empresa, vai para o DRE (FIN-08). Origem <c>Purchase</c> com natureza Material é
    /// compra de item estocável (CMP-03a CA 13), confirmada ou não. <c>Opening</c> entra pelo saldo da data de corte como realizado. O resto
    /// é previsto (<c>Planned</c>) ou confirmado. Cancelado não entra (null). Título sem natureza vai para Outros.
    /// </summary>
    public static PayableCostBucket? Bucket(DocumentOrigin origin, CostType? costType, PayableStatus status)
    {
        if (status == PayableStatus.Cancelled)
        {
            return null;
        }

        if (costType == CostType.Tax)
        {
            return PayableCostBucket.TaxGuide;
        }

        if (origin is DocumentOrigin.Purchase or DocumentOrigin.GoodsReceipt && costType == CostType.Material)
        {
            return PayableCostBucket.StockPurchase;
        }

        return origin == DocumentOrigin.Opening || status != PayableStatus.Planned ? PayableCostBucket.Confirmed : PayableCostBucket.Planned;
    }
}

/// <summary>Soma dos títulos a pagar de um projeto numa natureza, por classe do custo (DSH-02 CA 2 a 4).</summary>
public sealed record ProjectPayableCostTotals(Guid ProjectId, CostType CostType, decimal Confirmed, decimal Planned, decimal StockPurchase, decimal TaxGuide);

/// <summary>Título a pagar do projeto no drill-down do custo (DSH-02 CA 13): número, fornecedor, origem, datas, valor e classe.</summary>
public sealed record ProjectPayableCostEntry(
    Guid Id, string Number, Guid PartyId, DocumentOrigin Origin, Guid? OriginId, PayableStatus Status, DateOnly IssueDate, DateOnly? DueDate, decimal Amount, CostType CostType,
    PayableCostBucket Bucket);

/// <summary>
/// Títulos a pagar como custo do projeto (DSH-02, regra de negócio "Fontes por contrato"): somas em lote, agregadas no banco (uma consulta
/// para todos os projetos), e os títulos de um projeto para o drill-down. O chamador confere <c>projects.view_cost</c>. Contrato próprio, ao
/// lado do <see cref="IPayables"/>, para não obrigar os fakes de <see cref="IPayables"/> das outras trilhas a mudar.
/// </summary>
public interface IProjectPayableCosts
{
    /// <summary>Somas por projeto e natureza (título sem natureza em Outros), sem os cancelados.</summary>
    Task<IReadOnlyList<ProjectPayableCostTotals>> CostSummaryByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Títulos não cancelados do projeto na natureza, com a classe, do mais antigo ao mais novo.</summary>
    Task<IReadOnlyList<ProjectPayableCostEntry>> CostEntriesAsync(Guid projectId, CostType costType, CancellationToken cancellationToken = default);
}
