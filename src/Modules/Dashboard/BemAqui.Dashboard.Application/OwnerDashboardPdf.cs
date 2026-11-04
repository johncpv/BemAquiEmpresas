using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Fotografia do painel do proprietário (EXP-05 CA 10): os mesmos cards que o usuário vê na tela, na hora da geração. Bloco sem a permissão do
/// dono fica null (nem é lido), igual à tela. Leva a margem (só preenchida com <c>projects.view_cost</c>), por isso a marca [CostDto]; não
/// sai como JSON, só no PDF.
/// </summary>
[CostDto]
public sealed record OwnerDashboardSnapshot(
    DateTimeOffset GeneratedAt, BalancesCard? Balances, CashForecastCard? CashForecast, RevenueCard? Revenue, OverdueCard? Overdue, CommercialCard? Commercial,
    DecisionsCard? Decisions, MarginOverview? Margin, ActiveProjectsCard? ActiveProjects, ProductionCard? Production, BacklogCard? Backlog, DeliveriesCard? Deliveries)
{
    /// <summary>Os títulos dos blocos presentes, na ordem do PDF (para a auditoria e os testes).</summary>
    public IReadOnlyList<string> Blocks => new (object? Card, string Title)[]
    {
        (Balances, OwnerDashboardPdf.BalancesTitle), (CashForecast, OwnerDashboardPdf.CashTitle), (Revenue, OwnerDashboardPdf.RevenueTitle), (Overdue, OwnerDashboardPdf.OverdueTitle),
        (Margin, OwnerDashboardPdf.MarginTitle), (Decisions, OwnerDashboardPdf.DecisionsTitle), (ActiveProjects, OwnerDashboardPdf.ActiveTitle), (Production, OwnerDashboardPdf.ProductionTitle),
        (Backlog, OwnerDashboardPdf.BacklogTitle), (Deliveries, OwnerDashboardPdf.DeliveriesTitle), (Commercial, OwnerDashboardPdf.CommercialTitle),
    }.Where(b => b.Card is not null).Select(b => b.Title).ToList();
}

/// <summary>
/// "Baixar PDF" do painel do proprietário (EXP-05 CA 10): lê só os cards visíveis (<see cref="DashboardService.VisibleCardsAsync"/>, o mesmo
/// critério da tela) pelos mesmos métodos dos endpoints dos cards, e monta a fotografia com tabelas no lugar dos gráficos.
/// </summary>
public sealed class OwnerDashboardPdfService(DashboardService dashboard, OwnerProjectsService owner, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "OwnerDashboard";

    public async Task<OwnerDashboardSnapshot> SnapshotAsync(CancellationToken ct = default)
    {
        var visible = await dashboard.VisibleCardsAsync(ct);
        bool Sees(string card) => visible.Contains(card, StringComparer.Ordinal);
        MarginOverview? margin = null;
        if (Sees(DashboardCards.Margin) && await owner.MarginAsync(ct) is { IsSuccess: true } m)
        {
            margin = m.Value;
        }

        return new OwnerDashboardSnapshot(
            clock.GetUtcNow(),
            Sees(DashboardCards.Balances) ? await dashboard.BalancesAsync(ct) : null,
            Sees(DashboardCards.CashForecast) ? await dashboard.CashForecastAsync(ct) : null,
            Sees(DashboardCards.Revenue) ? await dashboard.RevenueAsync(ct) : null,
            Sees(DashboardCards.Overdue) ? await dashboard.OverdueAsync(ct) : null,
            Sees(DashboardCards.Commercial) ? await dashboard.CommercialAsync(ct) : null,
            Sees(DashboardCards.Decisions) ? await dashboard.DecisionsAsync(ct) : null,
            margin,
            Sees(DashboardCards.ActiveProjects) ? await owner.ActiveProjectsAsync(ct) : null,
            Sees(DashboardCards.Production) ? await owner.ProductionAsync(ct) : null,
            Sees(DashboardCards.Backlog) ? await owner.BacklogAsync(ct) : null,
            Sees(DashboardCards.Deliveries) ? await owner.DeliveriesAsync(ct) : null);
    }

    public async Task<(byte[] Content, string FileName)> PdfAsync(CancellationToken ct = default)
    {
        var snapshot = await SnapshotAsync(ct);
        var frame = await frames.CreateAsync(ct);
        var bytes = await renderer.RenderAsync(new OwnerDashboardPdfDocument(frame, snapshot), ct);
        await audit.RecordExportAsync(AuditEntity, "snapshot", "pdf", snapshot.Blocks.Count, ct);
        var day = BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(snapshot.GeneratedAt);
        return (bytes, $"painel-{day:yyyy-MM-dd}.pdf");
    }
}

