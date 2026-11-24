using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Pos.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
/// <summary>
/// Loja (PDV-01). CX-01: <c>CashDropAlertAmount</c> nulo mantém o aviso atual; <c>CashCeilingAmount</c> nulo = sem teto (a tela sempre manda).
/// IMP-01: <c>ReceiptPrintMode</c> nulo mantém o atual. OFF-02: <c>FreeRemoveLast</c> e <c>SyncAlertHours</c> nulos mantêm os atuais. O modo fiscal não muda por aqui (só o Proprietário, pela rota própria).
/// </summary>
public sealed record StoreRequest(
    string Code, string Name, Guid StockLocationId, Guid DefaultPriceListId, bool IsActive = true, decimal? CashDropAlertAmount = null, decimal? CashCeilingAmount = null,
    ReceiptPrintMode? ReceiptPrintMode = null, bool? FreeRemoveLast = null, int? SyncAlertHours = null, decimal? CashBreakTolerance = null);

/// <summary>IMP-01 CA 10: ligar (com o aceite do texto na versão informada) ou desligar o modo "só cupom não fiscal". <c>Version</c>: a da loja.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NonFiscalModeRequest(bool Enabled, bool Accepted, int? TermsVersion, uint Version);

/// <summary>IMP-01 CA 6: impressora do caixa. Na alteração, <c>Version</c> é a da impressora.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrinterRequest(
    Guid RegisterId, string Name, PrinterConnection Connection, int PaperWidthMm, int Columns, string Model, bool IsDefault = true, bool IsActive = true,
    bool TestConfirmed = false, uint Version = 0);

/// <summary>OFF-02 CA 15: resolver a pendência com o motivo; <c>Version</c> é a da pendência.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyncResolveRequest(SyncResolution Resolution, string Reason, uint Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest(int Number, Guid CashAccountId, RegisterUiMode UiMode, bool IsActive = true);

