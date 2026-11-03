using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Application.Exports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// Exportação do financeiro (EXP-02, ADR 0045 §3): <c>GET …/export?format=xlsx|pdf</c> com <b>os mesmos filtros</b> do <c>GET</c> da tela e a
/// mesma permissão (<c>finance.view</c>); o extrato, a conciliação e o DRE têm também o <c>GET …/pdf</c> do documento próprio do período.
/// Teto, auditoria, nome do arquivo e <c>no-store</c> no <see cref="ListExportService"/>; a máscara é a do serviço da tela, sem 2FA.
/// </summary>
internal static partial class FinanceEndpoints
{
    private const string Pdf = "pdf";

    private static void MapExports(RouteGroupBuilder finance)
    {
        finance.MapGet("/bank-accounts/{id:guid}/statement/export", (Guid id, string? format, DateOnly? from, DateOnly? to, bool? withoutOrder, HttpContext http, FinanceExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.StatementAsync(id, from, to, withoutOrder == true, max, c), ct))
            .RequirePermission("finance.view").WithName("ExportBankStatement");
        finance.MapGet("/bank-accounts/{id:guid}/statement/pdf", (Guid id, DateOnly? from, DateOnly? to, bool? withoutOrder, HttpContext http, FinanceExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, Pdf, (max, c) => service.StatementAsync(id, from, to, withoutOrder == true, max, c), ct))
            .RequirePermission("finance.view").WithName("DownloadBankStatementPdf");
        finance.MapGet("/bank-accounts/{id:guid}/statement-lines/export", (Guid id, string? format, DateOnly? from, DateOnly? to, string? status, HttpContext http, FinanceExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.BankStatementLinesAsync(id, from, to, status, max, c), ct))
            .RequirePermission("finance.view").WithName("ExportBankStatementLines");
        finance.MapGet("/bank-accounts/{id:guid}/reconciliation/export", (Guid id, string? format, DateOnly? from, DateOnly? to, HttpContext http, FinanceExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, (_, c) => service.ReconciliationAsync(id, from, to, c), ct))
            .RequirePermission("finance.view").WithName("ExportReconciliation");
        finance.MapGet("/bank-accounts/{id:guid}/reconciliation/pdf", (Guid id, DateOnly? from, DateOnly? to, HttpContext http, FinanceExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, Pdf, (_, c) => service.ReconciliationAsync(id, from, to, c), ct))
            .RequirePermission("finance.view").WithName("DownloadReconciliationPdf");
        finance.MapGet("/payment-orders/export", (string? format, string? situation, bool? awaitingMe, bool? selfApproved, HttpContext http, FinanceExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.PaymentOrdersAsync(situation, awaitingMe == true, selfApproved == true, max, c), ct))
            .RequirePermission("finance.view").WithName("ExportPaymentOrders");
        finance.MapGet("/receivables/export", (string? format, string? q, string? situation, Guid? partyId, DateOnly? dueFrom, DateOnly? dueTo, Guid? contractId, bool? withBillingSlip, string? sort,
                HttpContext http, FinanceExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (max, c) => service.ReceivablesAsync(new ReceivableListQuery(q, situation, partyId, dueFrom, dueTo, contractId, withBillingSlip == true), sort, max, c), ct))
            .RequirePermission("finance.view").WithName("ExportReceivables");
        finance.MapGet("/dre/export", (string? format, DateOnly? from, DateOnly? to, HttpContext http, FinanceExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (_, c) => service.DreAsync(from, to, c), ct))
            .RequirePermission("finance.view").WithName("ExportDre");
        finance.MapGet("/dre/pdf", (DateOnly? from, DateOnly? to, HttpContext http, FinanceExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, Pdf, (_, c) => service.DreAsync(from, to, c), ct))
            .RequirePermission("finance.view").WithName("DownloadDrePdf");
        finance.MapGet("/cash-flow/export", (string? format, string? view, int? days, Guid? bankAccountId, DateOnly? from, string? scenario, HttpContext http, FinanceExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.CashFlowAsync(new CashFlowQuery(view, days, bankAccountId, from, scenario), max, c), ct))
            .RequirePermission("finance.view").WithName("ExportCashFlow");
        finance.MapGet("/aging/export", (string? format, string? sort, HttpContext http, FinanceExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (_, c) => service.AgingAsync(sort, c), ct))
            .RequirePermission("finance.view").WithName("ExportAging");
        finance.MapGet("/aging/projects/export", (string? format, HttpContext http, FinanceExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (_, c) => service.AgingProjectsAsync(c), ct))
            .RequirePermission("finance.view").WithName("ExportAgingByProject");
    }
}
