using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Finance.Application.Reconciliation;
using BemAqui.Finance.Application.Statements;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application.Exports;

/// <summary>
/// Exportação das telas do financeiro (EXP-02, ADR 0045 §3): cada método chama o <b>mesmo serviço da tela</b> com os mesmos filtros, sem
/// paginação e até o teto, e devolve as colunas, os filtros com os rótulos da tela, as linhas, as abas e o PDF próprio quando há. Máscara,
/// folha agrupada e período válido são os do serviço da tela; o teto, a auditoria e o download ficam no <see cref="ListExportService"/>.
/// </summary>
public sealed class FinanceExportService(
    BankStatementService statements,
    BankAccountService accounts,
    BankStatementImportService bankLines,
    ReconciliationService reconciliation,
    ReconciliationPeriodGuard period,
    PaymentOrderService orders,
    ReceivableService receivables,
    IParties parties,
    DreService dre,
    CashFlowService cashFlow,
    AgingService aging)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static string? Day(DateOnly? date) => date?.ToString("dd/MM/yyyy", Invariant);

    /// <summary>Extrato da conta (CA 7 e 8): saldo de partida, movimentos e saldo final; o PDF é o "Extrato da conta".</summary>
    public async Task<Result<ExportData<StatementExportRow>>> StatementAsync(Guid accountId, DateOnly? from, DateOnly? to, bool withoutOrder, int max, CancellationToken ct)
    {
        var result = await statements.StatementAsync(accountId, from, to, withoutOrder, new PageRequest(1, Math.Max(1, max)), ct);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var statement = result.Value;
        var account = await accounts.GetAsync(accountId, ct);
        var closedUntil = await period.ClosedUntilAsync(accountId, ct);
        var reconciledUntil = await reconciliation.ReconciledUntilAsync(accountId, closedUntil, ct);
        var rows = FinanceExports.StatementRows(statement);
        ExportFilter[] filters =
        [
            new("Conta", statement.AccountName),
            new("De", Day(statement.From)),
            new("Até", Day(statement.To)),
            new("Só movimentos sem OP", withoutOrder ? FinanceExports.Yes : null),
        ];
        var model = new StatementPdfModel(statement, account.IsSuccess ? account.Value : null, reconciledUntil);
        return new ExportData<StatementExportRow>(FinanceExports.Statement, filters, new ExportRows<StatementExportRow>(statement.Lines.Total + 2, rows))
        {
            Decorate = t => t with { Notes = statement.OpeningMissing ? [StatementPdfDocument.OpeningMissingText] : [] },
            Document = (frame, table) => new StatementPdfDocument(frame, model, table),
        };
    }

    /// <summary>Extrato do banco (CA 9): as linhas importadas com a contraparte mascarada como a tela (sem <c>finance.edit</c>).</summary>
    public async Task<Result<ExportData<BankStatementLineView>>> BankStatementLinesAsync(Guid accountId, DateOnly? from, DateOnly? to, string? status, int max, CancellationToken ct)
    {
        var result = await bankLines.ViewAsync(accountId, from, to, status, new PageRequest(1, Math.Max(1, max)), ct);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var view = result.Value;
        ExportFilter[] filters = [new("Conta", view.AccountName), new("De", Day(view.From)), new("Até", Day(view.To)), new("Situação", FinanceExports.LineStatusFilter(status))];
        return new ExportData<BankStatementLineView>(FinanceExports.BankStatementLines, filters, new ExportRows<BankStatementLineView>(view.Total, view.Lines))
        {
            Decorate = t => t with { Notes = view.CounterpartyMasked ? ["A contraparte sai mascarada: o nome e o documento inteiros ficam com quem edita o financeiro."] : [] },
        };
    }

    /// <summary>Conciliação (CA 10 e 11): uma aba por bloco da tela e o resumo; o PDF é o "Relatório de conciliação", com "Prévia" se o período não fechou.</summary>
    public async Task<Result<ExportData<ReconDone>>> ReconciliationAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var result = await reconciliation.ViewAsync(accountId, from, to, ct);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var view = result.Value;
        var total = view.Done.Count + view.Pairs.Count + view.BankOnly.Count + view.SystemOnly.Count;
        ExportFilter[] filters = [new("Conta", view.AccountName), new("De", Day(view.From)), new("Até", Day(view.To))];
        return new ExportData<ReconDone>(FinanceExports.ReconciledSpec, filters, new ExportRows<ReconDone>(total, view.Done))
        {
            Decorate = t => t with { SheetName = "Conciliados", Sheets = FinanceExports.ReconciliationSheets(view), Landscape = true },
            Document = (frame, table) => new ReconciliationPdfDocument(frame, view, table),
        };
    }

    /// <summary>Ordens de pagamento (CA 12): os filtros da tela e a linha de total do filtro.</summary>
    public async Task<Result<ExportData<PaymentOrderExportRow>>> PaymentOrdersAsync(string? situation, bool awaitingMe, bool selfApproved, int max, CancellationToken ct)
    {
        var page = await orders.ListAsync(new PaymentOrderListQuery(situation, awaitingMe, selfApproved), new PageRequest(1, Math.Max(1, max)), ct);
        ExportFilter[] filters =
        [
            new("Situação", situation is { } s ? FinanceExports.OrderSituations.GetValueOrDefault(s, s) : null),
            new("Mostrar", awaitingMe ? "Aguardando você" : selfApproved ? "Aprovadas pelo próprio solicitante" : null),
        ];
        return new ExportData<PaymentOrderExportRow>(FinanceExports.PaymentOrders, filters, new ExportRows<PaymentOrderExportRow>(page.Total + 1, FinanceExports.PaymentOrderRows(page.Items)));
    }

    /// <summary>A receber (CA 13): a busca, os filtros e a ordem da tela, com as linhas "Em aberto" e "Vencido" iguais ao rodapé.</summary>
    public async Task<Result<ExportData<ReceivableExportRow>>> ReceivablesAsync(ReceivableListQuery query, string? sort, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (total, items, totals) = await receivables.ExportRowsAsync(query, sort, max, ct);
        var party = query.PartyId is { } id ? await parties.FindAsync(id, ct) : null;
        ExportFilter[] filters =
        [
            new("Busca", string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim()),
            new("Situação", query.Situation is { } s ? FinanceExports.ReceivableSituations.GetValueOrDefault(s, s) : null),
            new("Cliente", party is null ? null : party.TradeName ?? party.LegalName),
            new("Vencimento de", Day(query.DueFrom)),
            new("Vencimento até", Day(query.DueTo)),
            new("Contrato", query.ContractId is null ? null : "As parcelas de um contrato"),
            new("Boleto", query.WithBillingSlip ? "Com boleto" : null),
        ];
        IReadOnlyList<ReceivableExportRow> rows = total > max ? [] : FinanceExports.ReceivableRows(items, totals);
        return new ExportData<ReceivableExportRow>(FinanceExports.Receivables, filters, new ExportRows<ReceivableExportRow>(total + 2, rows));
    }

    /// <summary>DRE do período (CA 14 a 16): a hierarquia da tela, avisos "A classificar"; o PDF é o "DRE gerencial" em paisagem. Até 24 meses (o serviço recusa).</summary>
    public async Task<Result<ExportData<BemAqui.Finance.Domain.DreRow>>> DreAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var result = await dre.BuildAsync(new DreQuery(from, to), ct);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var report = result.Value;
        ExportFilter[] filters = [new("De", FinanceExports.MonthLabel(report.From)), new("Até", FinanceExports.MonthLabel(report.To))];
        var notes = new List<string>();
        if (report.IsProvisional)
        {
            notes.Add(FinanceExports.DreProvisional);
        }

        notes.Add(FinanceExports.DreNote);
        return new ExportData<BemAqui.Finance.Domain.DreRow>(FinanceExports.Dre(report), filters, new ExportRows<BemAqui.Finance.Domain.DreRow>(report.Rows.Count, report.Rows))
        {
            Decorate = t => t with { Sheets = [ExportSheet.From("Avisos", FinanceExports.DreNotices, report.ToClassify)], Notes = notes, Landscape = true },
            Document = (frame, _) => new DrePdfDocument(frame, report),
        };
    }

    /// <summary>
    /// Fluxo de caixa (CA 17 a 19): um período por linha com as colunas do cenário, as abas "Destaques" e "A receber sem data"; o PDF de lista
    /// em paisagem com os destaques no topo. A folha prevista segue a regra da tela (só o total para quem não aprova nem paga).
    /// </summary>
    public async Task<Result<ExportData<CashFlowExportRow>>> CashFlowAsync(CashFlowQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await cashFlow.BuildAsync(query, ct);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var flow = result.Value;
        var scenario = query.Scenario ?? CashFlowService.ScenarioBoth;
        var adjusted = scenario == CashFlowService.ScenarioBase ? null : flow.Adjusted;
        var account = query.BankAccountId is { } bank ? await accounts.GetAsync(bank, ct) : null;
        ExportFilter[] filters =
        [
            new("Visão", FinanceExports.CashFlowViewLabel(flow.View)),
            new("Horizonte", $"{query.Days ?? 90} dias"),
            new("Conta", account is { IsSuccess: true } ? account.Value.Name : "Todas (consolidado)"),
            new("Cenário", FinanceExports.ScenarioLabel(scenario)),
            new("De", Day(flow.From)),
            new("Até", Day(flow.To)),
        ];
        var undated = await receivables.ExportRowsAsync(new ReceivableListQuery(null, "previsto", null, null, null), null, max, ct);
        var waiting = undated.Items.Where(r => r.DueDate is null).Select(FinanceExports.ReceivableRow).ToList();
        var rows = FinanceExports.CashFlowRows(flow, adjusted);
        return new ExportData<CashFlowExportRow>(FinanceExports.CashFlow(scenario, adjusted is not null), filters, new ExportRows<CashFlowExportRow>(rows.Count, rows))
        {
            Decorate = t => t with
            {
                Sheets = [ExportSheet.From("Destaques", FinanceExports.HighlightsSpec, FinanceExports.CashFlowHighlights(flow, adjusted)), ExportSheet.From("A receber sem data", FinanceExports.Receivables, waiting)],
                Notes = FinanceExports.CashFlowNotes(flow, adjusted),
                Landscape = true,
            },
        };
    }

    /// <summary>Inadimplência por cliente (CA 20): as faixas, o histórico de pagamento e a linha de total; sem CPF/CNPJ.</summary>
    public async Task<Result<ExportData<AgingExportRow>>> AgingAsync(string? sort, CancellationToken ct)
    {
        var result = await aging.ReportAsync(sort, ct);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var rows = FinanceExports.AgingCustomerRows(result.Value);
        return new ExportData<AgingExportRow>(FinanceExports.AgingCustomers, [new("Ordem", FinanceExports.AgingSortLabel(sort))], new ExportRows<AgingExportRow>(rows.Count, rows));
    }

    /// <summary>Inadimplência por projeto (CA 20): "Sem projeto" no fim e a linha de total.</summary>
    public async Task<Result<ExportData<AgingExportRow>>> AgingProjectsAsync(CancellationToken ct)
    {
        var rows = FinanceExports.AgingProjectRows(await aging.ProjectsAsync(ct));
        return new ExportData<AgingExportRow>(FinanceExports.AgingProjects, [], new ExportRows<AgingExportRow>(rows.Count, rows));
    }
}
