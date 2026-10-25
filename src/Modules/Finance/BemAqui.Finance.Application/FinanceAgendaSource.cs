using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Detector do Financeiro para a Minha pauta (DSH-04 CA 2): OP aguardando a decisão (o mesmo filtro do "Aguardando você"), OP
/// aprovada para pagar, títulos a receber vencidos e títulos a pagar vencendo sem OP. Cada categoria são agregados e as 5 primeiras
/// linhas por índice, com limite; o "Caixa projetado negativo" entra com a FIN-07.
/// </summary>
public sealed class FinanceAgendaSource(IFinanceDb db, PaymentOrderService orders, IParties parties) : IAgendaSource
{
    public const string AwaitingDecision = "finance.payment_orders_awaiting";
    public const string ToPay = "finance.payment_orders_to_pay";
    public const string ReceivablesOverdue = "finance.receivables_overdue";
    public const string PayablesDueSoon = "finance.payables_due_soon";

    /// <summary>Títulos a pagar que vencem em até 3 dias (ponto 16 da revisão).</summary>
    public const int PayablesHorizonDays = 3;

    private const string OrdersLink = "/app/financeiro/ordens-de-pagamento";

    private static readonly AgendaCategory[] Declared =
    [
        new(AwaitingDecision, "finance", "finance.approve", AgendaTier.Unblocks, "as ordens de pagamento aguardando você"),
        new(ToPay, "finance", "finance.pay", AgendaTier.Unblocks, "as ordens de pagamento para pagar"),
        new(ReceivablesOverdue, "finance", "finance.edit", AgendaTier.Overdue, "os títulos a receber vencidos"),
        new(PayablesDueSoon, "finance", "finance.create", AgendaTier.DueSoon, "os títulos a pagar vencendo"),
    ];

    private static readonly ReceivableStatus[] OpenReceivable = [ReceivableStatus.Open, ReceivableStatus.PartiallyPaid];
    private static readonly PayableStatus[] OpenPayable = [PayableStatus.Open, PayableStatus.PartiallyPaid];

    public string Module => "finance";

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<AgendaItem?>();
        if (request.Wants(AwaitingDecision))
        {
            items.Add(await AwaitingAsync(request, cancellationToken));
        }

        if (request.Wants(ToPay))
        {
            items.Add(await ToPayAsync(request, cancellationToken));
        }

        if (request.Wants(ReceivablesOverdue))
        {
            items.Add(await OverdueAsync(request, cancellationToken));
        }

        if (request.Wants(PayablesDueSoon))
        {
            items.Add(await DueSoonAsync(request, cancellationToken));
        }