/// <summary>Textos da fotografia do painel: os mesmos títulos dos cards da tela.</summary>
public static class OwnerDashboardPdf
{
    public const string BalancesTitle = "Saldo em contas";
    public const string CashTitle = "Caixa projetado";
    public const string RevenueTitle = "Faturamento";
    public const string OverdueTitle = "A receber vencido";
    public const string MarginTitle = "Margem";
    public const string DecisionsTitle = "Aguardando sua decisão";
    public const string ActiveTitle = "Projetos em andamento";
    public const string ProductionTitle = "Produção";
    public const string BacklogTitle = "Carteira";
    public const string DeliveriesTitle = "Entregas do mês";
    public const string CommercialTitle = "Comercial";

    /// <summary>"Fotografia de {data hora}: os números mudam com novos lançamentos."</summary>
    public static string Note(DateTimeOffset at) => $"Fotografia de {PdfTheme.DateTime(at)}: os números mudam com novos lançamentos.";

    public static string Money(decimal? value) => value is { } v ? PdfTheme.Money(v) : "—";

    public static string Percent(decimal? value) => value is { } v ? PdfTheme.Percent(v) : "—";

    public static string Date(DateOnly? value) => value is { } v ? PdfTheme.Date(v) : "—";

    public static string Risk(DeliveryRisk risk) => risk switch
    {
        DeliveryRisk.Late => "Atrasado",
        DeliveryRisk.AtRisk => "Em risco",
        _ => "No prazo",
    };

    public static string Band(FactoryLoadBand band) => band switch
    {
        FactoryLoadBand.NoCapacity => "Sem capacidade",
        FactoryLoadBand.Idle => "Ociosa",
        FactoryLoadBand.Normal => "Normal",
        FactoryLoadBand.Full => "Cheia",
        FactoryLoadBand.Over => "Acima da capacidade",
        var other => other.ToString(),
    };
}

/// <summary>
/// Fotografia do painel do proprietário no modelo único (EXP-05 CA 10): um bloco por card visível, com os números da tela em tabelas (sem
/// gráficos) e a nota com a data e a hora da geração. Sem marca.
/// </summary>
public sealed class OwnerDashboardPdfDocument : PdfDocumentFrame
{
    private readonly OwnerDashboardSnapshot _snapshot;

    public OwnerDashboardPdfDocument(PdfFrame frame, OwnerDashboardSnapshot snapshot)
        : base(frame, Header(snapshot)) => _snapshot = snapshot;

    private static PdfDocumentHeader Header(OwnerDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PdfDocumentHeader("Painel do proprietário", Date: DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(snapshot.GeneratedAt).DateTime));
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var s = _snapshot;
        col.Spacing(8);
        col.Item().Text(OwnerDashboardPdf.Note(s.GeneratedAt)).FontColor(PdfTheme.Muted);
        if (s.Blocks.Count == 0)
        {
            col.Item().Text("Você ainda não tem acesso aos indicadores. Fale com o administrador.");
            return;
        }

        if (s.Balances is { } b)
        {
            Block(col, OwnerDashboardPdf.BalancesTitle);
            col.Item().Element(c => Table(c, ["Conta", "Saldo"], [.. b.Accounts.Select(a => new[] { a.Name, PdfTheme.Money(a.Balance) }), ["Consolidado", PdfTheme.Money(b.Consolidated)]]));
            if (b.OpeningMissing)
            {
                col.Item().Text("Há conta sem saldo inicial: o consolidado pode estar incompleto.").FontColor(PdfTheme.WarnInk);
            }
        }

        if (s.CashForecast is { } cash)
        {
            Block(col, OwnerDashboardPdf.CashTitle);
            col.Item().Element(c => Fields(c, 4,
                ("Saldo hoje", OwnerDashboardPdf.Money(cash.CurrentBalance)),
                ("Menor saldo (base)", cash.LowestBase is { } lb ? $"{PdfTheme.Money(lb.Balance)} em {PdfTheme.Date(lb.Date)}" : null),
                ("Menor saldo (ajustado)", cash.LowestAdjusted is { } la ? $"{PdfTheme.Money(la.Balance)} em {PdfTheme.Date(la.Date)}" : null),
                ("Diferença pelos atrasos", OwnerDashboardPdf.Money(cash.Difference))));
            col.Item().Element(c => Table(c, ["Horizonte", "Base", "Ajustado"], cash.Horizons.Select(h => new[] { $"{h.Days} dias", PdfTheme.Money(h.BaseBalance), PdfTheme.Money(h.AdjustedBalance) }).ToList()));
            col.Item().Element(c => Table(c, ["Semana até", "Base", "Ajustado"], cash.Weeks.Select(w => new[] { PdfTheme.Date(w.WeekEnd), PdfTheme.Money(w.BaseBalance), PdfTheme.Money(w.AdjustedBalance) }).ToList()));
        }

