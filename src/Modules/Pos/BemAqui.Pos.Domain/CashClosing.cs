using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Pos.Domain;

/// <summary>Por que o fechamento foi para revisão do gerente (CX-02 CA 6). Combináveis.</summary>
[Flags]
public enum CashReviewReasons
{
    None = 0,

    /// <summary>Alguma forma de pagamento com a diferença acima da tolerância da loja.</summary>
    BreakAboveTolerance = 1,

    /// <summary>Sangria acima do dinheiro calculado (CX-01 CA 4), refeita no servidor.</summary>
    CashOutAboveExpected = 2,

    /// <summary>Aprovações sem rede acima do limite da loja (OFF-03; reservado).</summary>
    OfflineApprovals = 4,
}

/// <summary>
/// Uma linha da contagem do fechamento (<c>pos.cash_counts</c>, CX-02 CA 2): o valor contado de uma forma de pagamento (dinheiro, com a contagem por
/// cédula e moeda opcional, ou o total dos comprovantes de cartão e de Pix). O esperado e a diferença são calculados <b>só no servidor</b>, depois da
/// contagem, e nunca descem para o aparelho (conferência cega, CA 3). Uma contagem por fechamento (<see cref="CountId"/> do aparelho); depois de
/// uma reabertura, a contagem nova fica ao lado da antiga, que continua no histórico.
/// </summary>
[DocumentName("Contagem do caixa")]
public sealed class CashCount : Entity, IAuditable
{
    public Guid SessionId { get; init; }

    public Guid CountId { get; init; }

    /// <summary>A forma de pagamento; nulo = dinheiro da gaveta sem forma "Dinheiro" cadastrada.</summary>
    public Guid? PaymentMethodId { get; init; }

    /// <summary>Tipo da forma no instante da contagem (<c>Cash</c>, <c>Pix</c>, <c>Debit</c>...), do cadastro do servidor.</summary>
    public string MethodType { get; init; } = string.Empty;

    public decimal Counted { get; init; }

    /// <summary>Contagem por cédula e moeda (jsonb, opcional): <c>[{"cents":5000,"quantity":3}]</c>.</summary>
    public string? Denominations { get; init; }

    /// <summary>Esperado da forma (servidor), nulo até o cálculo.</summary>
    public decimal? Expected { get; set; }

    /// <summary>Pix "Não localizado" no extrato (CA 13): vira quebra da forma Pix.</summary>
    public decimal PixNotFound { get; set; }

    /// <summary>Contado − Pix não localizado − esperado: positivo = sobra, negativo = falta.</summary>
    public decimal? Difference { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Resultado da conferência de um Pix atestado pelo operador contra o extrato do banco (CX-02 CA 13).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PixCheckStatus>))]
public enum PixCheckStatus
{
    Found,
    NotFound,
}

/// <summary>
/// Conferência de um Pix atestado pelo operador (<c>pos.cash_pix_checks</c>, CX-02 CA 13): quem tem <c>sales.approve</c> marca "Conferido" ou "Não
/// localizado" contra o extrato. O pagamento é do <c>Sales</c> (só o id); o não localizado vira quebra da forma Pix.
/// </summary>
[DocumentName("Conferência de Pix")]
public sealed class CashPixCheck : Entity, IAuditable
{
    public Guid SessionId { get; init; }

    public Guid PaymentId { get; init; }

    public Guid SaleId { get; init; }

    public Guid PaymentMethodId { get; init; }

    public decimal Amount { get; init; }

    public PixCheckStatus Status { get; set; }

