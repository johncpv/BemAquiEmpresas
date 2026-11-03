using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Application.Reconciliation;
using BemAqui.Finance.Application.Statements;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application.Exports;

/// <summary>Linha do extrato exportado (EXP-02 CA 7): a de partida e a final só com o rótulo e o saldo, em negrito.</summary>
public sealed record StatementExportRow(
    DateOnly? Date, string Description, string? Type, string? Origin, decimal? Inflow, decimal? Outflow, decimal Balance, string? WithoutOrder, string Situation, bool IsBalance);

/// <summary>Linha da aba "Resumo" e dos destaques (conciliação e fluxo): o item, o valor e a data, como a tela mostra.</summary>
public sealed record ExportSummaryRow(string Item, decimal? Value, DateOnly? Date = null, string? Note = null);

/// <summary>Linha das ordens de pagamento exportadas (EXP-02 CA 12); a última é o total do filtro.</summary>
public sealed record PaymentOrderExportRow(
    string Number, string? Situation, DateOnly? ExpectedDate, decimal Total, decimal? Items, string? Requester, DateTimeOffset? SubmittedAt, string? SelfApproved, bool IsTotal);

/// <summary>Linha do a receber exportado (EXP-02 CA 13); as duas últimas são "Em aberto" e "Vencido", iguais ao rodapé da tela.</summary>
public sealed record ReceivableExportRow(
    string Number, string? Customer, string? Project, string? Description, DateOnly? IssueDate, object? Due, decimal? Amount, decimal? Received, decimal Balance,
    string? Situation, string? BillingSlip, string? Opening, bool IsTotal);

/// <summary>Período do fluxo exportado (EXP-02 CA 17): base e ajustado lado a lado.</summary>
public sealed record CashFlowExportRow(
    DateOnly Start, DateOnly End, string Kind, decimal Inflow, decimal Outflow, decimal OverdueInflow, decimal OverdueOutflow, decimal Balance, decimal? AdjustedInflow,
    decimal? AdjustedOutflow, decimal? AdjustedOverdueInflow, decimal? AdjustedOverdueOutflow, decimal? AdjustedBalance);

/// <summary>Linha da inadimplência exportada (EXP-02 CA 20); a última é o total, igual aos cartões.</summary>
public sealed record AgingExportRow(
    string Name, string? Customer, IReadOnlyList<decimal> Buckets, decimal Total, int? MaxDaysLate, int? Count, decimal? AvgDelayDays, decimal? PctPaidLate, bool IsTotal);

/// <summary>
/// Colunas, rótulos e linhas das exportações do financeiro (EXP-02, ADR 0045): cada uma monta o <see cref="ExportTable"/> a partir do resultado
/// do mesmo serviço da tela (mesma consulta, mesma máscara), com os textos da tela. Sem SQL e sem regra nova de máscara.
/// </summary>
public static class FinanceExports
{
    public const string Yes = "Sim";
    public const string No = "Não";
    public const string NoHistory = "Sem histórico";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Months = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    public static string YesNo(bool value) => value ? Yes : No;

    public static string Date(DateOnly? date) => date?.ToString("dd/MM/yyyy", PtBr) ?? string.Empty;

    /// <summary>"mar/26", como o cabeçalho do DRE na tela.</summary>
    public static string MonthLabel(DateOnly month) => $"{Months[month.Month - 1]}/{month.Year % 100:00}";

    // ---------- Extrato da conta (FIN-04) ----------

    public static readonly ExportSpec<StatementExportRow> Statement = new ExportSpec<StatementExportRow>("BankStatement", "extrato", "Extrato da conta")
        .Date("Data", r => r.Date)
        .Text("Descrição", r => r.Description)
        .Text("Tipo", r => r.Type).NotInPdf()
        .Text("Origem", r => r.Origin)
        .Money("Entrada", r => r.Inflow)
        .Money("Saída", r => r.Outflow)
        .Money("Saldo", r => r.Balance)
        .Text("Sem OP", r => r.WithoutOrder).NotInPdf()
        .Text("Situação", r => r.Situation)
        .Style(r => r.IsBalance ? new ExportRowStyle(Bold: true) : null);

