using System.Globalization;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

/// <summary>
/// Um pagamento capturado da sessão de caixa, já com a forma (tipo e nome), o provedor, a bandeira e a taxa e o prazo vigentes na data da venda
/// (PAG-01). Entrada das regras puras de <see cref="CashSessionReceivables"/>.
/// </summary>
public sealed record CashSessionPayment(
    DateOnly BusinessDate, Guid SaleId, Guid PaymentMethodId, PaymentMethodType Type, string MethodName, Guid? ProviderPartyId, CardBrand? Brand, int Installments,
    decimal Amount, decimal FeePercent, int SettlementDays, Guid? CustomerId);

/// <summary>
/// Regras puras do fechamento de caixa no Financeiro (CX-03 CA 2 a 4): os resumos por forma que viram títulos da sessão.
/// <list type="bullet">
/// <item>Cartão: um título por adquirente, bandeira, forma e vencimento (dia da venda + D+N), contra a adquirente, com bruto, taxa prevista e líquido.</item>
/// <item>Crédito parcelado: um título por parcela; a parcela i vence em D+N + 30 × (i − 1); bruto e taxa divididos em centavos, com a sobra na última.</item>
/// <item>Pix e as outras formas: um título por forma, provedor e vencimento; no Pix, o "Não localizado" sai do título e vira um título de quebra.</item>
/// <item>Fiado: um por venda, contra o cliente (como na VND-01).</item>
/// </list>
/// A taxa é calculada e arredondada por pagamento (2 casas) e dividida entre as parcelas; o líquido de cada título é o bruto menos a taxa.
/// </summary>
public static class CashSessionReceivables
{
    public const string Channel = "Pos";

    /// <summary>Intervalo entre as parcelas do crédito parcelado (a tabela da PAG-01 dá o D+N da primeira).</summary>
    public const int InstallmentIntervalDays = 30;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static bool IsCard(PaymentMethodType type) => type is PaymentMethodType.Debit or PaymentMethodType.Credit or PaymentMethodType.CreditInstallments;

    /// <summary>Os títulos da sessão. <paramref name="pixNotFound"/>: o Pix "Não localizado" por forma (CX-02); <paramref name="closingDate"/>: o dia do caixa.</summary>
    public static IReadOnlyList<SaleReceivableSummary> Build(Guid sessionId, IReadOnlyList<CashSessionPayment> payments, IReadOnlyDictionary<Guid, decimal> pixNotFound, DateOnly closingDate)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(pixNotFound);
        var lines = new List<(SaleReceivableKey Key, DocumentOrigin Origin, Guid? Party, string Description, decimal Gross, decimal Fee)>();
        foreach (var p in payments.Where(p => p.Amount > 0))
        {
            var card = IsCard(p.Type);
            var onAccount = p.Type == PaymentMethodType.OnAccount;
            var brand = card ? (p.Brand ?? CardBrand.Any).ToString() : null;
            var parcels = p.Type == PaymentMethodType.CreditInstallments ? Math.Max(1, p.Installments) : 1;
            var fee = Math.Round(p.Amount * p.FeePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var grossParts = Split(p.Amount, parcels);
            var feeParts = Split(fee, parcels);
            for (var i = 0; i < parcels; i++)
            {
                var due = p.BusinessDate.AddDays(p.SettlementDays + (InstallmentIntervalDays * i));
                var key = new SaleReceivableKey(p.BusinessDate, Channel, sessionId, p.PaymentMethodId, onAccount ? p.CustomerId : p.ProviderPartyId, due, onAccount ? p.SaleId : null, brand);
                var label = p.MethodName + (brand is null or nameof(CardBrand.Any) ? string.Empty : " " + brand) + (onAccount ? " (fiado)" : string.Empty);
                var description = $"Vendas do caixa de {p.BusinessDate.ToString("dd/MM/yyyy", PtBr)} — {label}" + (parcels > 1 ? $", crédito em {due.ToString("dd/MM/yyyy", PtBr)}" : string.Empty);
                lines.Add((key, card ? DocumentOrigin.CardSettlement : DocumentOrigin.Sale, onAccount ? p.CustomerId : p.ProviderPartyId, description, grossParts[i], feeParts[i]));
            }
        }

        var summaries = lines.GroupBy(l => l.Key)
            .Select(g =>
            {
                var first = g.First();
                var gross = g.Sum(l => l.Gross);
                var fee = Math.Min(gross, g.Sum(l => l.Fee));
                return new SaleReceivableSummary(g.Key, first.Origin, first.Party, first.Description, gross, fee, gross - fee);
            })
            .ToList();

        foreach (var (methodId, notFound) in pixNotFound.Where(n => n.Value > 0))
        {
            var remaining = notFound;
            var removed = 0m;
            var groups = summaries.Where(s => s.Key.PaymentMethodId == methodId && !s.Key.Break && s.Origin == DocumentOrigin.Sale).OrderByDescending(s => s.Key.BusinessDate).ToList();
            foreach (var group in groups)
            {
                var cut = Math.Min(remaining, group.Gross);
                if (cut <= 0)
                {
                    break;
                }

                var gross = group.Gross - cut;
                var fee = Math.Min(gross, group.ExpectedFee);
                summaries[summaries.IndexOf(group)] = group with { Gross = gross, ExpectedFee = fee, Net = gross - fee };
                remaining -= cut;
                removed += cut;
            }

            if (removed > 0)
            {
                var name = groups[0].Description.Split(" — ", 2) is { Length: 2 } parts ? parts[1] : "Pix";
                summaries.Add(new SaleReceivableSummary(new SaleReceivableKey(closingDate, Channel, sessionId, methodId, null, closingDate, Break: true), DocumentOrigin.Sale, null,
                    $"Pix não localizado no caixa de {closingDate.ToString("dd/MM/yyyy", PtBr)} — {name}", removed, 0m, removed));
            }
        }

        return summaries.OrderBy(s => s.Key.Text, StringComparer.Ordinal).ToList();
    }

    /// <summary>Divide o valor em <paramref name="parts"/> partes em centavos; a diferença de centavos fica na última.</summary>
    public static IReadOnlyList<decimal> Split(decimal amount, int parts)
    {
        if (parts <= 1)
        {
            return [amount];
        }

        var each = Math.Floor(amount * 100m / parts) / 100m;
        var result = Enumerable.Repeat(each, parts - 1).ToList();
        result.Add(amount - (each * (parts - 1)));
        return result;
    }
}
