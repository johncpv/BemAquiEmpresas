using System.Globalization;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Blocos do painel do supervisor (DSH-03 CA 1 e 9) e as regras sem banco: a tela é da Fábrica (módulo <c>production</c> e
/// <c>production.view</c>, sem <c>dashboard.view</c>); cada bloco confere o módulo e a permissão do dono. Ordem dos códigos = a do celular
/// (Qualidade → Faltas → Quadro → Capacidade → Indicadores).
/// </summary>
public static class SupervisorBlocks
{
    public const string Module = "production";
    public const string ViewPermission = "production.view";
    public const string QualityPermission = "production.approve_quality";
    public const string InventoryModule = "inventory";
    public const string ShortagesPermission = "inventory.view";
    public const string PurchasingModule = "purchasing";
    public const string CreateRequestPermission = "purchasing.create";
    public const string ViewPurchasingPermission = "purchasing.view";

    public const string Quality = "quality";
    public const string Shortages = "shortages";
    public const string Board = "board";
    public const string Capacity = "capacity";
    public const string Indicators = "indicators";

    /// <summary>Semanas da capacidade: a atual e as próximas 4 (CA 3).</summary>
    public const int CapacityWeeks = 5;

    /// <summary>Período dos indicadores: os últimos 30 dias, contando hoje (CA 6).</summary>
    public const int IndicatorDays = 30;

    /// <summary>Corte das faltas que bloqueiam: o início previsto em até 5 dias úteis (CA 5, ponto 52).</summary>
    public const int BlockingWorkingDays = 5;

    /// <summary>Janela lida do calendário para contar os dias úteis até o início (além dela, a falta não bloqueia).</summary>
    public const int CalendarWindowDays = 31;

    /// <summary>Linhas por bloco (CA 8: nomes só da página).</summary>
    public const int ShortageRows = 30;
    public const int NonConformityRows = 20;
    public const int BoardCardRows = 50;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Dias úteis até o início previsto (CA 5): quantos dias úteis depois de hoje até o início, inclusive; 0 se o início é hoje ou já passou; nulo
    /// sem previsão ou além da janela lida do calendário.
    /// </summary>
    public static int? WorkingDaysUntil(DateOnly today, DateOnly? start, IReadOnlyList<DateOnly> workingDates)
    {
        ArgumentNullException.ThrowIfNull(workingDates);
        if (start is not { } date)
        {
            return null;
        }

        if (date <= today)
        {
            return 0;
        }

        return date > today.AddDays(CalendarWindowDays) ? null : workingDates.Count(d => d > today && d <= date);
    }

    /// <summary>Bloqueia: o início previsto cai em até 5 dias úteis.</summary>
    public static bool Blocks(int? workingDaysUntil) => workingDaysUntil is { } days && days <= BlockingWorkingDays;

    /// <summary>Data curta da tela: "12/11".</summary>
    public static string Day(DateOnly date) => date.ToString("dd/MM", PtBr);

    /// <summary>
    /// Situação da compra da falta (CA 5, ponto 53), pelas linhas de SC da OS e do item e os pedidos que as atendem: com pedido, "Pedido {PC} previsto
    /// para {data}" (o previsto mais cedo); em cotação, "Em cotação"; aberta, "Solicitada {SC}"; sem linha, "Sem solicitação".
    /// </summary>
    public static (PurchaseSituation Situation, string Label, Guid? RequestId, Guid? OrderId) Purchase(
        IReadOnlyCollection<PurchaseRequestLineInfo> lines, IReadOnlyCollection<PurchaseOrderForRequestLine> orders)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(orders);
        var mine = lines.Where(l => l.Status != PurchaseRequestStatus.Cancelled).ToList();
        if (mine.Count == 0)
        {
            return (PurchaseSituation.None, "Sem solicitação", null, null);
        }

        var ids = mine.Select(l => l.LineId).ToHashSet();
        var order = orders.Where(o => ids.Contains(o.RequestLineId)).OrderBy(o => o.ExpectedDate).ThenBy(o => o.Number, StringComparer.Ordinal).FirstOrDefault();
        if (order is not null)
        {
            return (PurchaseSituation.Ordered, $"Pedido {order.Number} previsto para {Day(order.ExpectedDate)}", mine[0].RequestId, order.PurchaseOrderId);
        }

        if (mine.FirstOrDefault(l => l.Status == PurchaseRequestStatus.Quoting) is { } quoting)
        {
            return (PurchaseSituation.Quoting, "Em cotação", quoting.RequestId, null);
        }

        var open = mine.OrderBy(l => l.RequestNumber, StringComparer.Ordinal).First();
        return (PurchaseSituation.Requested, "Solicitada " + open.RequestNumber, open.RequestId, null);
    }

    /// <summary>Ordem das faltas (CA 5, propositivo): as que bloqueiam primeiro (a mais próxima antes), depois pela data em que são necessárias.</summary>
    public static IOrderedEnumerable<T> Order<T>(IEnumerable<T> rows, Func<T, int?> blocksIn, Func<T, DateOnly?> neededOn, Func<T, string> orderNumber)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.OrderBy(r => Blocks(blocksIn(r)) ? 0 : 1).ThenBy(r => Blocks(blocksIn(r)) ? blocksIn(r)!.Value : int.MaxValue)
            .ThenBy(r => neededOn(r) is null ? 1 : 0).ThenBy(r => neededOn(r)).ThenBy(orderNumber, StringComparer.Ordinal);
    }

    /// <summary>Rótulo do motivo de parada (CA 6).</summary>
    public static string StoppageLabel(StoppageReason reason) => reason switch
    {
        StoppageReason.Material => "Material",
        StoppageReason.Machine => "Máquina",
        _ => "Outro",
    };
}