    public static string TransactionTypeLabel(string type) => type switch
    {
        nameof(TransactionType.Receipt) => "Recebimento",
        nameof(TransactionType.Payment) => "Pagamento",
        nameof(TransactionType.Transfer) => "Transferência",
        nameof(TransactionType.BankEntry) => "Lançamento sem OP",
        _ => type,
    };

    /// <summary>Saldo de partida (o rótulo da tela), os movimentos com o saldo depois de cada um e o saldo final; estornos marcados na "Situação".</summary>
    public static IReadOnlyList<StatementExportRow> StatementRows(StatementResult statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var rows = new List<StatementExportRow> { new(statement.From, statement.StartLabel, null, null, null, null, statement.StartBalance, null, "—", true) };
        rows.AddRange(statement.Lines.Items.Select(l => new StatementExportRow(
            l.Date, l.Description, TransactionTypeLabel(l.Type), l.Origin, l.Amount > 0 ? l.Amount : null, l.Amount < 0 ? -l.Amount : null, l.Balance, YesNo(l.WithoutOrder),
            l.Reversed ? "Estornado" : l.IsReversal ? "Estorno" : "—", false)));
        rows.Add(new StatementExportRow(statement.To, "Saldo final", null, null, null, null, statement.EndBalance, null, "—", true));
        return rows;
    }

    // ---------- Extrato do banco (BNK-03a) ----------

    public static readonly ExportSpec<BankStatementLineView> BankStatementLines = new ExportSpec<BankStatementLineView>("BankStatementLine", "extrato-banco", "Extrato do banco")
        .Date("Data", r => r.Date)
        .Text("Descrição", r => r.Description)
        .Text("Tipo", r => LineKindLabel(r.Kind))
        .Text("Contraparte", r => r.CounterpartyName)
        .Text("CPF/CNPJ da contraparte", r => r.CounterpartyDocument)
        .Money("Valor", r => r.Amount)
        .Text("Origem", r => SourceLabel(r.Source))
        .Text("Verificada pelo banco", r => YesNo(r.BankVerified)).NotInPdf()
        .Text("Situação", r => LineStatusLabel(r.Status))
        .Text("Motivo de ignorar", r => r.IgnoreReason);

    public static string LineKindLabel(string kind) => kind switch
    {
        "Pix" => "Pix",
        "Boleto" => "Boleto",
        "Ted" => "TED",
        "Doc" => "DOC",
        "Transfer" => "Transferência",
        "Fee" => "Tarifa",
        "Interest" => "Juros",
        "Yield" => "Rendimento",
        "Tax" => "Imposto",
        "Card" => "Cartão",
        "Check" => "Cheque",
        "Deposit" => "Depósito",
        "Reversal" => "Estorno",
        _ => "Outro",
    };

    public static string SourceLabel(string source) => source == nameof(StatementSource.Api) ? "API" : source == nameof(StatementSource.Ofx) ? "OFX" : source;

    public static string LineStatusLabel(string status) => status switch
    {
        nameof(StatementLineStatus.Pending) => "Pendente",
        nameof(StatementLineStatus.Reconciled) => "Conciliada",
        nameof(StatementLineStatus.Ignored) => "Ignorada",
        nameof(StatementLineStatus.Undone) => "Desfeita",
        _ => status,
    };

    /// <summary>Filtro "situacao" do extrato do banco → rótulo da tela.</summary>
    public static string? LineStatusFilter(string? status) => string.IsNullOrWhiteSpace(status) ? null : status switch
    {
        "Pending" => "Pendentes",
        "Reconciled" => "Conciliadas",
        "Ignored" => "Ignoradas",
        _ => status,
    };

    // ---------- Conciliação (BNK-03b) ----------

