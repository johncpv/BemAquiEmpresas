using BemAqui.Sales.Contracts;

namespace BemAqui.Sales.Domain;

/// <summary>Ações do pedido (CA 6): cada uma só vale nos estados da máquina.</summary>
public enum SalesOrderAction
{
    Edit,
    Confirm,
    Charge,

    /// <summary>Cobrança cancelada ou vencida sem pagamento (volta a Aberto).</summary>
    ChargeLapsed,
    Pay,
    Cancel,

    /// <summary>"Cancelar pedido e estornar venda" (só pago, <c>sales.approve</c>).</summary>
    CancelPaid,

    /// <summary>"Reabrir pedido" do pago depois de cancelado (CA 19): reserva de novo e vira venda.</summary>
    Reopen,

    /// <summary>"Devolver o valor" do pago depois de cancelado (CA 19).</summary>
    Refund,
}

/// <summary>
/// Regras puras do pedido de venda (VND-02a): a máquina de estados (CA 6), os limites de desconto (CA 5 e 7), os prazos da reserva (CA 12) e os
/// limites do aplicativo (CA 13). Padrões dos parâmetros novos de Configurações → Parâmetros → Pedidos de venda.
/// </summary>
public static class SalesOrderRules
{
    public const string SourceType = "SalesOrder";
    public const string DocType = "PV";
    public const int MaxLines = 200;
    public const int MaxOpenOrdersPerCustomerApp = 5;

    public const decimal DefaultDiscountLimitPct = 10m;
    public const decimal DefaultAppDiscountLimitPct = 0m;
    public const int DefaultHoldHours = 48;
    public const int DefaultAppHoldHours = 2;
    public const int DefaultChargeDays = 1;
    public const int MaxAppChargeDays = 3;
    public const decimal DefaultAppMaxLineQty = 50m;
    public const decimal DefaultAppMaxOrderQty = 200m;
    public const int DefaultAppUnpaidAlertCount = 10;

    /// <summary>Estados de pedido em aberto (CA 13: até 5 por cliente e aplicativo).</summary>
    public static readonly IReadOnlySet<SalesOrderStatus> OpenStates = new HashSet<SalesOrderStatus> { SalesOrderStatus.Draft, SalesOrderStatus.Open, SalesOrderStatus.AwaitingPayment };

    /// <summary>Transições permitidas (CA 6): ação → (de, para). O destino de <see cref="SalesOrderAction.Edit"/> e <see cref="SalesOrderAction.Refund"/> é o próprio estado.</summary>
    public static SalesOrderStatus? Target(SalesOrderStatus from, SalesOrderAction action) => (from, action) switch
    {
        (SalesOrderStatus.Draft or SalesOrderStatus.Open, SalesOrderAction.Edit) => from,
        (SalesOrderStatus.Draft, SalesOrderAction.Confirm) => SalesOrderStatus.Open,
        (SalesOrderStatus.Open, SalesOrderAction.Charge) => SalesOrderStatus.AwaitingPayment,
        (SalesOrderStatus.AwaitingPayment, SalesOrderAction.ChargeLapsed) => SalesOrderStatus.Open,
        (SalesOrderStatus.Open or SalesOrderStatus.AwaitingPayment, SalesOrderAction.Pay) => SalesOrderStatus.Paid,
        (SalesOrderStatus.Draft or SalesOrderStatus.Open or SalesOrderStatus.AwaitingPayment, SalesOrderAction.Cancel) => SalesOrderStatus.Cancelled,
        (SalesOrderStatus.Paid, SalesOrderAction.CancelPaid) => SalesOrderStatus.Cancelled,
        (SalesOrderStatus.Cancelled, SalesOrderAction.Reopen) => SalesOrderStatus.Paid,
        (SalesOrderStatus.Cancelled, SalesOrderAction.Refund) => SalesOrderStatus.Cancelled,
        _ => null,
    };

    public static bool Can(SalesOrderStatus from, SalesOrderAction action) => Target(from, action) is not null;

