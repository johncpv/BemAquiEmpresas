using System.Globalization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Consumidor do Financeiro para a venda de canal sem caixa (VND-01 CA 11). Não soma o evento: <b>recompõe o dia e o canal a partir da fonte</b>
/// (os pagamentos capturados das vendas concluídas, por <see cref="ISaleLookup"/>), agrupa por forma, provedor e vencimento e grava o valor de
/// cada grupo pelo <see cref="ISaleReceivables"/>. Assim reprocessar o dia não duplica, a venda cancelada sai do grupo e a ordem dos eventos não
/// importa (ADR 0023 §6). Uma trava por dia e canal (<c>pg_advisory_xact_lock</c>) põe em fila dois consumidores do mesmo grupo. Venda com
/// sessão de caixa fica para o fechamento (CX-03). Taxa e prazo vêm da tabela da adquirente (PAG-01) vigente no dia da venda.
/// </summary>
public sealed class SaleReceivablesConsumer(IFinanceDb db, ISaleLookup sales, IPaymentMethods methods, ISaleReceivables receivables)
    : IIntegrationEventConsumer<SaleCompleted>, IIntegrationEventConsumer<SaleCancelled>
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public Task HandleAsync(SaleCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.CashSessionId is null ? RecomposeAsync(integrationEvent.BusinessDate, integrationEvent.Channel, cancellationToken) : Task.CompletedTask;
    }

    public Task HandleAsync(SaleCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.CashSessionId is null ? RecomposeAsync(integrationEvent.BusinessDate, integrationEvent.Channel, cancellationToken) : Task.CompletedTask;
    }

    /// <summary>Recompõe os recebíveis de um dia e canal sem caixa; devolve os grupos gravados.</summary>
    public async Task<IReadOnlyList<SaleReceivableSummary>> RecomposeAsync(DateOnly businessDate, SaleChannel channel, CancellationToken cancellationToken)
    {
        var channelCode = channel.ToString();
        var lockKey = "finance.sale-receivables:" + businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" + channelCode;
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))) AS l").ToListAsync(cancellationToken);

        var facts = await sales.CapturedPaymentsAsync(businessDate, channel, null, cancellationToken);
        var summaries = await GroupAsync(businessDate, channelCode, facts, cancellationToken);
        foreach (var summary in summaries)
        {
            var written = await receivables.PostAsync(summary, cancellationToken);
            if (written.IsFailure)
            {
                throw new InvalidOperationException("Recebível de venda recusado: " + written.Error!.Message);
            }
        }

        await receivables.ZeroMissingAsync(businessDate, channelCode, null, summaries.Select(s => s.Key).ToList(), cancellationToken);
        return summaries;
    }

    /// <summary>
    /// Agrupa os pagamentos por forma, provedor (o do pagamento ou a adquirente da forma) e vencimento (dia + D+N da taxa vigente). Taxa por
    /// pagamento, arredondada em 2 casas; líquido = bruto − taxa. Fiado é por venda, contra o cliente.
    /// </summary>
    private async Task<List<SaleReceivableSummary>> GroupAsync(DateOnly businessDate, string channel, IReadOnlyList<SalePaymentFact> facts, CancellationToken ct)
    {
        if (facts.Count == 0)
        {
            return [];
        }

        // Inclusive forma desativada ou excluída depois da venda: a venda já aconteceu com ela.
        var methodIds = facts.Select(f => f.PaymentMethodId).Distinct().ToList();
        var known = await db.PaymentMethods.IgnoreQueryFilters().AsNoTracking().Where(m => methodIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type, m.AcquirerPartyId }).ToDictionaryAsync(m => m.Id, ct);
        var lines = new List<(SaleReceivableKey Key, DocumentOrigin Origin, Guid? Party, string Description, decimal Gross, decimal Fee)>();
        foreach (var fact in facts)
        {
            if (!known.TryGetValue(fact.PaymentMethodId, out var method))
            {
                throw new InvalidOperationException("Forma de pagamento " + fact.PaymentMethodId + " da venda não existe no Financeiro.");
            }

            var provider = fact.AcquirerPartyId ?? method.AcquirerPartyId;
            var brand = Enum.TryParse<CardBrand>(fact.CardBrand, ignoreCase: true, out var parsed) ? parsed : CardBrand.Any;
            var terms = await methods.FeeAsync(method.Id, brand, fact.Installments, businessDate, ct);
            var fee = terms is null ? 0m : Math.Round(fact.Amount * terms.FeePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var due = businessDate.AddDays(terms?.SettlementDays ?? 0);
            var onAccount = method.Type == PaymentMethodType.OnAccount;
            var card = method.Type is PaymentMethodType.Debit or PaymentMethodType.Credit or PaymentMethodType.CreditInstallments;
            var key = new SaleReceivableKey(businessDate, channel, null, method.Id, onAccount ? fact.CustomerId : provider, due, onAccount ? fact.SaleId : null);
            var description = $"Vendas {ChannelLabel(channel)} de {businessDate.ToString("dd/MM/yyyy", PtBr)} — {method.Name}" + (onAccount ? " (fiado)" : string.Empty);
            lines.Add((key, card ? DocumentOrigin.CardSettlement : DocumentOrigin.Sale, onAccount ? fact.CustomerId : provider, description, fact.Amount, fee));
        }

        return lines.GroupBy(l => l.Key)
            .Select(g =>
            {
                var first = g.First();
                var gross = Math.Round(g.Sum(l => l.Gross), 2, MidpointRounding.AwayFromZero);
                var fee = Math.Round(g.Sum(l => l.Fee), 2, MidpointRounding.AwayFromZero);
                return new SaleReceivableSummary(g.Key, first.Origin, first.Party, first.Description, gross, fee, gross - fee);
            })
            .OrderBy(s => s.Key.Text, StringComparer.Ordinal)
            .ToList();
    }

    public static string ChannelLabel(string channel) => channel switch
    {
        nameof(SaleChannel.Pos) => "do balcão",
        nameof(SaleChannel.Online) => "da loja virtual",
        nameof(SaleChannel.Delivery) => "do delivery",
        nameof(SaleChannel.Table) => "das mesas",
        nameof(SaleChannel.ServiceOrder) => "das OS",
        _ => channel,
    };
}