    public static readonly ExportSpec<ReconDone> ReconciledSpec = new ExportSpec<ReconDone>("BankReconciliation", "conciliacao", "Relatório de conciliação")
        .Date("Data", d => d.Lines.Count > 0 ? d.Lines.Min(l => l.Date) : null)
        .Text("Linha do banco", d => string.Join(" + ", d.Lines.Select(l => l.Description)))
        .Money("Valor no banco", d => d.Lines.Sum(l => l.Amount))
        .Text("Transação do sistema", d => string.Join(" + ", d.Transactions.Select(TransactionText)))
        .Money("Valor no sistema", d => d.Transactions.Sum(t => t.Amount))
        .Text("Motivo", d => d.Reason ?? ReconKindLabel(d.Kind))
        .Text("Confiança", d => ConfidenceLabel(d.Confidence))
        .Text("Conciliado por", d => d.CreatedByName ?? "o sistema")
        .DateTime("Conciliado em", d => d.CreatedAt);

    public static readonly ExportSpec<ReconPair> SuggestionsSpec = new ExportSpec<ReconPair>("BankReconciliation", "conciliacao", "Sugestões")
        .Date("Data", p => p.Line.Date)
        .Text("Linha do banco", p => p.Line.Description)
        .Money("Valor no banco", p => p.Line.Amount)
        .Text("Transação do sistema", p => TransactionText(p.Transaction))
        .Money("Valor no sistema", p => p.Transaction.Amount)
        .Text("Motivo", p => p.Reason)
        .Text("Confiança", p => ConfidenceLabel(p.Confidence));

    public static readonly ExportSpec<ReconUnpairedLine> BankOnlySpec = new ExportSpec<ReconUnpairedLine>("BankReconciliation", "conciliacao", "Sem par no banco")
        .Date("Data", u => u.Line.Date)
        .Text("Descrição", u => u.Line.Description)
        .Text("Tipo", u => LineKindLabel(u.Line.Kind.ToString()))
        .Money("Valor", u => u.Line.Amount)
        .Text("Contraparte", u => Counterparty(u.Line));

    public static readonly ExportSpec<ReconUnpairedTransaction> SystemOnlySpec = new ExportSpec<ReconUnpairedTransaction>("BankReconciliation", "conciliacao", "No sistema, não no banco")
        .Date("Data", u => u.Transaction.Date)
        .Text("Descrição", u => u.Transaction.Description)
        .Text("Origem", u => u.Transaction.Origin)
        .Money("Valor", u => u.Transaction.Amount)
        .Text("Há mais de 5 dias", u => YesNo(u.Stale));

    public static readonly ExportSpec<ExportSummaryRow> SummarySpec = new ExportSpec<ExportSummaryRow>("BankReconciliation", "conciliacao", "Resumo")
        .Text("Item", r => r.Item)
        .Money("Valor", r => r.Value)
        .Date("Data", r => r.Date)
        .Text("Observação", r => r.Note);

    /// <summary>Período não fechado (<c>ClosedUntil</c> antes de "Até" ou nulo): o PDF leva a marca "Prévia" (EXP-02 CA 11).</summary>
    public static bool IsPreview(ReconciliationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.ClosedUntil is not { } closed || closed < view.To;
    }

    public static IReadOnlyList<ExportSummaryRow> ReconciliationSummary(ReconciliationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var t = view.Totals;
        return
        [
            new("Saldo do banco", t.BankBalance, t.BalanceDate, t.BalanceDate is null ? "Sem saldo do banco no período" : null),
            new("Saldo do sistema", t.SystemBalance, t.BalanceDate),
            new("Diferença", t.Difference, t.BalanceDate),
            new("Pendentes", t.Pending),
            new("Conciliadas", t.Reconciled),
            new("Ignoradas", t.Ignored),
            new("Conciliada até", null, view.ReconciledUntil),
            new("Fechada até", null, view.ClosedUntil, IsPreview(view) ? "Período ainda não fechado: os números podem mudar." : null),
        ];
    }