        if (s.Revenue is { } r)
        {
            Block(col, OwnerDashboardPdf.RevenueTitle);
            col.Item().Element(c => Table(c, ["Período", "Faturado", "Meta", "% da meta"],
            [
                [$"Mês ({r.Month:00}/{r.Year})", OwnerDashboardPdf.Money(r.MonthAmount), OwnerDashboardPdf.Money(r.MonthTarget), OwnerDashboardPdf.Percent(r.MonthPct)],
                [$"Ano ({r.Year})", OwnerDashboardPdf.Money(r.YearAmount), OwnerDashboardPdf.Money(r.YearTarget), OwnerDashboardPdf.Percent(r.YearPct)],
            ]));
            col.Item().Text($"Dias úteis que faltam no mês: {r.WorkingDaysLeft}" + (r.Provisional ? " · Faturamento provisório (antes das notas fiscais)." : string.Empty)).FontColor(PdfTheme.Muted);
        }

        if (s.Overdue is { } o)
        {
            Block(col, OwnerDashboardPdf.OverdueTitle);
            col.Item().Element(c => Fields(c, 4, ("Total vencido", PdfTheme.Money(o.Total)), ("Títulos", o.Count.ToString(PdfTheme.PtBr)), ("Clientes", o.Customers.ToString(PdfTheme.PtBr)),
                ("Maior atraso", $"{o.MaxDaysLate} dias")));
            col.Item().Element(c => Table(c, ["Faixa", "Títulos", "Total"], o.Buckets.Select(x => new[] { x.Label, x.Count.ToString(PdfTheme.PtBr), PdfTheme.Money(x.Total) }).ToList()));
            col.Item().Element(c => Table(c, ["Maiores clientes", "Total", "Maior atraso"], o.TopCustomers.Select(x => new[] { x.PartyName, PdfTheme.Money(x.Total), $"{x.MaxDaysLate} dias" }).ToList()));
        }

        if (s.Margin is { } m)
        {
            Block(col, OwnerDashboardPdf.MarginTitle);
            col.Item().Element(c => Table(c, ["Projetos", "Quantos", "Orçada", "Projetada/realizada", "Desvio (pp)"],
            [
                ["Em andamento", m.ActiveCount.ToString(PdfTheme.PtBr), OwnerDashboardPdf.Percent(m.BudgetedMarginPct), OwnerDashboardPdf.Percent(m.ProjectedMarginPct), Pp(m.DeviationPp)],
                [$"Entregues em {m.Year}", m.FinishedCount.ToString(PdfTheme.PtBr), OwnerDashboardPdf.Percent(m.FinishedBudgetedMarginPct), OwnerDashboardPdf.Percent(m.FinishedRealizedMarginPct), Pp(m.FinishedDeviationPp)],
            ]));
            if (m.Worst.Count > 0)
            {
                col.Item().Element(c => Table(c, ["Piores pelo desvio", "Cliente", "Orçada", "Projetada", "Desvio (pp)"],
                    m.Worst.Select(w => new[] { $"{w.Number} {w.Name}", w.PartyName, OwnerDashboardPdf.Percent(w.BudgetedMarginPct), OwnerDashboardPdf.Percent(w.ProjectedMarginPct), Pp(w.DeviationPp) }).ToList()));
            }
        }

        if (s.Decisions is { } d)
        {
            Block(col, OwnerDashboardPdf.DecisionsTitle);
            col.Item().Element(c => Table(c, ["Decisão", "Detalhe", "Valor", "Prazo"],
                d.Items.Select(i => new[] { i.Count > 1 ? $"{i.Title} ({i.Count})" : i.Title, i.Detail ?? "—", OwnerDashboardPdf.Money(i.Amount), OwnerDashboardPdf.Date(i.DueDate) }).ToList()));
        }

        if (s.ActiveProjects is { } a)
        {
            Block(col, OwnerDashboardPdf.ActiveTitle);
            col.Item().Text($"{a.Total} projetos em andamento; os {a.Items.Count} mais urgentes:").FontColor(PdfTheme.Muted);
            col.Item().Element(c => Table(c, ["Projeto", "Cliente", "Prometida", "Prevista", "Situação", "Contratado"],
                a.Items.Select(p => new[] { $"{p.Number} {p.Name}", p.PartyName, OwnerDashboardPdf.Date(p.PromisedDate), OwnerDashboardPdf.Date(p.ForecastDate), OwnerDashboardPdf.Risk(p.Risk), PdfTheme.Money(p.ContractedAmount) }).ToList()));
        }

