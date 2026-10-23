using BemAqui.Purchasing.Contracts;

namespace BemAqui.Purchasing.Domain;

/// <summary>Estado de uma linha de falta da mesma OS e item, para a recomposição (CA 5, 6 e 13).</summary>
public sealed record ShortageLineState(Guid Id, PurchaseRequestStatus Status, decimal Quantity, bool ManuallyEdited);

/// <summary>
/// O que a recomposição faz com a falta de um item numa OS: <c>Skip</c> quando há linha editada à mão (a mão manda); senão a linha
/// automática em aberto vai a <c>OpenTarget</c> (0 = cancelar). <c>DropNotice</c> é a falta atual quando ela ficou abaixo do que já está
/// em cotação ou pedido (o comprador é avisado), com a situação dessas linhas.
/// </summary>
public sealed record ShortagePlan(bool Skip, decimal OpenTarget, decimal? DropNotice, PurchaseRequestStatus? DropStatus);

/// <summary>Regras puras da solicitação de compra (CMP-01), testadas sem banco.</summary>
public static class PurchaseRequestRules
{
    public const int DefaultLeadDays = 15;
    public const int MinLeadDays = 0;
    public const int MaxLeadDays = 365;
    public const int MaxNotes = 500;
    public const int MaxReason = 500;
    public const int MaxLines = 50;
    public const decimal MaxQuantity = 99_999_999_999_999.9999m;

    /// <summary>Motivo gravado quando a falta zera na fonte (CA 6).</summary>
    public const string ShortageResolvedReason = "Falta resolvida pelo estoque";

    /// <summary>Motivo gravado quando o item volta ao mínimo (CA 9).</summary>
    public const string MinStockResolvedReason = "Estoque voltou ao mínimo";

    /// <summary>
    /// Situação da SC pelas linhas (regra da story): todas canceladas → <c>Cancelled</c>; alguma em cotação → <c>Quoting</c>; todas as não
    /// canceladas pedidas → <c>Ordered</c>; senão <c>Open</c>.
    /// </summary>
    public static PurchaseRequestStatus DeriveStatus(IEnumerable<PurchaseRequestStatus> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var active = lines.Where(s => s != PurchaseRequestStatus.Cancelled).ToList();
        if (active.Count == 0)
        {
            return PurchaseRequestStatus.Cancelled;
        }

        if (active.Contains(PurchaseRequestStatus.Quoting))
        {
            return PurchaseRequestStatus.Quoting;
        }

        return active.All(s => s == PurchaseRequestStatus.Ordered) ? PurchaseRequestStatus.Ordered : PurchaseRequestStatus.Open;
    }

    /// <summary>
    /// Necessidade da linha da falta (CA 5): data prometida do projeto − prazo padrão; sem data prometida, hoje + prazo padrão. Pode ficar
    /// no passado (a lista mostra "Atrasada").
    /// </summary>
    public static DateOnly ShortageNeededBy(DateOnly? promisedDate, DateOnly today, int leadDays) =>
        promisedDate is { } promised ? promised.AddDays(-leadDays) : today.AddDays(leadDays);

    /// <summary>Necessidade sugerida da linha manual e da linha do mínimo: hoje + prazo padrão (CA 3, 9).</summary>
    public static DateOnly SuggestedNeededBy(DateOnly today, int leadDays) => today.AddDays(leadDays);

    /// <summary>
    /// Recomposição da falta de um item na OS (CA 5, 6 e 13): a falta atual da fonte menos o que já está em cotação ou pedido vira a linha
    /// em aberto; linha editada à mão (aberta ou cancelada pelo comprador) faz a recomposição deixar o item como está.
    /// </summary>
    public static ShortagePlan PlanShortage(decimal shortage, IReadOnlyCollection<ShortageLineState> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Any(l => l.ManuallyEdited))
        {
            return new ShortagePlan(true, 0m, null, null);
        }

        var current = Math.Max(0m, shortage);
        var committed = lines.Where(l => l.Status is PurchaseRequestStatus.Quoting or PurchaseRequestStatus.Ordered).ToList();
        var committedQty = committed.Sum(l => l.Quantity);
        var target = Math.Max(0m, current - committedQty);
        if (committed.Count > 0 && committedQty > current)
        {
            var status = committed.Any(l => l.Status == PurchaseRequestStatus.Quoting) ? PurchaseRequestStatus.Quoting : PurchaseRequestStatus.Ordered;
            return new ShortagePlan(false, target, current, status);
        }

        return new ShortagePlan(false, target, null, null);
    }

    /// <summary>
    /// Posição do item contra o mínimo (CA 9, ponto 30): disponível em todos os locais + o que já está em compra para o estoque geral (linhas
    /// "Estoque geral" ainda não pedidas, fora a própria linha do mínimo, e o saldo a receber dos pedidos em aberto).
    /// </summary>
    public static decimal Position(decimal available, decimal generalPending, decimal openOrders) => available + generalPending + openOrders;

    /// <summary>
    /// Quanto comprar pelo mínimo (CA 9): abaixo do mínimo, compra até ele (mínimo − posição, arredondado para cima na unidade de estoque);
    /// no mínimo ou acima, 0 (a linha em aberto é cancelada).
    /// </summary>
    public static decimal MinStockQuantity(decimal minStock, decimal position) =>
        minStock <= 0m || position >= minStock ? 0m : Math.Ceiling(minStock - position);

    /// <summary>"Precisa em {n} dias" / "Atrasada {n} d" (CA 12): dias até a necessidade; negativo = atrasada.</summary>
    public static int DaysUntil(DateOnly neededBy, DateOnly today) => neededBy.DayNumber - today.DayNumber;
}