    /// <summary>As abas da conciliação na ordem da tela (a "Conciliados" é a principal): Sugestões, Sem par no banco, No sistema e Resumo.</summary>
    public static IReadOnlyList<ExportSheet> ReconciliationSheets(ReconciliationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return
        [
            ExportSheet.From("Sugestões", SuggestionsSpec, view.Pairs),
            ExportSheet.From("Sem par no banco", BankOnlySpec, view.BankOnly),
            ExportSheet.From("No sistema, não no banco", SystemOnlySpec, view.SystemOnly),
            ExportSheet.From("Resumo", SummarySpec, ReconciliationSummary(view)),
        ];
    }

    private static string TransactionText(ReconTransaction t) => t.Origin is { Length: > 0 } origin ? $"{origin}: {t.Description}" : t.Description;

    /// <summary>A contraparte como a tela mostra (já mascarada pelo serviço sem <c>finance.edit</c>).</summary>
    private static string? Counterparty(ReconLine l) => (l.CounterpartyName, l.CounterpartyDocument) switch
    {
        (null, null) => null,
        ({ } name, null) => name,
        (null, { } doc) => doc,
        var (name, doc) => $"{name} ({doc})",
    };

    public static string ReconKindLabel(ReconciliationKind kind) => kind switch
    {
        ReconciliationKind.Suggested => "Sugestão aceita",
        ReconciliationKind.Manual => "Escolhida à mão",
        ReconciliationKind.BankEntry => "Lançamento sem OP",
        ReconciliationKind.Settlement => "Baixa de título",
        _ => kind.ToString(),
    };

    public static string? ConfidenceLabel(ReconciliationConfidence? confidence) => confidence switch
    {
        ReconciliationConfidence.High => "Alta",
        ReconciliationConfidence.Medium => "Média",
        _ => null,
    };

    // ---------- Ordens de pagamento (FIN-03a) ----------

    public static readonly ExportSpec<PaymentOrderExportRow> PaymentOrders = new ExportSpec<PaymentOrderExportRow>("PaymentOrder", "ordens-de-pagamento", "Ordens de pagamento")
        .Text("Número", r => r.Number)
        .Text("Situação", r => r.Situation)
        .Date("Data prevista", r => r.ExpectedDate)
        .Money("Total", r => r.Total)
        .Quantity("Títulos", r => r.Items)
        .Text("Solicitante", r => r.Requester)
        .DateTime("Enviada em", r => r.SubmittedAt)
        .Text("Autoaprovada", r => r.SelfApproved)
        .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    public static string OrderStatusLabel(PaymentOrderStatus status) => status switch
    {
        PaymentOrderStatus.Pending => "Aguardando aprovação",
        PaymentOrderStatus.Approved => "Aprovada",
        PaymentOrderStatus.Rejected => "Reprovada",
        PaymentOrderStatus.Paid => "Paga",
        PaymentOrderStatus.Cancelled => "Cancelada",
        PaymentOrderStatus.Reversed => "Estornada",
        _ => status.ToString(),
    };

    /// <summary>Filtro "situacao" das ordens → rótulo da tela.</summary>
    public static readonly IReadOnlyDictionary<string, string> OrderSituations = new Dictionary<string, string>
    {
        ["aguardando"] = "Aguardando aprovação",
        ["aprovada"] = "Aprovadas",
        ["paga"] = "Pagas",
        ["reprovada"] = "Reprovadas",
        ["cancelada"] = "Canceladas ou estornadas",
    };

    public static IReadOnlyList<PaymentOrderExportRow> PaymentOrderRows(IReadOnlyList<PaymentOrderSummary> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var rows = orders.Select(o => new PaymentOrderExportRow(o.Number, OrderStatusLabel(o.Status), o.ExpectedDate, o.Total, o.Items, o.RequesterName, o.SubmittedAt, YesNo(o.SelfApproved), false)).ToList();
        rows.Add(new PaymentOrderExportRow("Total", null, null, orders.Sum(o => o.Total), orders.Sum(o => o.Items), null, null, null, true));
        return rows;
    }

    // ---------- A receber (FIN-01) ----------

