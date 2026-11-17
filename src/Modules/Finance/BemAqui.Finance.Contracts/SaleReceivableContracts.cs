using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Chave do recebível de venda (VND-01 CA 11 e 13): dia de negócio, canal, sessão de caixa (nula no canal sem caixa), forma de pagamento,
/// provedor (adquirente ou gateway; nulo em Pix, dinheiro e vale sem adquirente) e vencimento. No fiado, o título é por venda
/// (<c>SaleId</c>) e contra o cliente (<c>PartyId</c>).
/// </summary>
public sealed record SaleReceivableKey(DateOnly BusinessDate, string Channel, Guid? CashSessionId, Guid PaymentMethodId, Guid? ProviderPartyId, DateOnly DueDate, Guid? SaleId = null)
{
    /// <summary>Texto único da chave, gravado em <c>finance.receivables.sale_group_key</c> (idempotência do reprocessamento).</summary>
    public string Text =>
        string.Join('|', BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Channel, CashSessionId?.ToString("N") ?? "-",
            PaymentMethodId.ToString("N"), ProviderPartyId?.ToString("N") ?? "-", DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            SaleId?.ToString("N") ?? "-");
}

/// <summary>
/// Valores de um recebível de venda: bruto, taxa prevista e líquido (<c>decimal(18,2)</c>). O título vale o líquido; o vencimento é a data
/// prevista do crédito (D+N da adquirente). <c>Origin</c> é <see cref="DocumentOrigin.Sale"/> ou <see cref="DocumentOrigin.CardSettlement"/>.
/// <c>PartyId</c> é a adquirente, o gateway ou o cliente do fiado; nulo quando não há terceiro (Pix e dinheiro).
/// </summary>
public sealed record SaleReceivableSummary(SaleReceivableKey Key, DocumentOrigin Origin, Guid? PartyId, string Description, decimal Gross, decimal ExpectedFee, decimal Net);

/// <summary>Recebível gravado ou atualizado; <c>Changed</c> falso quando os valores já eram esses (reprocessamento).</summary>
public sealed record SaleReceivableWrite(Guid ReceivableId, string Number, bool Created, bool Changed);

/// <summary>
/// Escrita de recebível por origem de venda (VND-01 CA 13), na transação de quem chama: o núcleo de vendas (canal sem caixa, por dia e canal) e o
/// fechamento do caixa (CX-03, por sessão) usam o mesmo contrato, só mudando a chave. "Grava o valor", não soma: reprocessar o mesmo dia ou a
/// mesma sessão não duplica. Valor zero cancela o título sem baixa; voltar a ter valor reabre.
/// </summary>
public interface ISaleReceivables
{
    Task<Result<SaleReceivableWrite>> PostAsync(SaleReceivableSummary summary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zera os recebíveis de venda do dia, canal e sessão (nula = sem caixa) que não estão em <paramref name="keep"/>: quem recompõe o dia tira os
    /// grupos que deixaram de existir (venda cancelada). Devolve quantos mudaram.
    /// </summary>
    Task<int> ZeroMissingAsync(DateOnly businessDate, string channel, Guid? cashSessionId, IReadOnlyCollection<SaleReceivableKey> keep, CancellationToken cancellationToken = default);
}
