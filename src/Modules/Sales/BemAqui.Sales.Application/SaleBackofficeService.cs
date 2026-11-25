using System.Globalization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Retaguarda de vendas (PDV-07): a lista com filtros, os totais do filtro e a exportação (ADR 0045), o detalhe com a trilha completa e a leitura
/// auditada do documento do consumidor. Custo e margem só com <c>sales.view_cost</c> (ausentes sem ela); o CPF vem mascarado na lista e no arquivo.
/// Sem restrição por loja no MVP (PP-42, opção a): quem tem <c>sales.view</c> vê as vendas de todas as lojas do tenant.
/// </summary>
public sealed class SaleBackofficeService(
    ISalesDb db,
    SaleCustomerDocumentService documents,
    IPosRegisters registers,
    IPosBackoffice pos,
    IPaymentMethods methods,
    IUserDirectory users,
    IPermissionChecker permissions,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const int MaxDays = 366;
    public const string RevealRateLimitPolicy = "sales-reveal-document";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Rótulos dos estados da venda, iguais aos chips da tela.</summary>
    public static string StatusLabel(SaleStatus status) => status switch
    {
        SaleStatus.Completed => "Concluída",
        SaleStatus.Cancelled => "Cancelada",
        SaleStatus.PartiallyReturned => "Devolvida em parte",
        SaleStatus.Returned => "Devolvida",
        SaleStatus.PartiallyPaid => "Paga em parte",
        _ => "Aberta",
    };

    public async Task<Result<SaleListPage>> ListAsync(SaleListFilter filter, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(filter) is { } invalid)
        {
            return invalid;
        }

        var cost = await CanSeeCostAsync(cancellationToken);
        var page = request.Normalize();
        var query = Filtered(filter);
        var done = query.Where(s => ReturnRules.Done.Contains(s.Status));
        var count = await query.CountAsync(cancellationToken);
        var total = await done.SumAsync(s => (decimal?)s.Total, cancellationToken) ?? 0m;
        var cancelled = await query.Where(s => s.Status == SaleStatus.Cancelled)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Total = g.Sum(s => s.Total) }).FirstOrDefaultAsync(cancellationToken);
        decimal? totalCost = null;
        if (cost)
        {
            totalCost = SaleRules.Money(await done.Join(db.SaleLines.AsNoTracking(), s => s.Id, l => l.SaleId, (s, l) => l)
                .SumAsync(l => (decimal?)((l.UnitCost ?? 0m) * l.Quantity), cancellationToken) ?? 0m);
        }

        var items = await BuildAsync(Sorted(query, page.Sort).Skip(page.Skip).Take(page.PageSize), cost, cancellationToken);
        var totals = new SaleListTotals(count, total, cancelled?.Count ?? 0, cancelled?.Total ?? 0m, totalCost, totalCost is { } c ? total - c : null);
        return new SaleListPage(items, page.Page, page.PageSize, count, totals);
    }

    /// <summary>Exportação (CA 3, ADR 0045): o mesmo filtro, sem paginação, até o teto do formato; acima dele, a mensagem pede para refinar.</summary>
    public async Task<Result<ExportData<SaleListItem>>> ExportAsync(SaleListFilter filter, string? sort, int max, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (Validate(filter) is { } invalid)
        {
            return invalid;
        }

        var query = Filtered(filter);
        var total = await query.CountAsync(cancellationToken);
        List<SaleListItem> items = total > max ? [] : await BuildAsync(Sorted(query, sort), await CanSeeCostAsync(cancellationToken), cancellationToken);
        var stores = filter.StoreId is { } storeId ? (await registers.ListStoresAsync(cancellationToken)).FirstOrDefault(s => s.Id == storeId)?.Name : null;
        IReadOnlyList<ExportFilter> filters =
        [
            new("Período", $"{filter.From.ToString("dd/MM/yyyy", PtBr)} a {filter.To.ToString("dd/MM/yyyy", PtBr)}"),
            new("Loja", stores),
            new("Caixa", filter.RegisterId?.ToString()),
            new("Operador", filter.OperatorId?.ToString()),
            new("Forma de pagamento", filter.PaymentMethodId?.ToString()),
            new("Estado", filter.State?.ToString()),
        ];
        return new ExportData<SaleListItem>(Spec, filters, new ExportRows<SaleListItem>(total, items));
    }

    /// <summary>Colunas do arquivo: o consumidor sempre mascarado; custo e margem saem do arquivo sem <c>sales.view_cost</c>.</summary>
    public static ExportSpec<SaleListItem> Spec { get; } = new ExportSpec<SaleListItem>("Sale", "vendas", "Vendas")
        .Text("Número", r => r.Number)
        .DateTime("Data e hora", r => r.At)
        .Text("Loja", r => r.StoreName)
        .Text("Caixa", r => r.RegisterLabel).NotInPdf()
        .Text("Operador", r => r.OperatorName)
        .Text("Formas", r => string.Join(", ", r.PaymentMethods))
        .Text("Estado", r => string.Join(", ", Chips(r)))
        .Text("Consumidor", r => r.CustomerDocument).NotInPdf()
        .Money("Total", r => r.Total)
        .Money("Custo", r => r.Cost, new ExportSensitivity("sales.view_cost") { Omit = true })
        .Money("Margem", r => r.Margin, new ExportSensitivity("sales.view_cost") { Omit = true });

    /// <summary>Os chips da venda, na ordem da tela.</summary>
    public static IReadOnlyList<string> Chips(SaleListItem row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var chips = new List<string> { StatusLabel(row.Status) };
        if (row.StockStatus == SaleStockStatus.Shortfall)
        {
            chips.Add("Estoque: falta de saldo");
        }

        if (row.HasDivergence)
        {
            chips.Add("Com divergência");
        }

        if (row.Offline)
        {
            chips.Add("Feita sem internet");
        }

        return chips;
    }

    public async Task<SaleDetail?> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null)
        {
            return null;
        }

        var cost = await CanSeeCostAsync(cancellationToken);
        var lines = await db.SaleLines.AsNoTracking().Where(l => l.SaleId == id).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
        var payments = await db.SalePayments.AsNoTracking().Where(p => p.SaleId == id).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var postings = await db.SaleStockPostings.AsNoTracking().Where(p => p.SaleId == id).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var masked = await documents.MaskedAsync([id], cancellationToken);
        var moments = new[] { sale.CompletedAtDevice, sale.CompletedAt, sale.CancelledAt, sale.ReceivedAt }.Where(m => m is not null).Select(m => m!.Value).ToList();
        var trail = await pos.SaleTrailAsync(id, sale.RegisterId, moments.Min().AddDays(-1), moments.Max().AddDays(1), cancellationToken);
        var methodNames = await MethodNamesAsync(cancellationToken);
        var names = await NamesAsync(
            new[] { sale.SellerUserId ?? sale.CreatedBy, sale.CancelledBy }.Concat(payments.Select(p => p.ConfirmedBy))
                .Concat(trail.Approvals.SelectMany(a => new Guid?[] { a.OperatorId, a.SupervisorId })).Concat(trail.Prints.Select(p => (Guid?)p.OperatorId))
                .Concat(trail.DeviceEvents.Select(e => e.OperatorId)),
            cancellationToken);
        var register = sale.RegisterId is { } r ? await registers.FindAsync(r, cancellationToken) : null;
        var storeName = sale.StoreId is { } st ? (await registers.ListStoresAsync(cancellationToken)).FirstOrDefault(s => s.Id == st)?.Name ?? "Loja removida" : null;
        var operatorId = sale.SellerUserId ?? sale.CreatedBy;
        decimal? saleCost = cost && lines.All(l => l.ItemId is null || l.UnitCost is not null) ? SaleRules.Money(lines.Sum(l => (l.UnitCost ?? 0m) * l.Quantity)) : null;
        var detailLines = lines.Select(l => new SaleDetailLine(l.Id, l.LineNumber, l.Description, l.UnitCode, l.Quantity, l.ListUnitPrice ?? l.UnitPrice, l.UnitPrice, l.Discount, l.Total,
            l.DiscountReason, l.PriceReason, cost ? l.UnitCost : null)).ToList();
        var detailPayments = payments.Select(p => new SaleDetailPayment(p.Id, methodNames.GetValueOrDefault(p.PaymentMethodId, "Forma inativa"), p.Amount, p.ChangeAmount, p.Status,
            p.Integration, p.Installments, p.CardBrand, p.Nsu, p.AuthorizationCode, p.ConfirmedAt, Name(names, p.ConfirmedBy))).ToList();
        return new SaleDetail(
            sale.Id, sale.Number, sale.Channel, sale.Status, sale.StockStatus, sale.PaymentDifference is not null, IsOffline(sale), sale.PaymentDifference, sale.StoreId, storeName,
            sale.RegisterId, register?.Label, sale.CashSessionId, operatorId, Name(names, operatorId), sale.BusinessDate, sale.CompletedAt ?? sale.ReceivedAt, sale.CompletedAtDevice,
            sale.ReceivedAt, sale.Subtotal, sale.DiscountTotal, sale.ServiceFee, sale.Total, masked.GetValueOrDefault(id), sale.CancelledAt, sale.CancelReason,
            Name(names, sale.CancelledBy), detailLines, detailPayments, Trail(sale, lines, postings, trail, names), saleCost, saleCost is { } sc ? sale.Total - sc : null);
    }

    /// <summary>CA 6: o documento inteiro, só para quem tem <c>sales.view_customer_document</c> (a rota exige, com limite por usuário), com a leitura gravada na auditoria (quem, quando, IP).</summary>
    public async Task<SaleCustomerDocumentView?> RevealDocumentAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        if (await documents.RevealAsync(saleId, cancellationToken) is not { } document)
        {
            return null;
        }

        var number = await db.Sales.AsNoTracking().Where(s => s.Id == saleId).Select(s => s.Number).FirstOrDefaultAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(SaleCustomerDocument), saleId.ToString(), $"Venda {number}: documento do consumidor", cancellationToken);
        return new SaleCustomerDocumentView(document);
    }

    private static Error? Validate(SaleListFilter filter) =>
        filter.To < filter.From || filter.To.DayNumber - filter.From.DayNumber >= MaxDays
            ? SaleReturnService.Field("from", "Escolha um período de até um ano, com a data final igual ou depois da inicial.")
            : null;

    private IQueryable<Sale> Filtered(SaleListFilter f)
    {
        var query = db.Sales.AsNoTracking().Where(s =>
            (s.Status == SaleStatus.Completed || s.Status == SaleStatus.PartiallyReturned || s.Status == SaleStatus.Returned || s.Status == SaleStatus.Cancelled)
            && s.BusinessDate >= f.From && s.BusinessDate <= f.To);
        if (f.StoreId is { } store)
        {
            query = query.Where(s => s.StoreId == store);
        }

        if (f.RegisterId is { } register)
        {
            query = query.Where(s => s.RegisterId == register);
        }

        if (f.OperatorId is { } op)
        {
            query = query.Where(s => (s.SellerUserId ?? s.CreatedBy) == op);
        }

        if (f.PaymentMethodId is { } method)
        {
            query = query.Where(s => db.SalePayments.Any(p => p.SaleId == s.Id && p.PaymentMethodId == method));
        }

        var offlineAfter = CashSessionSalesSummary.OfflineAfter;
        return f.State switch
        {
            SaleListState.Completed => query.Where(s => s.Status == SaleStatus.Completed),
            SaleListState.Cancelled => query.Where(s => s.Status == SaleStatus.Cancelled),
            SaleListState.Returned => query.Where(s => s.Status == SaleStatus.PartiallyReturned || s.Status == SaleStatus.Returned),
            SaleListState.StockShortfall => query.Where(s => s.StockStatus == SaleStockStatus.Shortfall),
            SaleListState.Divergence => query.Where(s => s.PaymentDifference != null),
            SaleListState.Offline => query.Where(s => s.CompletedAtDevice != null && s.CompletedAtDevice + offlineAfter < s.ReceivedAt),
            _ => query,
        };
    }

    private static IQueryable<Sale> Sorted(IQueryable<Sale> query, string? sort) => sort switch
    {
        "total" => query.OrderBy(s => s.Total).ThenBy(s => s.Id),
        "-total" => query.OrderByDescending(s => s.Total).ThenByDescending(s => s.Id),
        "number" => query.OrderBy(s => s.Number),
        "-number" => query.OrderByDescending(s => s.Number),
        "at" => query.OrderBy(s => s.CompletedAt ?? s.ReceivedAt).ThenBy(s => s.Id),
        _ => query.OrderByDescending(s => s.CompletedAt ?? s.ReceivedAt).ThenByDescending(s => s.Id),
    };

    private async Task<List<SaleListItem>> BuildAsync(IQueryable<Sale> query, bool cost, CancellationToken cancellationToken)
    {
        var offlineAfter = CashSessionSalesSummary.OfflineAfter;
        var rows = await query.Select(s => new
        {
            s.Id, s.Number, At = s.CompletedAt ?? s.ReceivedAt, s.BusinessDate, s.Channel, s.StoreId, s.RegisterId, OperatorId = s.SellerUserId ?? s.CreatedBy, s.Total,
            s.Status, s.StockStatus, Divergence = s.PaymentDifference != null, Offline = s.CompletedAtDevice != null && s.CompletedAtDevice + offlineAfter < s.ReceivedAt,
        }).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var paid = await db.SalePayments.AsNoTracking().Where(p => ids.Contains(p.SaleId)).Select(p => new { p.SaleId, p.PaymentMethodId }).Distinct().ToListAsync(cancellationToken);
        var costBySale = new Dictionary<Guid, decimal>();
        if (cost)
        {
            var costs = await db.SaleLines.AsNoTracking().Where(l => ids.Contains(l.SaleId)).GroupBy(l => l.SaleId)
                .Select(g => new { SaleId = g.Key, Cost = g.Sum(l => (l.UnitCost ?? 0m) * l.Quantity), Missing = g.Any(l => l.ItemId != null && l.UnitCost == null) })
                .ToListAsync(cancellationToken);
            costBySale = costs.Where(c => !c.Missing).ToDictionary(c => c.SaleId, c => SaleRules.Money(c.Cost));
        }
        var masked = await documents.MaskedAsync(ids, cancellationToken);
        var methodNames = await MethodNamesAsync(cancellationToken);
        var stores = (await registers.ListStoresAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name);
        var registerIds = rows.Where(r => r.RegisterId is not null).Select(r => r.RegisterId!.Value).Distinct().ToList();
        var registerRefs = registerIds.Count == 0 ? new Dictionary<Guid, PosRegisterRef>() : await registers.FindManyAsync(registerIds, cancellationToken);
        var names = await NamesAsync(rows.Select(r => r.OperatorId), cancellationToken);
        return rows.Select(r =>
        {
            decimal? c = cost && costBySale.TryGetValue(r.Id, out var value) ? value : null;
            return new SaleListItem(
                r.Id, r.Number, r.At, r.BusinessDate, r.Channel, r.StoreId, r.StoreId is { } s ? stores.GetValueOrDefault(s, "Loja removida") : null, r.RegisterId,
                r.RegisterId is { } reg && registerRefs.TryGetValue(reg, out var rr) ? $"Caixa {rr.RegisterNumber:00}" : null, r.OperatorId, Name(names, r.OperatorId), r.Total,
                paid.Where(p => p.SaleId == r.Id).Select(p => methodNames.GetValueOrDefault(p.PaymentMethodId, "Forma inativa")).Distinct().Order(StringComparer.CurrentCulture).ToList(),
                r.Status, r.StockStatus, r.Divergence, r.Offline, masked.GetValueOrDefault(r.Id), c, c is { } cc ? r.Total - cc : null);
        }).ToList();
    }

    private static List<SaleTrailEntry> Trail(Sale sale, List<SaleLine> lines, List<SaleStockPosting> postings, PosSaleTrail trail, Dictionary<Guid, string> names)
    {
        var entries = new List<SaleTrailEntry>();
        foreach (var e in trail.DeviceEvents)
        {
            entries.Add(new SaleTrailEntry(e.At, "device", DeviceLabel(e.Type), e.OperatorId is { } o ? Name(names, o) : null, "info"));
        }

        foreach (var a in trail.Approvals)
        {
            entries.Add(new SaleTrailEntry(a.ApprovedAt, "approval", $"Supervisor aprovou: {ApprovalLabel(a.Action)}",
                $"{Name(names, a.SupervisorId)} para {Name(names, a.OperatorId)} · {Brl(a.BeforeAmount)} → {Brl(a.AfterAmount)} · {a.Reason}{(a.Online ? string.Empty : " · sem rede")}",
                a.Online ? "info" : "warn"));
        }

        foreach (var s in trail.Syncs)
        {
            var late = s.ReceivedAt - s.OccurredAt > CashSessionSalesSummary.OfflineAfter;
            entries.Add(new SaleTrailEntry(s.ReceivedAt, "sync", $"Chegou ao servidor: {SyncLabel(s.Kind)}",
                $"Feito no caixa em {Local(s.OccurredAt)}, chegou em {Local(s.ReceivedAt)}{(late ? " (feito sem internet)" : string.Empty)}{(s.Outcome == "Accepted" ? string.Empty : " · " + OutcomeLabel(s.Outcome))}{(s.ChainOk ? string.Empty : " · sequência do aparelho com falha")}",
                s.Outcome == "Quarantined" || !s.ChainOk ? "crit" : s.Outcome == "AcceptedWithDivergence" || late ? "warn" : "ok"));
        }

        if (trail.Syncs.Count == 0 && sale.FromDevice)
        {
            entries.Add(new SaleTrailEntry(sale.ReceivedAt, "sync", "Chegou ao servidor",
                $"Feita no caixa em {(sale.CompletedAtDevice is { } d ? Local(d) : "—")}, chegou em {Local(sale.ReceivedAt)}{(IsOffline(sale) ? " (feita sem internet)" : string.Empty)}",
                IsOffline(sale) ? "warn" : "ok"));
        }

        var items = lines.Where(l => l.ItemId is not null).GroupBy(l => l.ItemId!.Value).ToDictionary(g => g.Key, g => g.First().Description);
        foreach (var p in postings)
        {
            var item = items.GetValueOrDefault(p.ItemId, "Item");
            var (title, tone) = p.Status switch
            {
                SaleStockPostingStatus.Posted when p.ShortfallQuantity > 0 => ("Estoque: falta de saldo", "warn"),
                SaleStockPostingStatus.Posted => ("Estoque baixado", "ok"),
                SaleStockPostingStatus.Shortfall => ("Estoque: falta de saldo", "warn"),
                SaleStockPostingStatus.Blocked => ("Estoque: baixa bloqueada", "crit"),
                _ => ("Estoque estornado", "info"),
            };
            var detail = $"{item}: {p.Quantity.ToString("0.####", PtBr)}{(p.ShortfallQuantity > 0 ? $" (faltaram {p.ShortfallQuantity.ToString("0.####", PtBr)})" : string.Empty)}{(string.IsNullOrWhiteSpace(p.Note) ? string.Empty : " · " + p.Note)}";
            entries.Add(new SaleTrailEntry(p.CreatedAt, "stock", title, detail, tone));
        }

        if (sale.StockStatus == SaleStockStatus.Pending && sale.Status != SaleStatus.Cancelled)
        {
            entries.Add(new SaleTrailEntry(sale.ReceivedAt, "stock", "Estoque: baixa pendente", "A baixa sai em instantes depois que a venda chega.", "info"));
        }

        entries.Add(sale.CashSessionId is not null
            ? new SaleTrailEntry(sale.ReceivedAt, "finance", "Financeiro: entra no fechamento do caixa", "O recebível da venda é lançado quando a sessão de caixa fecha.", "info")
            : new SaleTrailEntry(sale.ReceivedAt, "finance", "Financeiro: recebível do dia", "O recebível entra no título do dia e do canal da venda.", "info"));

        foreach (var p in trail.Prints)
        {
            var reprint = p.Kind == "Reprint";
            entries.Add(new SaleTrailEntry(p.PrintedAt, "print", reprint ? $"Reimpressão ({p.Copy}ª via)" : "Cupom impresso", Name(names, p.OperatorId), reprint ? "warn" : "info"));
        }

        foreach (var d in trail.Divergences)
        {
            entries.Add(new SaleTrailEntry(d.OccurredAt, "pending", d.Quarantined ? "Pendência do PDV (quarentena)" : "Pendência do PDV",
                $"{d.Summary} · {(d.Status == "Open" ? "aberta" : "resolvida")}", d.Status == "Open" ? (d.Quarantined ? "crit" : "warn") : "ok"));
        }

        if (sale.CancelledAt is { } cancelledAt)
        {
            entries.Add(new SaleTrailEntry(cancelledAt, "sale", "Venda cancelada", $"{Name(names, sale.CancelledBy)} · {sale.CancelReason}", "crit"));
        }

        return entries.OrderBy(e => e.At).ToList();
    }

    private static bool IsOffline(Sale sale) => sale.CompletedAtDevice is { } d && d + CashSessionSalesSummary.OfflineAfter < sale.ReceivedAt;

    private static string DeviceLabel(string type) => type switch
    {
        "SaleStarted" => "Venda começou",
        "LineAdded" => "Item lançado",
        "LineChanged" => "Item alterado",
        "LineRemoved" => "Item removido",
        "DiscountApplied" => "Desconto",
        "PriceOverridden" => "Troca de preço",
        "SaleParked" => "Venda em espera",
        "SaleResumed" => "Venda retomada",
        "SaleDiscarded" => "Venda descartada",
        "CustomerSet" => "Consumidor informado",
        "PaymentStarted" => "Foi para o pagamento",
        "PaymentCancelled" => "Voltou aos itens",
        "TenderCaptured" => "Pagamento registrado",
        "TenderChanged" => "Pagamento alterado",
        "SaleCompleted" => "Venda concluída no caixa",
        _ => type,
    };

    private static string ApprovalLabel(string action) => action switch
    {
        "Discount" => "desconto",
        "ChangePrice" => "troca de preço",
        "CancelItem" => "cancelamento de item",
        "CancelSale" => "cancelamento da venda",
        "Return" => "devolução",
        "CreditReissue" => "nova via do vale",
        _ => action,
    };

    private static string SyncLabel(string kind) => kind switch
    {
        "pos.sale" => "a venda",
        "pos.sale_cancel" => "o cancelamento",
        "pos.print" => "a impressão",
        _ => kind,
    };

    private static string OutcomeLabel(string outcome) => outcome switch
    {
        "AcceptedWithDivergence" => "aceito com divergência",
        "Quarantined" => "segurado na quarentena",
        _ => outcome,
    };

    private static string Brl(decimal value) => value.ToString("C", PtBr);

    private static string Local(DateTimeOffset at) => BusinessTime.ToBusiness(at).ToString("dd/MM/yyyy HH:mm:ss", PtBr);

    private async Task<bool> CanSeeCostAsync(CancellationToken cancellationToken) => await permissions.HasPermissionAsync(SaleService.Module, "view_cost", cancellationToken);

    private async Task<Dictionary<Guid, string>> MethodNamesAsync(CancellationToken cancellationToken) =>
        (await methods.ActiveAsync(BusinessTime.Today(clock), cancellationToken)).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Name);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, cancellationToken) ?? "Usuário removido";
        }

        return names;
    }

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } v ? names.GetValueOrDefault(v, "Usuário removido") : null;
}