    public static readonly ExportSpec<ReceivableExportRow> Receivables = new ExportSpec<ReceivableExportRow>("Receivable", "contas-a-receber", "Contas a receber")
        .Text("Número", r => r.Number)
        .Text("Cliente", r => r.Customer)
        .Text("Contrato/projeto", r => r.Project).NotInPdf()
        .Text("Parcela/marco", r => r.Description)
        .Date("Emissão", r => r.IssueDate).NotInPdf()
        .Mixed("Vencimento", ExportColumnType.Date, r => r.Due)
        .Money("Valor", r => r.Amount)
        .Money("Recebido", r => r.Received).NotInPdf()
        .Money("Saldo", r => r.Balance)
        .Text("Situação", r => r.Situation)
        .Text("Boleto", r => r.BillingSlip).NotInPdf()
        .Text("Implantação", r => r.Opening).NotInPdf()
        .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    /// <summary>Filtro "situacao" do a receber → rótulo da tela.</summary>
    public static readonly IReadOnlyDictionary<string, string> ReceivableSituations = new Dictionary<string, string>
    {
        ["aberto"] = "Em aberto",
        ["vencido"] = "Vencidos",
        ["previsto"] = "Previstos",
        ["pago"] = "Pagos",
        ["cancelado"] = "Cancelados",
    };

    /// <summary>Situação igual ao chip da tela (<c>situation</c> de <c>receivables.ts</c>).</summary>
    public static string ReceivableSituation(ReceivableDetail r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.IsOverdue)
        {
            return "Vencido";
        }

        if (r.IsDueToday)
        {
            return "Vence hoje";
        }