    public static string StatusLabel(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.Draft => "em rascunho",
        SalesOrderStatus.Open => "aberto",
        SalesOrderStatus.AwaitingPayment => "aguardando pagamento",
        SalesOrderStatus.Paid => "pago",
        SalesOrderStatus.Cancelled => "cancelado",
        _ => status.ToString(),
    };

    public static string ActionLabel(SalesOrderAction action) => action switch
    {
        SalesOrderAction.Edit => "ser alterado",
        SalesOrderAction.Confirm => "ser confirmado",
        SalesOrderAction.Charge => "receber cobrança",
        SalesOrderAction.ChargeLapsed => "voltar a aberto",
        SalesOrderAction.Pay => "ser pago",
        SalesOrderAction.Cancel => "ser cancelado",
        SalesOrderAction.CancelPaid => "ser cancelado com estorno da venda",
        SalesOrderAction.Reopen => "ser reaberto",
        SalesOrderAction.Refund => "ter o valor devolvido",
        _ => action.ToString(),
    };

    /// <summary>"Este pedido está {estado} e não pode {ação}." (CA 6).</summary>
    public static string TransitionMessage(SalesOrderStatus from, SalesOrderAction action) => $"Este pedido está {StatusLabel(from)} e não pode {ActionLabel(action)}.";

    /// <summary>Desconto em % de um valor bruto (2 casas); bruto zero = 0%.</summary>
    public static decimal DiscountPct(decimal discount, decimal gross) => gross <= 0 ? 0m : Math.Round(discount / gross * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Algum desconto (de linha ou do pedido) passa do limite (CA 5 e 7)?</summary>
    public static bool DiscountAboveLimit(IEnumerable<(decimal Gross, decimal Discount)> lines, decimal orderDiscount, decimal subtotal, decimal limitPct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var list = lines.ToList();
        var total = list.Sum(l => l.Discount) + orderDiscount;
        return list.Any(l => DiscountPct(l.Discount, l.Gross) > limitPct) || DiscountPct(total, subtotal) > limitPct;
    }

    /// <summary>Data mais <paramref name="days"/> dias úteis (sábado e domingo não contam; feriado, não nesta versão).</summary>
    public static DateOnly AddBusinessDays(DateOnly from, int days)
    {
        var date = from;
        var added = 0;
        while (added < days)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                added++;
            }
        }

        return date;
    }

    /// <summary>
    /// Validade da reserva com cobrança emitida (CA 12): fim do dia útil seguinte ao vencimento (compensação), no fuso de negócio; o chamador converte.
    /// </summary>
    public static DateOnly ChargeHoldDate(DateOnly dueDate) => AddBusinessDays(dueDate, 1);

    /// <summary>Rótulo do pedido criado por aplicativo (CA 4).</summary>
    public static string AppLabel(SalesOrderOrigin origin, string? appName) =>
        origin == SalesOrderOrigin.Conecta ? "Pedido do Conecta: confirme com o cliente" : $"Pedido de {appName ?? origin.ToString()}";

    /// <summary>Quebra o desconto do pedido entre as linhas, na proporção do total de cada uma; o resto do arredondamento fica na última (a venda não tem desconto de cabeçalho).</summary>
    public static IReadOnlyList<decimal> SpreadOrderDiscount(IReadOnlyList<decimal> lineTotals, decimal orderDiscount)
    {
        ArgumentNullException.ThrowIfNull(lineTotals);
        var sum = lineTotals.Sum();
        if (orderDiscount <= 0 || sum <= 0)
        {
            return lineTotals.Select(_ => 0m).ToList();
        }

        var parts = lineTotals.Select(t => SaleRules.Money(orderDiscount * t / sum)).ToList();
        var diff = SaleRules.Money(orderDiscount - parts.Sum());
        for (var i = parts.Count - 1; i >= 0 && diff != 0; i--)
        {
            var room = lineTotals[i] - parts[i];
            var adjust = diff > 0 ? Math.Min(diff, room) : Math.Max(diff, -parts[i]);
            parts[i] += adjust;
            diff -= adjust;
        }

        return parts;
    }
}
