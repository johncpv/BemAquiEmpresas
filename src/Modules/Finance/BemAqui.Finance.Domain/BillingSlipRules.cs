namespace BemAqui.Finance.Domain;

/// <summary>Parâmetros de Configurações → Cobrança usados na emissão (BNK-01b CA 1 a 3).</summary>
public sealed record BillingDefaults(decimal FinePct, decimal InterestMonthPct, decimal DefaultDiscountPct, bool AutoEmail, int DailyEmailLimit, OverdueFineFrom OverdueFineFrom);

/// <summary>Limites da D8 (FIN-01 CA 13) usados no boleto: desconto acima do % do saldo ou do valor exige <c>finance.approve</c>.</summary>
public sealed record DiscountLimits(decimal Pct, decimal Amount);

/// <summary>O que vai ao banco: valor (saldo), vencimento, multa, juros, desconto até uma data e a mensagem.</summary>
public sealed record BillingSlipTerms(decimal Amount, DateOnly DueDate, decimal FinePct, decimal InterestMonthPct, decimal DiscountAmount, DateOnly? DiscountUntil, string? Message);

/// <summary>Regras puras da emissão do boleto (BNK-01b): elegibilidade, vencimento, limites e motivos.</summary>
public static class BillingSlipRules
{
    /// <summary>Teto legal (CDC) da multa e dos juros de mora que a tela de Cobrança aceita (CA 3).</summary>
    public const decimal MaxFinePct = 2m;

    public const decimal MaxInterestMonthPct = 1m;

    public const int MinDailyEmailLimit = 1;

    public const int MaxDailyEmailLimit = 2000;

    /// <summary>Lote de até 50 títulos (R-PRP-6, CA 19).</summary>
    public const int MaxBatch = 50;

    /// <summary>Título que aceita boleto (CA 4): em aberto ou parcial, com saldo.</summary>
    public static bool Eligible(Receivable receivable)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        return receivable.IsOpen && receivable.Balance > 0 && receivable.DueDate is not null;
    }

    /// <summary>Valor do boleto = saldo do título na emissão, nunca maior (regra da story).</summary>
    public static decimal AmountFor(Receivable receivable)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        return Math.Round(receivable.Balance, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Vencimento sugerido (CA 4 e 15, propositivo): o do título; vencido, o próximo dia útil depois de hoje (sábado e domingo pulados;
    /// feriado segue a regra do banco).
    /// </summary>
    public static DateOnly SuggestedDueDate(DateOnly? receivableDueDate, DateOnly today)
    {
        if (receivableDueDate is { } due && due >= today)
        {
            return due;
        }

        var next = today.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }

        return next;
    }

    /// <summary>De onde contam multa e juros (CA 15): no vencido, do vencimento original (padrão) ou do novo, pela configuração.</summary>
    public static DateOnly FineFrom(DateOnly receivableDueDate, DateOnly slipDueDate, OverdueFineFrom rule) =>
        rule == OverdueFineFrom.OriginalDueDate && receivableDueDate < slipDueDate ? receivableDueDate : slipDueDate;

    /// <summary>Desconto padrão (CA 4): o % de Configurações sobre o valor do boleto, até o vencimento.</summary>
    public static decimal DefaultDiscount(decimal amount, decimal pct) => Math.Round(amount * pct / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Acima dos limites (CA 5, D8 e D13): desconto acima de % do saldo ou do valor, ou multa e juros acima dos de Configurações →
    /// Cobrança. Só quem tem <c>finance.approve</c> confirma.
    /// </summary>
    public static bool RequiresApproval(BillingSlipTerms terms, DiscountLimits limits, BillingDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(defaults);
        var discountPct = terms.Amount > 0 ? terms.DiscountAmount / terms.Amount * 100m : 100m;
        return (terms.DiscountAmount > 0 && (discountPct > limits.Pct || terms.DiscountAmount > limits.Amount))
            || terms.FinePct > defaults.FinePct || terms.InterestMonthPct > defaults.InterestMonthPct;
    }

    /// <summary>Mensagem ao pagador: até 3 linhas de até 78 caracteres, sem linha vazia no meio.</summary>
    public static IReadOnlyList<string> MessageLines(string? message) =>
        string.IsNullOrWhiteSpace(message)
            ? []
            : message.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static bool ValidMessage(string? message)
    {
        var lines = MessageLines(message);
        return lines.Count <= BillingSlip.MaxMessageLines && lines.All(l => l.Length <= BillingSlip.MaxMessageLineLength);
    }

    /// <summary>Motivo do cancelamento em PT-BR (lista da tela e aviso do automático, CA 16 e 17).</summary>
    public static string ReasonText(BillingSlipCancelReason reason) => reason switch
    {
        BillingSlipCancelReason.PaidOtherMeans => "Pagamento por outro meio",
        BillingSlipCancelReason.CustomerRequest => "Pedido do cliente",
        BillingSlipCancelReason.IssueError => "Erro na emissão",
        BillingSlipCancelReason.Replacement => "Substituição por outro boleto",
        BillingSlipCancelReason.ReceivableCancelled => "o título foi cancelado",
        BillingSlipCancelReason.AmountChanged => "o valor do título mudou",
        BillingSlipCancelReason.DueDateChanged => "o vencimento do título mudou",
        BillingSlipCancelReason.SettledOtherMeans => "o título recebeu baixa por outro meio",
        _ => reason.ToString(),
    };

    /// <summary>Cancelamento que o sistema pede sozinho porque o título mudou (CA 17).</summary>
    public static bool IsAutomatic(BillingSlipCancelReason reason) =>
        reason is BillingSlipCancelReason.ReceivableCancelled or BillingSlipCancelReason.AmountChanged or BillingSlipCancelReason.DueDateChanged or BillingSlipCancelReason.SettledOtherMeans;

    /// <summary>Motivo aceito no cancelamento manual (CA 16).</summary>
    public static bool IsManual(BillingSlipCancelReason reason) => !IsAutomatic(reason);

    /// <summary>
    /// Código do motivo enviado ao Inter (<c>motivoCancelamento</c>). Os valores são os da documentação da API de Cobrança; conferidos no
    /// sandbox (runbook).
    /// </summary>
    public static string BankReason(BillingSlipCancelReason reason) => reason switch
    {
        BillingSlipCancelReason.PaidOtherMeans or BillingSlipCancelReason.SettledOtherMeans => "PAGODIRETOAOCLIENTE",
        BillingSlipCancelReason.CustomerRequest => "APEDIDODOCLIENTE",
        BillingSlipCancelReason.Replacement or BillingSlipCancelReason.AmountChanged or BillingSlipCancelReason.DueDateChanged => "SUBSTITUICAO",
        _ => "ACERTOS",
    };
}