        return r.Status switch
        {
            ReceivableStatus.Planned => "Previsto",
            ReceivableStatus.PartiallyPaid => "Parcial",
            ReceivableStatus.Paid => "Pago",
            ReceivableStatus.Cancelled => "Cancelado",
            _ => "Em aberto",
        };
    }

    /// <summary>"Aguarda: fabricação concluída" na parcela prevista do contrato (COM-04b), como a tela.</summary>
    public static string? Awaits(ReceivableDetail r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Status != ReceivableStatus.Planned || r.TriggerEvent is not { } trigger)
        {
            return null;
        }

        var label = trigger.ToString() switch
        {
            "FabricationDone" => "fabricação concluída",
            "Delivered" => "entrega",
            "TechnicalAcceptance" => "aceite técnico",
            "Signature" => "assinatura",
            var other => other,
        };
        return "Aguarda: " + label;
    }

    public static string? SlipLabel(BillingSlipStatus? status) => status switch
    {
        null => null,
        BillingSlipStatus.Requested => "Boleto em emissão",
        BillingSlipStatus.Issued => "Boleto emitido",
        BillingSlipStatus.Failed => "Emissão recusada",
        BillingSlipStatus.Paid => "Boleto pago",
        BillingSlipStatus.Cancelled => "Boleto cancelado",
        BillingSlipStatus.Expired => "Boleto expirado",
        BillingSlipStatus.PaidPendingReview => "Aguardando conferência",
        var other => other.ToString(),
    };

    public static ReceivableExportRow ReceivableRow(ReceivableDetail r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new ReceivableExportRow(r.Number, r.PartyName, r.ProjectNumber, r.Description, r.IssueDate, (object?)r.DueDate ?? Awaits(r), r.Amount, r.PaidAmount, r.Balance,
            ReceivableSituation(r), SlipLabel(r.BillingSlipStatus), YesNo(r.Origin == BemAqui.SharedKernel.DocumentOrigin.Opening), false);
    }

    /// <summary>Os títulos do filtro e as linhas "Em aberto" e "Vencido", iguais ao rodapé da tela.</summary>
    public static IReadOnlyList<ReceivableExportRow> ReceivableRows(IReadOnlyList<ReceivableDetail> items, ReceivableTotals totals)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(totals);
        var rows = items.Select(ReceivableRow).ToList();
        rows.Add(new ReceivableExportRow("Em aberto", null, null, null, null, null, null, null, totals.Open, null, null, null, true));
        rows.Add(new ReceivableExportRow("Vencido", null, null, null, null, null, null, null, totals.Overdue, null, null, null, true));
        return rows;
    }

    // ---------- DRE (FIN-08) ----------

    public const string DreNote = "Gerencial, por competência; não substitui a contabilidade.";
    public const string DreProvisional = "Provisório: até a emissão de notas no sistema, a receita é a data de emissão dos títulos.";

    /// <summary>
    /// As colunas do DRE do período (CA 14): Código, Linha com recuo por nível, um mês por coluna (com "(provisório)"), Total, % da receita
    /// líquida e Δ (valor e %); subtotais e resultados em negrito; resultado negativo em vermelho.
    /// </summary>
    public static ExportSpec<DreRow> Dre(DreReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var spec = new ExportSpec<DreRow>("Dre", "dre", "DRE gerencial")
            .Text("Código", r => r.Code)
            .Text("Linha", r => r.Label).Indented();
        for (var i = 0; i < report.Months.Count; i++)
        {
            var index = i;
            var month = report.Months[i];
            spec.Money(MonthLabel(month.Month) + (month.IsProvisional ? " (provisório)" : string.Empty), r => index < r.Values.Count ? r.Values[index] : 0m);
        }

        return spec
            .Money("Total do período", r => r.Total)
            .Percent("% da receita líquida", r => r.PercentOfNetRevenue)
            .Money("Δ mês anterior", r => r.Delta)
            .Percent("Δ %", r => r.DeltaPercent)
            .Style(r => new ExportRowStyle(r.Level, r.Kind != DreRowKind.Account, r.CriticalWhenNegative));
    }

    public static readonly ExportSpec<DreClassifyNotice> DreNotices = new ExportSpec<DreClassifyNotice>("Dre", "dre", "Avisos")
        .Text("Código", n => n.Code)
        .Text("Conta a classificar", n => n.Name)
        .Money("Total no período", n => n.Total);

    // ---------- Fluxo de caixa (FIN-05, FIN-07) ----------

    /// <summary>
    /// Colunas do fluxo pelo cenário (CA 17): base, ajustado ou os dois lado a lado; as colunas do cenário que não está na tela saem. Sem o
    /// ajustado (conta filtrada ou cenário base), só a base.
    /// </summary>
    public static ExportSpec<CashFlowExportRow> CashFlow(string scenario, bool hasAdjusted)
    {
        var spec = new ExportSpec<CashFlowExportRow>("CashFlow", "fluxo-de-caixa", "Fluxo de caixa")
            .Date("Início", r => r.Start)
            .Date("Fim", r => r.End)
            .Text("Tipo", r => r.Kind);
        var onlyAdjusted = hasAdjusted && scenario == CashFlowService.ScenarioAdjusted;
        if (onlyAdjusted)
        {
            return spec
                .Money("Entradas ajustadas", r => r.AdjustedInflow)
                .Money("Saídas", r => r.AdjustedOutflow)
                .Money("Vencidos a receber", r => r.AdjustedOverdueInflow)
                .Money("Vencidos a pagar", r => r.AdjustedOverdueOutflow)
                .Money("Saldo ajustado", r => r.AdjustedBalance);
        }

        spec.Money("Entradas", r => r.Inflow)
            .Money("Saídas", r => r.Outflow)
            .Money("Vencidos a receber", r => r.OverdueInflow)
            .Money("Vencidos a pagar", r => r.OverdueOutflow)
            .Money("Saldo base", r => r.Balance);
        return hasAdjusted && scenario == CashFlowService.ScenarioBoth
            ? spec.Money("Entradas ajustadas", r => r.AdjustedInflow).Money("Saldo ajustado", r => r.AdjustedBalance)
            : spec;
    }

    public static IReadOnlyList<CashFlowExportRow> CashFlowRows(CashFlowResult flow, CashFlowAdjusted? adjusted)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return flow.Periods.Select((p, i) =>
        {
            var a = adjusted is not null && i < adjusted.Periods.Count ? adjusted.Periods[i] : null;
            var kind = p.ContainsToday ? "Hoje" : p.Projected ? "Projetado" : "Realizado";
            return new CashFlowExportRow(p.Start, p.End, kind, p.Inflow, p.Outflow, p.OverdueInflow, p.OverdueOutflow, p.Balance, a?.Inflow, a?.Outflow, a?.OverdueInflow,
                a?.OverdueOutflow, a?.Balance);
        }).ToList();
    }

    public static readonly ExportSpec<ExportSummaryRow> HighlightsSpec = new ExportSpec<ExportSummaryRow>("CashFlow", "fluxo-de-caixa", "Destaques")
        .Text("Destaque", r => r.Item)
        .Money("Valor", r => r.Value)
        .Date("Data", r => r.Date)
        .Text("Observação", r => r.Note);

    /// <summary>Os destaques da tela (CA 17): saldo atual, menor saldo e primeiro dia negativo de cada cenário, efeito dos atrasos e o sem data.</summary>
    public static IReadOnlyList<ExportSummaryRow> CashFlowHighlights(CashFlowResult flow, CashFlowAdjusted? adjusted)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var rows = new List<ExportSummaryRow> { new("Saldo atual", flow.CurrentBalance, flow.Today) };
        rows.Add(Point("Menor saldo previsto (base)", flow.Lowest));
        rows.Add(Point("Primeiro dia negativo (base)", flow.FirstNegative, "Não fica negativo no período"));
        if (adjusted is not null)
        {
            rows.Add(Point("Menor saldo previsto (ajustado)", adjusted.Lowest));
            rows.Add(Point("Primeiro dia negativo (ajustado)", adjusted.FirstNegative, "Não fica negativo no período"));
            rows.Add(new("Diferença causada por atrasos", adjusted.LowestDifference ?? 0));
            rows.Add(new("Risco de não receber no período", adjusted.AtRiskTotal, null, $"Limite de inadimplência: {adjusted.ThresholdPct.ToString("N2", PtBr)}%"));
            rows.AddRange(adjusted.AtRisk.Select(c => new ExportSummaryRow("Cliente acima do limite: " + (c.Name ?? "Cliente"), c.AtRisk, null,
                $"Inadimplência de {c.DefaultPct.ToString("N2", PtBr)}% · {(c.Count == 1 ? "1 título" : c.Count + " títulos")}")));
            rows.Add(new("Deslocado para depois de " + Date(flow.To), adjusted.ShiftedBeyond.Total, null, adjusted.ShiftedBeyond.Count == 1 ? "1 recebimento" : adjusted.ShiftedBeyond.Count + " recebimentos"));
        }

        rows.Add(new("A receber sem data (aguardando marco)", flow.UndatedReceivables.Total, null, flow.UndatedReceivables.Count == 1 ? "1 título" : flow.UndatedReceivables.Count + " títulos"));
        return rows;
    }

    /// <summary>Notas do topo do PDF (CA 18): os destaques numa linha cada e as regras da tela.</summary>
    public static IReadOnlyList<string> CashFlowNotes(CashFlowResult flow, CashFlowAdjusted? adjusted)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var notes = CashFlowHighlights(flow, adjusted)
            .Select(h => h.Item + ": " + (h.Value is { } v ? v.ToString("C2", PtBr) : string.Empty) + (h.Date is { } d ? " em " + Date(d) : string.Empty) + (h.Note is { } n ? $" ({n})" : string.Empty))
            .ToList();
        if (!flow.Consolidated)
        {
            notes.Add("Projeção só no consolidado.");
        }

        if (adjusted is { TenantHasHistory: false })
        {
            notes.Add("Ainda não há histórico de pagamento: o ajustado é igual à base.");
        }

        return notes;
    }

    private static ExportSummaryRow Point(string label, CashFlowPoint? point, string? missing = "Só no consolidado") =>
        point is null ? new ExportSummaryRow(label, null, null, missing) : new ExportSummaryRow(label, point.Balance, point.Date);

    public static string CashFlowViewLabel(string view) => view switch
    {
        "weekly" => "Semanal",
        "monthly" => "Mensal",
        _ => "Diária",
    };

    public static string ScenarioLabel(string scenario) => scenario switch
    {
        CashFlowService.ScenarioBase => "Base",
        CashFlowService.ScenarioAdjusted => "Ajustado",
        _ => "Os dois",
    };

    // ---------- Inadimplência (FIN-06a, FIN-06b) ----------

    private static readonly string[] BucketHeaders = ["Até 30", "31 a 60", "61 a 90", "Mais de 90"];

    public static readonly ExportSpec<AgingExportRow> AgingCustomers = Aging(new ExportSpec<AgingExportRow>("Aging", "inadimplencia-clientes", "Inadimplência por cliente")
            .Text("Cliente", r => r.Name))
        .Quantity("Maior atraso (dias)", r => r.MaxDaysLate)
        .Quantity("Títulos", r => r.Count)
        .Mixed("Atraso médio (dias)", ExportColumnType.Quantity, r => r.IsTotal ? null : r.AvgDelayDays is { } d ? (object)d : NoHistory)
        .Mixed("% pago com atraso", ExportColumnType.Percent, r => r.IsTotal ? null : r.PctPaidLate is { } p ? (object)p : NoHistory)
        .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    public static readonly ExportSpec<AgingExportRow> AgingProjects = Aging(new ExportSpec<AgingExportRow>("Aging", "inadimplencia-projetos", "Inadimplência por projeto")
            .Text("Projeto", r => r.Name)
            .Text("Cliente", r => r.Customer))
        .Quantity("Maior atraso (dias)", r => r.MaxDaysLate)
        .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    private static ExportSpec<AgingExportRow> Aging(ExportSpec<AgingExportRow> spec)
    {
        for (var i = 0; i < BucketHeaders.Length; i++)
        {
            var index = i;
            spec.Money(BucketHeaders[i], r => index < r.Buckets.Count ? r.Buckets[index] : 0m);
        }

        return spec.Money("Total vencido", r => r.Total);
    }

    /// <summary>Por cliente, do maior saldo para o menor (a ordem da tela), e a linha de total igual aos cartões; sem CPF/CNPJ (a tela não mostra).</summary>
    public static IReadOnlyList<AgingExportRow> AgingCustomerRows(AgingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = report.Rows.Select(r => new AgingExportRow(r.PartyName, null, r.Buckets, r.Total, r.MaxDaysLate, r.Count, r.AvgDelayDays, r.PctPaidLate, false)).ToList();
        rows.Add(new AgingExportRow("Total", null, report.Buckets.Select(b => b.Total).ToList(), report.Total, null, report.Count, null, null, true));
        return rows;
    }

    /// <summary>Por projeto, com "Sem projeto" no fim, e a linha de total.</summary>
    public static IReadOnlyList<AgingExportRow> AgingProjectRows(AgingProjectReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = report.Rows.OrderBy(r => r.ProjectId is null ? 1 : 0)
            .Select(r => new AgingExportRow(r.ProjectId is null ? "Sem projeto" : string.Join(" · ", new[] { r.ProjectNumber, r.ProjectName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                r.PartyName, r.Buckets, r.Total, r.MaxDaysLate, r.Count, null, null, false)).ToList();
        var buckets = Enumerable.Range(0, BucketHeaders.Length).Select(i => report.Rows.Sum(r => i < r.Buckets.Count ? r.Buckets[i] : 0m)).ToList();
        rows.Add(new AgingExportRow("Total", null, buckets, report.Total, null, null, null, null, true));
        return rows;
    }

    public static string? AgingSortLabel(string? sort) => sort switch
    {
        null or "" or "-total" => null,
        "total" => "Menor total vencido",
        "-maxDaysLate" => "Maior atraso",
        "maxDaysLate" => "Menor atraso",
        "name" => "Nome (A a Z)",
        "-name" => "Nome (Z a A)",
        _ => sort,
    };
}
