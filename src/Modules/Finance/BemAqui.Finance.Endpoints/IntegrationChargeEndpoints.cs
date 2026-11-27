using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Boleto emitido do título na lista (INT-03 CA 17): id, vencimento e valor; linha digitável e Pix só no detalhe.</summary>
public sealed record IntegrationSlipSummary(Guid Id, DateOnly DueDate, decimal Amount);

/// <summary>Título em aberto do cliente (INT-03 CA 17): número, vencimento, valor, saldo, "Vencido" e o boleto mais recente emitido.</summary>
public sealed record IntegrationChargeSummary(Guid Id, string Number, DateOnly? DueDate, decimal Amount, decimal Balance, bool Overdue, IntegrationSlipSummary? Slip);

/// <summary>
/// Um título (INT-03 CA 17): linha digitável, Pix copia-e-cola e o link do PDF do boleto mais recente emitido. Sem o retorno do banco
/// (<c>provider_payload</c>) e sem cache.
/// </summary>
public sealed record IntegrationCharge(
    Guid Id, Guid CustomerId, string Number, ReceivableStatus Status, DateOnly? DueDate, decimal Amount, decimal Balance, bool Overdue, Guid? SlipId, string? DigitableLine,
    string? PixCopyPaste, string? PdfUrl);

/// <summary>
/// Cobranças pela API de integração (INT-03 CA 17 e 20) com <c>finance.view</c>: <c>GET /customers/{id}/charges</c>, <c>GET /charges/{id}</c> e
/// <c>GET /charges/{id}/pdf</c>. Título de quem não é cliente, de outro cliente (<c>?customerId=</c>), cancelado ou inexistente: 404 igual. Cada leitura
/// vai para a auditoria (CA 23) com a origem do aplicativo.
/// </summary>
internal static class IntegrationChargeEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationCharges(this IEndpointRouteBuilder api)
    {
        var root = api.MapIntegrationGroup();
        root.MapGet("/customers/{id:guid}/charges", async (Guid id, IParties parties, IOrderCharges charges, IAuditWriter audit, CancellationToken ct) =>
            {
                if (!await IsCustomerAsync(id, parties, ct))
                {
                    return IntegrationApi.NotFound();
                }

                var open = await charges.OpenByCustomerAsync(id, ct);
                await audit.RecordReadAsync("Party", id.ToString(), "charges", ct);
                return Results.Ok(open.Select(c => new IntegrationChargeSummary(c.Id, c.Number, c.DueDate, c.Amount, c.Balance, c.Overdue,
                    c.Slip is { } s ? new IntegrationSlipSummary(s.Id, s.DueDate, s.Amount) : null)).ToList());
            })
            .RequirePermission("finance.view").WithName("IntegrationListCustomerCharges").Produces<IReadOnlyList<IntegrationChargeSummary>>();
        root.MapGet("/charges/{id:guid}", async (Guid id, Guid? customerId, IParties parties, IOrderCharges charges, IAuditWriter audit, CancellationToken ct) =>
            {
                if (await VisibleAsync(id, customerId, parties, charges, ct) is not { } charge)
                {
                    return IntegrationApi.NotFound();
                }

                await audit.RecordReadAsync("Receivable", id.ToString(), "charge", ct);
                var slip = charge.Slip;
                return Results.Ok(new IntegrationCharge(charge.Id, charge.CustomerId, charge.Number, charge.Status, charge.DueDate, charge.Amount, charge.Balance, charge.Overdue,
                    slip?.Id, slip?.DigitableLine, slip?.PixCopyPaste, slip is { HasPdf: true } ? $"/api/v1/integration/charges/{charge.Id}/pdf" : null));
            })
            .RequirePermission("finance.view").WithName("IntegrationGetCharge").Produces<IntegrationCharge>();
        root.MapGet("/charges/{id:guid}/pdf", async (Guid id, Guid? customerId, IParties parties, IOrderCharges charges, OrderChargeService service, AttachmentService attachments,
                IAuditWriter audit, HttpContext http, CancellationToken ct) =>
            {
                if (await VisibleAsync(id, customerId, parties, charges, ct) is null || await service.SlipPdfAsync(id, ct) is not { } pdf)
                {
                    return IntegrationApi.NotFound();
                }

                var file = await attachments.DownloadCheckedAsync(BillingSlipService.AttachmentOwner, pdf.SlipId, pdf.AttachmentId, ct);
                if (file.IsFailure)
                {
                    return file.Error!.ToProblem();
                }

                await audit.RecordReadAsync("Receivable", id.ToString(), "slip-pdf", ct);
                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
            })
            .RequirePermission("finance.view").WithName("IntegrationDownloadChargePdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        return api;
    }

    private static async Task<bool> IsCustomerAsync(Guid id, IParties parties, CancellationToken ct) =>
        await parties.FindAsync(id, ct) is { IsDeleted: false } party && party.Roles.Contains(PartyRoleKind.Customer);

    /// <summary>O título só aparece se for de um cliente e, com <c>customerId</c>, desse cliente (CA 20).</summary>
    private static async Task<CustomerChargeDetail?> VisibleAsync(Guid id, Guid? customerId, IParties parties, IOrderCharges charges, CancellationToken ct) =>
        await charges.ChargeOfCustomerAsync(id, ct) is { } charge && (customerId is null || customerId == charge.CustomerId) && await IsCustomerAsync(charge.CustomerId, parties, ct)
            ? charge
            : null;
}