        if (s.Production is { } p)
        {
            Block(col, OwnerDashboardPdf.ProductionTitle);
            col.Item().Element(c => Fields(c, 4, ("Projetos em andamento", p.ActiveProjects.ToString(PdfTheme.PtBr)), ("OS em aberto", p.OpenOrders.ToString(PdfTheme.PtBr)),
                ("OS atrasadas", p.LateOrders.ToString(PdfTheme.PtBr)), ("OS em risco", p.AtRiskOrders.ToString(PdfTheme.PtBr))));
            col.Item().Element(c => Table(c, ["Semana", "Horas comprometidas", "Capacidade", "Ocupação", "Faixa"],
                p.Weeks.Select(w => new[] { $"{PdfTheme.Date(w.Start)} a {PdfTheme.Date(w.End)}", PdfTheme.Quantity(w.CommittedHours), PdfTheme.Quantity(w.CapacityHours), OwnerDashboardPdf.Percent(w.Percent), OwnerDashboardPdf.Band(w.Band) }).ToList()));
            if (p.BusyUntil is { } busy)
            {
                col.Item().Text("Fábrica ocupada até " + PdfTheme.Date(busy)).FontColor(PdfTheme.Muted);
            }
        }

        if (s.Backlog is { } k)
        {
            Block(col, OwnerDashboardPdf.BacklogTitle);
            col.Item().Element(c => Fields(c, 2, ("A produzir", $"{PdfTheme.Money(k.ToProduce)} ({k.ToProduceCount} projetos)"),
                ("A faturar", k.ToInvoice is { } inv ? $"{PdfTheme.Money(inv)} ({k.ToInvoiceCount} contratos)" + (k.Provisional ? " · provisório" : string.Empty) : null)));
        }

        if (s.Deliveries is { } e)
        {
            Block(col, OwnerDashboardPdf.DeliveriesTitle);
            col.Item().Element(c => Fields(c, 4, ("Previstas no mês", e.ForecastInMonth.ToString(PdfTheme.PtBr)), ("Entregues no mês", e.DeliveredInMonth.ToString(PdfTheme.PtBr)),
                ("Em risco", e.AtRisk.ToString(PdfTheme.PtBr)), ("Atrasadas", e.Late.ToString(PdfTheme.PtBr))));
            if (e.MostDelayed is { } late)
            {
                col.Item().Text($"Mais atrasado: {late.Number} {late.Name}, {late.DelayDays} dias depois do prometido.");
            }
        }

        if (s.Commercial is { } cm)
        {
            Block(col, OwnerDashboardPdf.CommercialTitle);
            col.Item().Element(c => Fields(c, 4, ("Em aberto", $"{PdfTheme.Money(cm.OpenAmount)} ({cm.OpenCount})"), ("Conversão (quantidade)", OwnerDashboardPdf.Percent(cm.ConversionByCount)),
                ("Conversão (valor)", OwnerDashboardPdf.Percent(cm.ConversionByAmount)), ("Vencendo em 7 dias", $"{PdfTheme.Money(cm.ExpiringAmount)} ({cm.ExpiringCount})")));
            col.Item().Element(c => Table(c, ["Etapa do funil", "Orçamentos", "Valor"], cm.Stages.Select(x => new[] { x.Situation, x.Count.ToString(PdfTheme.PtBr), PdfTheme.Money(x.Amount) }).ToList()));
        }
    });

    private static string Pp(decimal? value) => value is { } v ? v.ToString("+0.0;-0.0;0.0", PdfTheme.PtBr) : "—";

    private static void Block(ColumnDescriptor col, string title) => col.Item().Element(c => Section(c, title));

    /// <summary>Tabela simples: primeira coluna em texto e as demais à direita (números).</summary>
    private static void Table(IContainer c, string[] headers, List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            c.Text("Nada no momento.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                for (var i = 1; i < headers.Length; i++)
                {
                    cols.RelativeColumn(2);
                }
            });
            table.Header(h =>
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = HeaderCell(h.Cell());
                    (i > 0 ? cell.AlignRight() : cell).Text(headers[i]).SemiBold();
                }
            });
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = BodyCell(table.Cell());
                    (i > 0 ? cell.AlignRight() : cell).Text(i < row.Length ? row[i] : string.Empty);
                }
            }
        });
    }
}