/// <summary>
/// Lojas e caixas (PDV-01 CA 2 e 3): tabelas do <c>Pos</c>, telas de retaguarda autorizadas com <c>sales.*</c> (02, seção 4.1). A lista também
/// serve ao registro do aparelho (<c>pos.manage_devices</c>), que escolhe a loja e o caixa. O aperto de mão do aparelho fica em
/// <c>/api/v1/pos/sync/*</c>, só com o esquema <c>Device</c> (CA 9).
/// </summary>
internal static class PosEndpoints
{
    public static IEndpointRouteBuilder MapPosEndpoints(this IEndpointRouteBuilder api)
    {
        var stores = api.MapGroup("/pos/stores").WithTags("Lojas e caixas").RequireModule("sales");
        stores.MapGet(string.Empty, async (StoreService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequireAnyPermissionOf("sales.view", "pos.manage_devices").WithName("ListStores").Produces<IReadOnlyList<StoreView>>();
        stores.MapGet("/options", async (StoreService service, CancellationToken ct) => Results.Ok(await service.OptionsAsync(ct)))
            .RequireAnyPermission("sales.create", "sales.edit").WithName("GetStoreFormOptions").Produces<StoreFormOptions>();
        stores.MapPost(string.Empty, async (StoreRequest input, StoreService service, CancellationToken ct) =>
                (await service.CreateStoreAsync(ToInput(input), ct)).ToHttpResult(s => Results.Created("/api/v1/pos/stores/" + s.Id, s)))
            .RequirePermission("sales.create").WithName("CreateStore").Produces<StoreView>(StatusCodes.Status201Created);
        stores.MapPut("/{id:guid}", async (Guid id, StoreRequest input, StoreService service, CancellationToken ct) =>
                (await service.UpdateStoreAsync(id, ToInput(input), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("UpdateStore").Produces<StoreView>();
        stores.MapPost("/{id:guid}/registers", async (Guid id, RegisterRequest input, StoreService service, CancellationToken ct) =>
                (await service.CreateRegisterAsync(id, ToInput(input), ct)).ToHttpResult(s => Results.Created("/api/v1/pos/stores/" + s.Id, s)))
            .RequirePermission("sales.create").WithName("CreateRegister").Produces<StoreView>(StatusCodes.Status201Created);
        stores.MapPut("/{id:guid}/registers/{registerId:guid}", async (Guid id, Guid registerId, RegisterRequest input, StoreService service, CancellationToken ct) =>
                (await service.UpdateRegisterAsync(id, registerId, ToInput(input), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("UpdateRegister").Produces<StoreView>();

        // IMP-01 CA 10, 11 e 13: o texto do aceite (versionado) e o modo "só cupom não fiscal", que só o Proprietário liga ou desliga (403 para quem
        // edita lojas sem ser do grupo). O aceite fica no histórico da loja (auditoria).
        stores.MapGet("/non-fiscal-terms", () => Results.Ok(StoreService.Terms()))
            .RequireAnyPermission("sales.view", "sales.edit").WithName("GetNonFiscalTerms").Produces<NonFiscalTermsView>();
        stores.MapPost("/{id:guid}/non-fiscal-mode", async (Guid id, NonFiscalModeRequest input, StoreService service, CancellationToken ct) =>
                (await service.SetNonFiscalModeAsync(id, new NonFiscalModeInput(input.Enabled, input.Accepted, input.TermsVersion, input.Version), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("SetStoreNonFiscalMode").Produces<StoreView>();

        // IMP-01 CA 6 e 7: impressoras do caixa (o assistente "Instalar impressora" do /pdv, com login de quem registra aparelhos). Descem para os
        // aparelhos do caixa na sincronização.
        var printers = api.MapGroup("/pos/printers").WithTags("PDV: impressoras").RequireModule("pos");
        printers.MapGet(string.Empty, async (Guid? registerId, PrinterService service, CancellationToken ct) => Results.Ok(await service.ListAsync(registerId, ct)))
            .RequireAnyPermissionOf("pos.manage_devices", "sales.view").WithName("ListPrinters").Produces<IReadOnlyList<PrinterView>>();
        printers.MapPost(string.Empty, async (PrinterRequest input, PrinterService service, CancellationToken ct) =>
                (await service.CreateAsync(ToInput(input), ct)).ToHttpResult(p => Results.Created("/api/v1/pos/printers/" + p.Id, p)))
            .RequirePermission("pos.manage_devices").WithName("CreatePrinter").Produces<PrinterView>(StatusCodes.Status201Created);
        printers.MapPut("/{id:guid}", async (Guid id, PrinterRequest input, PrinterService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, ToInput(input), input.Version, ct)).ToHttpResult())
            .RequirePermission("pos.manage_devices").WithName("UpdatePrinter").Produces<PrinterView>();

        // CA 8 a 10: só o principal do aparelho (esquema Device, lista fechada por teste). A loja e o caixa vêm do principal, nunca do corpo.
        var sync = api.MapGroup("/pos/sync").WithTags("PDV: sincronização").RequireModule("pos")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        sync.MapPost("/handshake", async (HttpContext http, PosSyncService service, CancellationToken ct) =>
            {
                var result = await service.HandshakeAsync(DevicePrincipal.From(http.User)!, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                DeviceEndpoints.SetCookie(http, result.Value.Rotation.Secret, result.Value.Rotation.ExpiresAt);
                return Results.Ok(result.Value.Handshake);
            })
            .RequireDevice().WithName("PosHandshake").Produces<PosHandshake>();

        // OFF-01 CA 10 a 12: foto e delta do catálogo da loja do aparelho (a loja vem do principal; nada de outra loja, sem custo nem margem).
        sync.MapGet("/snapshot", async (HttpContext http, PosCatalogSyncService service, CancellationToken ct) =>
                (await service.SnapshotAsync(DevicePrincipal.From(http.User)!, ct)).ToHttpResult())
            .RequireDevice().WithName("GetPosCatalogSnapshot").Produces<PosCatalogSnapshot>();
        sync.MapGet("/changes", async (long cursor, int? limit, HttpContext http, PosCatalogSyncService service, CancellationToken ct) =>
                (await service.ChangesAsync(DevicePrincipal.From(http.User)!, cursor, limit, ct)).ToHttpResult())
            .RequireDevice().WithName("GetPosCatalogChanges").Produces<PosCatalogChanges>();
        // PDV-05 (PP-02): saldo da loja do aparelho e a opção "vender sem saldo"; sem since, a lista inteira; com since, só o que mudou.
        sync.MapGet("/stock", async (long? since, HttpContext http, PosStockSyncService service, CancellationToken ct) =>
                (await service.StockAsync(DevicePrincipal.From(http.User)!, since, ct)).ToHttpResult())
            .RequireDevice().RequireRateLimiting(PosStockSyncService.RateLimitPolicy).WithName("GetPosStock").Produces<PosStockSnapshot>();

        // OFF-02 CA 1 a 6: subida dos eventos da fila de saída, assinada pela chave do aparelho e sem operador com sessão. É o único endpoint que
        // continua aberto para o aparelho revogado ou substituído (PDV-01 CA 8): o que ele envia entra em quarentena. A loja e o caixa vêm do principal.
        sync.MapPost("/events", async (PosSyncBatch input, HttpContext http, PosEventIngestion service, TimeProvider clock, CancellationToken ct) =>
            {
                var skew = long.TryParse(http.Request.Headers[DeviceSignatureHeaders.Timestamp].ToString(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms) - clock.GetUtcNow()
                    : TimeSpan.Zero;
                return (await service.IngestAsync(DevicePrincipal.From(http.User)!, input, skew, ct)).ToHttpResult();
            })
            .RequireDevice(allowRevoked: true).RequireRateLimiting(PosEventIngestion.RateLimitPolicy).WithName("PostPosSyncEvents").Produces<PosSyncBatchResult>();

        // OFF-02 CA 14 e 15: Pendências do PDV (retaguarda, sales.*), a exportação e o relatório de exceções por operador.
        var pending = api.MapGroup("/pos/divergences").WithTags("PDV: pendências").RequireModule("sales");
        pending.MapGet(string.Empty, async (SyncDivergenceStatus? status, SyncDivergenceKind? kind, Guid? storeId, Guid? registerId, SyncDivergenceService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new SyncDivergenceFilter(status, kind, storeId, registerId), ct)))
            .RequirePermission("sales.view").WithName("ListSyncDivergences").Produces<SyncDivergenceList>();
        pending.MapPost("/{id:guid}/resolve", async (Guid id, SyncResolveRequest input, SyncDivergenceService service, CancellationToken ct) =>
                (await service.ResolveAsync(id, new SyncResolveInput(input.Resolution, input.Reason, input.Version), ct)).ToHttpResult())
            .RequirePermission("sales.approve").WithName("ResolveSyncDivergence").Produces<SyncDivergenceView>();
        pending.MapGet("/export", async (SyncDivergenceStatus? status, SyncDivergenceKind? kind, Guid? storeId, Guid? registerId, SyncDivergenceService service, CancellationToken ct) =>
            {
                var file = await service.ExportAsync(new SyncDivergenceFilter(status, kind, storeId, registerId), ct);
                return Results.File(file.Content, SafeCsv.ContentType, file.FileName);
            })
            .RequirePermission("sales.view").WithName("ExportSyncDivergences");
        pending.MapGet("/operator-exceptions", async (DateOnly from, DateOnly to, Guid? storeId, SyncDivergenceService service, CancellationToken ct) =>
                (await service.ExceptionsAsync(from, to, storeId, ct)).ToHttpResult())
            .RequirePermission("sales.view").WithName("GetOperatorExceptions").Produces<OperatorExceptionReport>();

        // PDV-02 CA 15: o operador (sessão por PIN do caixa) acha o cliente cadastrado para ligar à venda; só nome e documento mascarado,
        // com rate limit por tenant, usuário e aparelho (revisão de segurança, achado 1).
        var customers = api.MapGroup("/pos/customers").WithTags("PDV: venda").RequireModule("pos");
        customers.MapGet(string.Empty, async (string? term, PosCustomerSearchService service, CancellationToken ct) => Results.Ok(await service.SearchAsync(term, ct)))
            .RequirePermission("pos.create").AllowPosPinSession().RequireRateLimiting(PosCustomerSearchService.RateLimitPolicy)
            .WithName("SearchPosCustomers").Produces<IReadOnlyList<PosCustomer>>();
        return api;
    }

    private static StoreInput ToInput(StoreRequest r) => new(r.Code, r.Name, r.StockLocationId, r.DefaultPriceListId, r.IsActive, r.CashDropAlertAmount, r.CashCeilingAmount, r.ReceiptPrintMode, r.FreeRemoveLast, r.SyncAlertHours, r.CashBreakTolerance);

    private static PrinterInput ToInput(PrinterRequest r) => new(r.RegisterId, r.Name, r.Connection, r.PaperWidthMm, r.Columns, r.Model, r.IsDefault, r.IsActive, r.TestConfirmed);

    private static RegisterInput ToInput(RegisterRequest r) => new(r.Number, r.CashAccountId, r.UiMode, r.IsActive);
}
