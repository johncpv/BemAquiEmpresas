using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Contracts;

/// <summary>Tipo da nota de saída: NF-e do equipamento e das peças (FIS-03a) ou NFS-e da instalação (FIS-06).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceType>))]
public enum InvoiceType
{
    NFe,
    NFSe,
}

/// <summary>Um tributo da nota autorizada (sem dado pessoal): o código (<c>ICMS</c>, <c>PIS</c>…), o valor e se entra no custo do projeto.</summary>
public sealed record InvoiceTaxAmount(string Tax, decimal Amount, bool IsCost);

/// <summary>
/// Nota de saída autorizada (FIS-03a CA 7): só ids, datas e valores, nunca o destinatário. O Commercial grava o imposto realizado na razão
/// do projeto (fonte <c>InvoiceTax</c>, ADR 0025 adendo) quando <paramref name="ProjectTaxApplies"/> é verdadeiro: nota de produção com
/// data a partir da data de corte <c>fiscal.project_tax_cutover_on</c>; nota de homologação nunca entra. Nenhum consumidor no Inventory: a
/// nota só referencia o romaneio, que já baixou o estoque (D4).
/// </summary>
public sealed record InvoiceAuthorized(
    Guid InvoiceId, InvoiceType Type, Guid? ProjectId, Guid? ShipmentId, Guid? PartyId, DateOnly IssuedOn, decimal TotalAmount, IReadOnlyList<InvoiceTaxAmount> Taxes,
    decimal CostTaxAmount, bool ProjectTaxApplies = false) : IIntegrationEvent;

/// <summary>
/// Nota de saída cancelada na SEFAZ (FIS-03b CA 1 e 5): só ids, datas e valores, nunca o destinatário nem o motivo. O Commercial grava o
/// reverso do imposto na razão do projeto (fonte <c>InvoiceTaxReversal</c>, valor negativo igual ao da <c>InvoiceTax</c>) quando a nota tinha
/// entrado no custo (<paramref name="ProjectTaxApplies"/>). A receita sai sozinha: o <c>IOutgoingInvoices</c> deixa de contar a nota. Nenhum
/// consumidor no Inventory: cancelar não mexe no estoque (D12); o romaneio continua expedido.
/// </summary>
public sealed record InvoiceCancelled(
    Guid InvoiceId, InvoiceType Type, Guid? ProjectId, Guid? ShipmentId, DateOnly CancelledOn, decimal CostTaxAmount, decimal TotalAmount, bool ProjectTaxApplies = false)
    : IIntegrationEvent;

/// <summary>Nota rejeitada pela SEFAZ (FIS-03a CA 8): só o sino de quem emitiu; o motivo fica na nota.</summary>
public sealed record InvoiceRejected(Guid InvoiceId, InvoiceType Type, Guid? ProjectId, int? Code) : IIntegrationEvent;

/// <summary>Nota denegada pela SEFAZ (FIS-03a CA 9): irregularidade do destinatário; o número fica registrado como denegado.</summary>
public sealed record InvoiceDenied(Guid InvoiceId, InvoiceType Type, Guid? ProjectId, Guid? PartyId) : IIntegrationEvent;

/// <summary>Situação da nota vista pelos outros módulos (nome de <c>OutgoingInvoiceStatus</c>).</summary>
public sealed record OutgoingInvoiceSummary(
    Guid Id, InvoiceType Type, string Status, int? Series, long? Number, string? AccessKey, Guid? ProjectId, Guid? ShipmentId, Guid? PartyId, DateOnly? IssuedOn,
    decimal TotalAmount, string Environment, string Link)
{
    /// <summary>"NF-e 2/42", "NFS-e 9001" ou "Rascunho" (sem número).</summary>
    public string Label => Number is { } n ? Type == InvoiceType.NFe ? $"NF-e {Series}/{n}" : $"NFS-e {n}" : "Rascunho de nota";
}

/// <summary>Receita de uma nota autorizada e não cancelada (FIN-08 pela data da nota, D4): valor total e data da nota.</summary>
public sealed record InvoicedRevenue(Guid InvoiceId, InvoiceType Type, Guid? ProjectId, Guid? PartyId, int? Series, long? Number, DateOnly IssuedOn, decimal Amount);

/// <summary>Faturado de um projeto (DSH-01a/01b "a faturar" = contratado − notas): NF-e do equipamento e NFS-e do serviço separadas.</summary>
public sealed record ProjectInvoicing(Guid ProjectId, decimal EquipmentAmount, decimal ServiceAmount, int Count)
{
    public decimal Total => EquipmentAmount + ServiceAmount;
}

/// <summary>
/// Notas de saída para os outros módulos (FIS-03a CA 12, 13 e 15, ADR 0006). Só contam como receita e faturamento as notas
/// <b>autorizadas</b> (sem as canceladas) do ambiente de <b>produção</b>: nota de homologação não tem valor fiscal.
/// </summary>
public interface IOutgoingInvoices
{
    /// <summary>Nota ativa do romaneio (rascunho, processando, contingência ou autorizada; sem as descartadas): a Produção recusa o estorno da expedição (CA 12).</summary>
    Task<OutgoingInvoiceSummary?> ActiveForShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default);

    /// <summary>Notas ativas de vários romaneios (a fila "A emitir" e a tela do romaneio), por romaneio.</summary>
    Task<IReadOnlyDictionary<Guid, OutgoingInvoiceSummary>> ActiveForShipmentsAsync(IReadOnlyCollection<Guid> shipmentIds, CancellationToken cancellationToken = default);

    /// <summary>Notas do projeto (todas as situações, sem os rascunhos descartados), da mais nova à mais antiga (CA 15).</summary>
    Task<IReadOnlyList<OutgoingInvoiceSummary>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Receita pela data da nota de <paramref name="from"/> a <paramref name="to"/>; <paramref name="projectIds"/> nulo = todas (CA 13).</summary>
    Task<IReadOnlyList<InvoicedRevenue>> RevenueAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default);

    /// <summary>Faturado por projeto, separando NF-e e NFS-e (CA 13); projeto sem nota fica fora.</summary>
    Task<IReadOnlyDictionary<Guid, ProjectInvoicing>> InvoicedByProjectAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Data de corte fiscal (<c>fiscal.project_tax_cutover_on</c>): a data da primeira nota autorizada em produção; nula antes dela.</summary>
    Task<DateOnly?> CutoverOnAsync(CancellationToken cancellationToken = default);
}
