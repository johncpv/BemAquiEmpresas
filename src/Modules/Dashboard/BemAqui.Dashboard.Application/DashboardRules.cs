using System.Globalization;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Regras do cabeçalho do painel sem banco (DSH-01a CA 2 e 10): saudação pelo horário, frase-resumo dos cards visíveis e a
/// faixa "Atenção" com até 3 alertas, na ordem caixa → decisões → maior vencido. O painel nunca recalcula número: só lê os
/// cards já montados pelos contratos dos donos.
/// </summary>
public static class DashboardRules
{
    public const int MaxAlerts = 3;

    /// <summary>Horizonte do alerta de caixa negativo (CA 10).</summary>
    public const int NegativeCashHorizonDays = 90;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"Bom dia" até 11h59, "Boa tarde" até 17h59 e "Boa noite" depois (hora de America/Sao_Paulo).</summary>
    public static string Greeting(int hour) => hour switch
    {
        < 12 => "Bom dia",
        < 18 => "Boa tarde",
        _ => "Boa noite",
    };

    /// <summary>Primeiro nome do nome de exibição; sem nome, vazio (a saudação fica só "Bom dia").</summary>
    public static string FirstName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    /// <summary>% atingido com uma casa; nulo sem meta ou com meta zero.</summary>
    public static decimal? Pct(decimal? amount, decimal? target) =>
        amount is { } a && target is { } t && t > 0 ? Math.Round(a * 100m / t, 1, MidpointRounding.AwayFromZero) : null;

    public static string Money(decimal value) => "R$ " + value.ToString("N2", PtBr);

    public static string Day(DateOnly date) => date.ToString("dd/MM", PtBr);

    /// <summary>
    /// Frase-resumo (CA 2) só com os cards visíveis, ex.: "Caixa positivo nas próximas 12 semanas; 3 decisões esperam você.";
    /// nula quando nenhum card que entra na frase está visível.
    /// </summary>
    public static string? Summary(CashForecastCard? cash, BalancesCard? balances, DecisionsCard? decisions, OverdueCard? overdue)
    {
        var parts = new List<string>();
        if (cash is { Available: true })
        {
            parts.Add(cash.FirstAdjustedNegative is { } negative
                ? $"caixa ajustado fica negativo em {Day(negative.Date)}"
                : $"caixa positivo nas próximas {cash.Weeks.Count} semanas");
        }
        else if (balances is not null)
        {
            parts.Add($"{Money(balances.Consolidated)} em contas");
        }

        if (decisions is { Available: true })
        {
            parts.Add(decisions.Total switch
            {
                0 => "nenhuma decisão espera você",
                1 => "1 decisão espera você",
                var n => $"{n} decisões esperam você",
            });
        }

        if (overdue is not null)
        {
            parts.Add(overdue.Total > 0 ? $"{Money(overdue.Total)} a receber vencido" : "nada vencido a receber");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var text = string.Join("; ", parts) + ".";
        return char.ToUpper(text[0], PtBr) + text[1..];
    }

    /// <summary>
    /// Faixa "Atenção" (CA 10): caixa ajustado negativo nos próximos 90 dias (crítico), decisões aguardando (alerta, com total e
    /// o primeiro vencimento) e o maior título vencido (alerta, com dias e o atraso médio do cliente). Cada alerta leva à ação
    /// (R-PRP-5); sem alerta, a lista vem vazia e a faixa não aparece. A DSH-01b (CA 4) põe o projeto que mais vai atrasar em relação ao
    /// prometido (crítico, com o link para o cronograma) logo depois do caixa: caixa → projeto → decisões → maior vencido.
    /// </summary>
    public static IReadOnlyList<AttentionAlert> Attention(DateOnly today, CashForecastCard? cash, DecisionsCard? decisions, OverdueCard? overdue, DeliveriesCard? deliveries = null)
    {
        var alerts = new List<AttentionAlert>();
        if (cash is { Available: true, FirstAdjustedNegative: { } negative } && negative.Date <= today.AddDays(NegativeCashHorizonDays))
        {
            alerts.Add(new AttentionAlert("cash_negative", "crit", $"O caixa ajustado fica negativo em {Day(negative.Date)}",
                $"Saldo previsto de {Money(negative.Balance)} nesse dia, contando o atraso histórico de cada cliente. Veja o que vence e o que dá para adiar.",
                "/app/financeiro/fluxo-de-caixa?cenario=ajustado"));
        }

        if (deliveries?.MostDelayed is { } late)
        {
            var name = string.IsNullOrWhiteSpace(late.Name) ? late.Number : late.Name;
            var detail = late.ForecastDate is { } f && late.PromisedDate is { } p
                ? $"Previsão {Day(f)}, prometido ao cliente {Day(p)}."
                : "A previsão passou da data prometida ao cliente.";
            alerts.Add(new AttentionAlert("project_late", "crit", $"{name} vai atrasar {late.DelayDays} {(late.DelayDays == 1 ? "dia" : "dias")}", detail,
                $"/app/projetos/{late.ProjectId}?aba=cronograma"));
        }

        if (decisions is { Available: true, Total: > 0 })
        {
            var title = decisions.Total == 1 ? "1 decisão aguarda você" : $"{decisions.Total} decisões aguardam você";
            var detail = decisions.FirstDue is { } due
                ? $"{Money(decisions.TotalAmount)} no total; a primeira vence em {Day(due)}."
                : $"{Money(decisions.TotalAmount)} no total.";
            alerts.Add(new AttentionAlert("decisions", "warn", title, detail, decisions.Items.Count == 1 ? decisions.Items[0].Url : "/app"));
        }

        if (overdue?.Largest is { } largest)
        {
            var history = largest.CustomerAvgDelayDays is { } avg
                ? $"Atraso médio do cliente: {avg.ToString("0.#", PtBr)} {(avg == 1 ? "dia" : "dias")}."
                : "Cliente sem histórico de pagamento.";
            alerts.Add(new AttentionAlert("overdue", "warn", $"{(string.IsNullOrEmpty(largest.PartyName) ? "Cliente" : largest.PartyName)} com título vencido",
                $"{Money(largest.Balance)} vencidos há {largest.DaysLate} {(largest.DaysLate == 1 ? "dia" : "dias")}. {history}",
                "/app/financeiro/receber/" + largest.ReceivableId));
        }

        return alerts.Take(MaxAlerts).ToList();
    }
}