        return [.. items.OfType<AgendaItem>()];
    }

    private async Task<AgendaItem?> AwaitingAsync(AgendaRequest request, CancellationToken ct)
    {
        var ids = await orders.AwaitingMeAsync(request.UserId, ct);
        if (ids.Count == 0)
        {
            return null;
        }

        var rows = await db.PaymentOrders.AsNoTracking().Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.Number, o.Total, o.ExpectedDate }).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return null;
        }

        var top = rows.OrderBy(o => o.ExpectedDate).ThenByDescending(o => o.Total).Take(AgendaLimits.EntriesPerCategory).ToList();
        var first = rows.Min(o => o.ExpectedDate);
        var total = rows.Sum(o => o.Total);
        return new AgendaItem(
            AwaitingDecision,
            AgendaText.Count(rows.Count, "ordem de pagamento aguarda você", "ordens de pagamento aguardam você"),
            AgendaText.Sentence(AgendaText.Total(total), AgendaText.Due("a primeira", first, request.Today)),
            OrdersLink + "?filtro=aguardando-voce", first, total, AgendaTier.Unblocks, rows.Count, "Aprovar",
            [.. top.Select(o => new AgendaEntry(o.Number, "Prevista para " + AgendaText.DayMonth(o.ExpectedDate, request.Today), $"{OrdersLink}/{o.Id}?acao=aprovar", o.ExpectedDate, o.Total))]);
    }

    private async Task<AgendaItem?> ToPayAsync(AgendaRequest request, CancellationToken ct)
    {
        var approved = db.PaymentOrders.AsNoTracking().Where(o => o.Status == PaymentOrderStatus.Approved);
        var count = await approved.CountAsync(ct);
        if (count == 0)
        {
            return null;
        }

        var total = await approved.SumAsync(o => o.Total, ct);
        var first = await approved.MinAsync(o => o.ExpectedDate, ct);
        var top = await approved.OrderBy(o => o.ExpectedDate).ThenByDescending(o => o.Total).Take(AgendaLimits.EntriesPerCategory)
            .Select(o => new { o.Id, o.Number, o.Total, o.ExpectedDate }).ToListAsync(ct);
        return new AgendaItem(
            ToPay,
            AgendaText.Count(count, "ordem de pagamento aprovada para pagar", "ordens de pagamento aprovadas para pagar"),
            AgendaText.Sentence(AgendaText.Total(total), "a primeira está prevista para " + AgendaText.Day(first, request.Today)),
            OrdersLink + "?situacao=aprovada", first, total, AgendaTier.Unblocks, count, "Pagar",
            [.. top.Select(o => new AgendaEntry(o.Number, "Prevista para " + AgendaText.DayMonth(o.ExpectedDate, request.Today), $"{OrdersLink}/{o.Id}?acao=pagar", o.ExpectedDate, o.Total))]);
    }

    private async Task<AgendaItem?> OverdueAsync(AgendaRequest request, CancellationToken ct)
    {
        var today = request.Today;
        var overdue = db.Receivables.AsNoTracking().Where(r => OpenReceivable.Contains(r.Status) && r.DueDate != null && r.DueDate < today);
        var count = await overdue.CountAsync(ct);
        if (count == 0)
        {
            return null;
        }

        var total = await overdue.SumAsync(r => r.Amount - r.PaidAmount, ct);
        var first = (await overdue.MinAsync(r => r.DueDate, ct))!.Value;
        var customers = await overdue.Select(r => r.PartyId).Distinct().CountAsync(ct);
        var top = await overdue.OrderBy(r => r.DueDate).ThenByDescending(r => r.Amount - r.PaidAmount).Take(AgendaLimits.EntriesPerCategory)
            .Select(r => new { r.Id, r.Number, r.PartyId, r.DueDate, Balance = r.Amount - r.PaidAmount }).ToListAsync(ct);
        var names = await NamesAsync(top.Select(r => r.PartyId), ct);
        return new AgendaItem(
            ReceivablesOverdue,
            AgendaText.Count(count, "título a receber vencido", "títulos a receber vencidos"),
            AgendaText.Sentence(AgendaText.Total(total) + (customers == 1 ? " de 1 cliente" : $" de {customers} clientes"), AgendaText.Due("o mais antigo", first, today)),
            "/app/financeiro/receber?situacao=vencido", first, total, AgendaTier.Overdue, count, "Cobrar",
            [.. top.Select(r => new AgendaEntry(r.Number, names.GetValueOrDefault(r.PartyId), "/app/financeiro/receber/" + r.Id, r.DueDate, r.Balance))]);
    }

    private async Task<AgendaItem?> DueSoonAsync(AgendaRequest request, CancellationToken ct)
    {
        var today = request.Today;
        var limit = today.AddDays(PayablesHorizonDays);

        // Fora de OP = Open ou PartiallyPaid (em OP o título fica AwaitingApproval ou Approved, FIN-03a).
        var due = db.Payables.AsNoTracking().Where(p => OpenPayable.Contains(p.Status) && p.DueDate != null && p.DueDate >= today && p.DueDate <= limit);
        var count = await due.CountAsync(ct);
        if (count == 0)
        {
            return null;
        }

        var total = await due.SumAsync(p => p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount, ct);
        var first = (await due.MinAsync(p => p.DueDate, ct))!.Value;
        var top = await due.OrderBy(p => p.DueDate).ThenByDescending(p => p.Amount).Take(AgendaLimits.EntriesPerCategory)
            .Select(p => new { p.Id, p.Number, p.PartyId, p.DueDate, Balance = p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount })
            .ToListAsync(ct);
        var names = await NamesAsync(top.Select(p => p.PartyId), ct);
        return new AgendaItem(
            PayablesDueSoon,
            AgendaText.Count(count, "título a pagar vence em breve sem OP", "títulos a pagar vencem em breve sem OP"),
            AgendaText.Sentence(AgendaText.Total(total), AgendaText.Due("o primeiro", first, today)),
            "/app/financeiro/pagar?situacao=aberto", first, total, AgendaTier.DueSoon, count, "Gerar OP",
            [.. top.Select(p => new AgendaEntry(p.Number, names.GetValueOrDefault(p.PartyId), $"{OrdersLink}/nova?titulos={p.Id}", p.DueDate, p.Balance))]);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? [] : (await parties.FindManyAsync(list, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
    }
}