    public Guid CheckedBy { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>O que foi contado numa forma: a forma (nulo = dinheiro sem forma), o tipo e o valor.</summary>
public sealed record CashCountedLine(Guid? MethodId, string MethodType, decimal Counted);

/// <summary>Uma forma no fechamento: esperado (servidor), contado, Pix não localizado e a diferença (sobra positiva, falta negativa).</summary>
public sealed record CashClosingLine(Guid? MethodId, string MethodType, decimal Expected, decimal Counted, decimal PixNotFound)
{
    public decimal Difference => Counted - PixNotFound - Expected;
}

/// <summary>
/// Regras do fechamento com conferência cega (CX-02), puras, para o servidor:
/// - <b>Esperado por forma</b>: as vendas da sessão (pagamentos capturados líquidos do troco, sem as canceladas); no dinheiro, mais o fundo de troco,
///   os suprimentos e o recolhimento devolvido, menos as sangrias e o recolhimento. Todas as formas do tipo dinheiro são uma gaveta só.
/// - <b>Tolerância por forma</b>, em módulo (CA 5): sobra numa forma não compensa falta em outra.
/// - <b>Recolhimento</b> = dinheiro contado − fundo que fica (CA 12).
/// </summary>
public static class CashClosing
{
    public const string CashType = "Cash";
    public const string PixType = "Pix";

    /// <summary>Dinheiro esperado na gaveta: fundo + vendas em dinheiro + efeito dos movimentos (sangria, suprimento, recolhimento).</summary>
    public static decimal ExpectedDrawer(decimal opening, decimal cashSales, IEnumerable<(CashMovementKind Kind, decimal Amount)> movements) =>
        opening + cashSales + (movements ?? []).Sum(m => CashMovement.DrawerEffect(m.Kind, m.Amount));

    /// <summary>
    /// As linhas do fechamento. <paramref name="salesByMethod"/>: vendas líquidas por forma; <paramref name="methodTypes"/>: o tipo de cada forma;
    /// <paramref name="drawerExtra"/>: fundo + movimentos (o que entra no dinheiro além das vendas); <paramref name="pixNotFound"/>: Pix não
    /// localizado por forma.
    /// </summary>
    public static IReadOnlyList<CashClosingLine> Lines(
        IReadOnlyDictionary<Guid, decimal> salesByMethod, IReadOnlyDictionary<Guid, string> methodTypes, IReadOnlyList<CashCountedLine> counted, decimal drawerExtra,
        IReadOnlyDictionary<Guid, decimal>? pixNotFound = null)
    {
        ArgumentNullException.ThrowIfNull(salesByMethod);
        ArgumentNullException.ThrowIfNull(methodTypes);
        ArgumentNullException.ThrowIfNull(counted);
        string TypeOf(Guid? id, string? fallback = null) => id is { } m && methodTypes.TryGetValue(m, out var t) ? t : fallback ?? string.Empty;

        // A gaveta: a primeira forma de dinheiro contada, ou a primeira com venda, ou nenhuma (dinheiro sem forma).
        var drawerKey = counted.Where(c => TypeOf(c.MethodId, c.MethodType) == CashType).Select(c => c.MethodId).FirstOrDefault()
            ?? salesByMethod.Keys.Where(k => TypeOf(k) == CashType).OrderBy(k => k).Select(k => (Guid?)k).FirstOrDefault();
        Guid? KeyOf(Guid? id, string type) => type == CashType ? drawerKey : id;

        var expected = new Dictionary<Guid, decimal>();
        var counts = new Dictionary<Guid, decimal>();
        var types = new Dictionary<Guid, string>();
        var order = new List<Guid>();
        var drawerSlot = Guid.Empty; // a gaveta sem forma usa Guid.Empty como chave interna
        Guid Slot(Guid? key) => key ?? drawerSlot;

        void Touch(Guid slot, string type)
        {
            if (!types.ContainsKey(slot))
            {
                types[slot] = type;
                order.Add(slot);
            }
        }

        Touch(Slot(drawerKey), CashType);
        expected[Slot(drawerKey)] = drawerExtra;
        foreach (var (method, amount) in salesByMethod)
        {
            var type = TypeOf(method);
            var slot = Slot(KeyOf(method, type));
            Touch(slot, type);
            expected[slot] = expected.GetValueOrDefault(slot) + amount;
        }

        foreach (var line in counted)
        {
            var type = TypeOf(line.MethodId, line.MethodType);
            var slot = Slot(KeyOf(line.MethodId, type));
            Touch(slot, type);
            counts[slot] = counts.GetValueOrDefault(slot) + line.Counted;
        }

        return order.Select(slot => new CashClosingLine(
            slot == drawerSlot ? null : slot, types[slot], expected.GetValueOrDefault(slot), counts.GetValueOrDefault(slot),
            pixNotFound is not null && slot != drawerSlot ? pixNotFound.GetValueOrDefault(slot) : 0m)).ToList();
    }

    /// <summary>CA 5: todas as formas com a diferença, em módulo, até a tolerância (sobra numa não compensa falta em outra).</summary>
    public static bool WithinTolerance(IEnumerable<CashClosingLine> lines, decimal tolerance) => (lines ?? []).All(l => Math.Abs(l.Difference) <= tolerance);

    /// <summary>CA 12: recolhimento = dinheiro contado − fundo que fica (o fundo nunca passa do contado).</summary>
    public static decimal Deposit(decimal countedCash, decimal kept) => Math.Max(0m, countedCash - kept);
}
