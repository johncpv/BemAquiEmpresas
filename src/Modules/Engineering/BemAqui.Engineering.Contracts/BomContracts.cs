using BemAqui.SharedKernel;

namespace BemAqui.Engineering.Contracts;

/// <summary>
/// Linha da BOM liberada para os outros módulos: a necessidade da OS já na unidade de estoque (4 casas). EST-06: também a quantidade
/// <b>por unidade do produto</b> na unidade da linha, a unidade e a perda %, para a capacidade de produzir converter no Estoque (item
/// sem conversão vira "não calculado", CA 5). Parâmetros opcionais: quem só usa a necessidade não muda.
/// </summary>
public sealed record BomRequirement(Guid BomLineId, Guid ItemId, decimal RequiredQty, decimal QuantityPerUnit = 0m, Guid UnitId = default, decimal ScrapPct = 0m);

/// <summary>BOM liberada vigente de uma OS para a escolha da BOM de referência (EST-06 CA 1): sem as linhas, com quantas são.</summary>
public sealed record ReleasedBomReference(Guid BomId, Guid ProductionOrderId, int Revision, DateTimeOffset ReleasedAt, int LineCount);

/// <summary>Revisão liberada vigente da OS (ENG-02 CA 18): número, quando foi liberada e as linhas com a necessidade.</summary>
public sealed record ReleasedBomInfo(Guid BomId, Guid ProductionOrderId, int Revision, DateTimeOffset ReleasedAt, IReadOnlyList<BomRequirement> Lines);

/// <summary>
/// BOM da OS para a Produção, o Estoque e a EST-06 (ENG-02 CA 18, ADR 0023 §6): a fonte da reconciliação das reservas. O consumidor
/// de <see cref="BomReleased"/> e <see cref="BomRevised"/> lê daqui a revisão vigente e ignora o evento de revisão antiga.
/// </summary>
public interface IBoms
{
    /// <summary>A revisão liberada vigente (uma por OS) ou null, se a OS ainda não tem BOM liberada.</summary>
    Task<ReleasedBomInfo?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>A OS tem BOM liberada (bloqueio das etapas depois da primeira, D12, CA 16).</summary>
    Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BOMs liberadas vigentes (uma por OS), a mais nova primeiro, até <paramref name="take"/> (1 a 500), para a BOM de referência da
    /// EST-06 (CA 1). Inclui as OS concluídas: "quantos iguais a um já feito".
    /// </summary>
    Task<IReadOnlyList<ReleasedBomReference>> ReleasedAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>Linha do <see cref="BomReleased"/>: só ids e a necessidade na unidade de estoque.</summary>
public sealed record BomReleasedLine(Guid BomLineId, Guid ItemId, decimal RequiredQty);

/// <summary>
/// Primeira BOM da OS liberada (ENG-02 CA 8 e 9), no outbox na mesma transação da liberação. <c>Lines</c> vazio: OS só de mão de obra.
/// A EST-02 reconcilia as reservas a partir de <see cref="IBoms.ReleasedForOrderAsync"/>, sem aplicar o evento como delta.
/// </summary>
public sealed record BomReleased(Guid BomId, Guid ProductionOrderId, Guid ProjectId, int Revision, IReadOnlyList<BomReleasedLine> Lines) : IIntegrationEvent;

/// <summary>
/// Linha do <see cref="BomRevised"/>: a necessidade nova e a da revisão anterior. Item que saiu na revisão vem com <c>RequiredQty</c> 0
/// e o id da linha da revisão anterior; item novo vem com <c>PreviousRequiredQty</c> 0.
/// </summary>
public sealed record BomRevisedLine(Guid BomLineId, Guid ItemId, decimal RequiredQty, decimal PreviousRequiredQty);

/// <summary>
/// Revisão da BOM liberada (ENG-02 CA 13): a anterior ficou <c>Superseded</c> na mesma transação. A EST-02 ajusta as reservas até a
/// necessidade da revisão vigente lida da fonte; evento de revisão antiga é ignorado (ADR 0023 §6).
/// </summary>
public sealed record BomRevised(Guid BomId, Guid PreviousBomId, Guid ProductionOrderId, Guid ProjectId, int Revision, IReadOnlyList<BomRevisedLine> Lines) : IIntegrationEvent;
