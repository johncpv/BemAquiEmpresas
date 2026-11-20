using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// Parâmetros da loja que o caixa usa sem rede (PP-10 e PP-22). Dinheiro em centavos inteiros (OFF-01 CA 8). <c>OfflineWindowHours</c>: depois disso
/// sem sincronizar, o caixa para de vender (padrão e teto de 168 h). <c>CashDropAlertCents</c> e <c>CashCeilingCents</c> (nulo = sem teto): aviso de sangria e
/// bloqueio do Dinheiro (CX-01 CA 7 e 8). IMP-01: nome e CNPJ da empresa para o cabeçalho do cupom (o CNPJ próprio da loja é da NFCE-07), o modo
/// fiscal, <c>CanSell</c> (falso: a loja não tem documento fiscal para a venda, ou seja, sem o aceite do modo não fiscal na versão vigente do texto até a NFC-e da Fase 13, CA 10 e 13)
/// e o que o caixa faz com o cupom ao concluir (CA 5). OFF-02: <c>FreeRemoveLast</c>, o último item sai sem supervisor (o servidor confere por ele).
/// </summary>
public sealed record PosSyncStore(
    Guid Id,
    string Code,
    string Name,
    Guid RegisterId,
    int RegisterNumber,
    int OfflineWindowHours,
    long CashBreakToleranceCents,
    int ExchangeDays,
    long CashDropAlertCents,
    long? CashCeilingCents,
    string? CompanyName = null,
    string? CompanyDocument = null,
    StoreFiscalMode FiscalMode = StoreFiscalMode.Nfce,
    bool CanSell = true,
    ReceiptPrintMode ReceiptPrintMode = ReceiptPrintMode.Ask,
    bool FreeRemoveLast = true);

/// <summary>A tabela de preço da loja e a versão mais nova que já desceu (o aparelho informa a versão em que vendeu).</summary>
public sealed record PosSyncPriceList(Guid Id, long Version);

/// <summary>
/// Produto vendável no caixa da loja (CA 10): nome, unidade, pesável e PLU, códigos de barras e o preço da tabela da loja em centavos (null = sem
/// preço: não vende). Sem custo e sem margem (CA 12). <c>ChangeSeq</c> é a posição da linha no cursor.
/// </summary>
public sealed record PosSyncItem(
    Guid ProductId,
    Guid? ParentProductId,
    string Description,
    string UnitCode,
    bool IsWeighable,
    string? Plu,
    IReadOnlyList<PosSyncBarcode> Barcodes,
    long? PriceCents,
    long? PriceListVersion,
    long ChangeSeq);

/// <summary>
/// Forma de pagamento ativa (CA 10): sem taxa da adquirente. <c>CardBrands</c> e <c>MaxInstallments</c> (PDV-03 CA 4): as bandeiras que o caixa
/// oferece (vazio: todas) e o teto de parcelas (1 fora do crédito parcelado).
/// </summary>
public sealed record PosSyncPaymentMethod(
    Guid Id,
    string Name,
    PaymentMethodType Type,
    bool AllowsChange,
    string NfePaymentCode,
    bool Confirmed,
    IReadOnlyList<CardBrand> CardBrands,
    int MaxInstallments,
    long ChangeSeq);

/// <summary>Operador que pode entrar por PIN nos caixas da loja: id e nome curto. O PIN nunca desce (a entrada sem rede é da OFF-03).</summary>
public sealed record PosSyncOperator(Guid UserId, string DisplayName);

/// <summary>Impressora do caixa (IMP-01 CA 3, 6 e 7): conexão, largura da bobina, colunas (32, 42 ou 48), modelo e se é a padrão.</summary>
public sealed record PosSyncPrinter(Guid Id, string Name, PrinterConnection Connection, int PaperWidthMm, int Columns, string Model, bool IsDefault);

/// <summary>Coleção de impressoras: substitui a do aparelho por inteiro quando a versão muda.</summary>
public sealed record PosSyncPrinters(int Version, IReadOnlyList<PosSyncPrinter> Items);

/// <summary>
/// Foto inicial do catálogo da loja do aparelho (OFF-01 CA 10). <c>Cursor</c> é o ponto de partida do delta. <c>Collections</c> é a lista fechada
/// desta <c>SchemaVersion</c>. Operadores e impressoras são pequenos e descem inteiros (também no delta).
/// </summary>
public sealed record PosCatalogSnapshot(
    int SchemaVersion,
    IReadOnlyList<string> Collections,
    long Cursor,
    DateTimeOffset GeneratedAt,
    PosSyncStore Store,
    PosSyncPriceList PriceList,
    IReadOnlyList<PosSyncItem> Items,
    IReadOnlyList<PosSyncPaymentMethod> PaymentMethods,
    IReadOnlyList<PosSyncOperator> Operators,
    PosSyncPrinters Printers);

/// <summary>
/// Delta desde o cursor (OFF-01 CA 11): itens e formas novos ou alterados, os ids removidos (produto desativado, excluído ou fora do caixa; forma
/// desativada), o cursor novo e se há mais páginas (<c>HasMore</c>: pedir de novo com o cursor novo). Loja, operadores e impressoras vêm inteiros.
/// </summary>
public sealed record PosCatalogChanges(
    int SchemaVersion,
    long Cursor,
    bool HasMore,
    DateTimeOffset GeneratedAt,
    PosSyncStore Store,
    PosSyncPriceList PriceList,
    IReadOnlyList<PosSyncItem> Items,
    IReadOnlyList<Guid> RemovedItemIds,
    IReadOnlyList<PosSyncPaymentMethod> PaymentMethods,
    IReadOnlyList<Guid> RemovedPaymentMethodIds,
    IReadOnlyList<PosSyncOperator> Operators,
    PosSyncPrinters Printers);

/// <summary>
/// Descida do catálogo para o aparelho de caixa (OFF-01 CA 10 a 12, rotas <c>/api/v1/pos/sync/*</c> no esquema <c>Device</c>). A loja vem do
/// principal do aparelho, nunca da requisição; tudo sai de <c>pos.catalog_items</c> filtrado pela loja (preço da tabela da loja, operadores da
/// loja). Não gira o segredo do aparelho (só o aperto de mão gira).
/// </summary>
public sealed class PosCatalogSyncService(
    IPosDb db, PosSyncService sync, PosCatalogProjector projector, IPosStoreOperators operators, PrinterService printers, ICompanyIdentity company, TimeProvider clock)
{
    /// <summary>Versão do formato da foto e do delta; sobe quando uma coleção entra ou muda de forma.</summary>
    public const int SchemaVersion = 1;

    public const int DefaultPageSize = 1000;
    public const int MaxPageSize = 5000;

    /// <summary>Lista fechada de coleções da <see cref="SchemaVersion"/> 1. Os códigos de barras e o preço vão dentro de cada item.</summary>
    public static readonly IReadOnlyList<string> Collections = ["store", "priceList", "items", "paymentMethods", "operators", "printers"];

    public async Task<Result<PosCatalogSnapshot>> SnapshotAsync(DeviceIdentity device, CancellationToken cancellationToken = default)
    {
        var place = await sync.ResolveAsync(device, cancellationToken);
        if (place.IsFailure)
        {
            return place.Error!;
        }

        var (store, register) = place.Value;
        await projector.EnsureBuiltAsync(store, cancellationToken);

        // O cursor é lido antes das linhas: o que mudar no meio desce de novo no próximo delta (o aparelho grava por id, sem duplicar).
        var cursor = await CursorAsync(store.Id, cancellationToken);
        var rows = await db.CatalogItems.AsNoTracking().Where(i => i.StoreId == store.Id && !i.Deleted).ToListAsync(cancellationToken);
        var items = rows.Where(r => r.Kind == CatalogItemKind.Product).Select(ToItem).OrderBy(i => i.Description, StringComparer.CurrentCulture).ToList();
        var methods = rows.Where(r => r.Kind == CatalogItemKind.PaymentMethod).Select(ToPaymentMethod).OrderBy(m => m.Name, StringComparer.CurrentCulture).ToList();
        return new PosCatalogSnapshot(
            SchemaVersion, Collections, cursor, clock.GetUtcNow(), await ToStoreAsync(store, register, cancellationToken), await PriceListAsync(store, cancellationToken),
            items, methods, await OperatorsAsync(store.Id, cancellationToken), await printers.ForRegisterAsync(register.Id, cancellationToken));
    }

    public async Task<Result<PosCatalogChanges>> ChangesAsync(DeviceIdentity device, long cursor, int? limit, CancellationToken cancellationToken = default)
    {
        if (cursor < 0)
        {
            return Error.Validation("invalid_cursor", "Cursor inválido. Baixe a foto do catálogo de novo.");
        }

        var place = await sync.ResolveAsync(device, cancellationToken);
        if (place.IsFailure)
        {
            return place.Error!;
        }

        var (store, register) = place.Value;
        await projector.EnsureBuiltAsync(store, cancellationToken);
        var current = await CursorAsync(store.Id, cancellationToken);
        if (cursor > current)
        {
            // Cursor de outra loja (aparelho trocado de caixa) ou de um banco restaurado: o aparelho baixa a foto de novo.
            return Error.Conflict("snapshot_required", "O catálogo deste caixa precisa ser baixado de novo.");
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var rows = await db.CatalogItems.AsNoTracking().Where(i => i.StoreId == store.Id && i.ChangeSeq > cursor)
            .OrderBy(i => i.ChangeSeq).Take(size + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > size;
        var page = rows.Take(size).ToList();
        var next = page.Count == 0 ? cursor : page[^1].ChangeSeq;
        return new PosCatalogChanges(
            SchemaVersion, next, hasMore, clock.GetUtcNow(), await ToStoreAsync(store, register, cancellationToken), await PriceListAsync(store, cancellationToken),
            page.Where(r => r.Kind == CatalogItemKind.Product && !r.Deleted).Select(ToItem).ToList(),
            page.Where(r => r.Kind == CatalogItemKind.Product && r.Deleted).Select(r => r.EntityId).ToList(),
            page.Where(r => r.Kind == CatalogItemKind.PaymentMethod && !r.Deleted).Select(ToPaymentMethod).ToList(),
            page.Where(r => r.Kind == CatalogItemKind.PaymentMethod && r.Deleted).Select(r => r.EntityId).ToList(),
            await OperatorsAsync(store.Id, cancellationToken), await printers.ForRegisterAsync(register.Id, cancellationToken));
    }

    private async Task<long> CursorAsync(Guid storeId, CancellationToken cancellationToken) =>
        await db.CatalogItems.Where(i => i.StoreId == storeId).MaxAsync(i => (long?)i.ChangeSeq, cancellationToken) ?? 0;

    private async Task<PosSyncPriceList> PriceListAsync(Store store, CancellationToken cancellationToken)
    {
        var version = await db.CatalogItems.Where(i => i.StoreId == store.Id && i.Kind == CatalogItemKind.Product && !i.Deleted)
            .MaxAsync(i => i.PriceListVersion, cancellationToken);
        return new PosSyncPriceList(store.DefaultPriceListId, version ?? 0);
    }

    private async Task<IReadOnlyList<PosSyncOperator>> OperatorsAsync(Guid storeId, CancellationToken cancellationToken) =>
        (await operators.ForStoreAsync(storeId, cancellationToken)).Select(o => new PosSyncOperator(o.UserId, o.DisplayName)).ToList();

    private async Task<PosSyncStore> ToStoreAsync(Store store, Register register, CancellationToken cancellationToken)
    {
        var identity = await company.GetAsync(cancellationToken);
        return new PosSyncStore(
            store.Id, store.Code, store.Name, register.Id, register.Number, Math.Clamp(store.OfflineWindowHours, 1, Store.DefaultOfflineWindowHours),
            PosCatalogProjector.ToCents(store.CashBreakTolerance), store.ExchangeDays, PosCatalogProjector.ToCents(store.CashDropAlertAmount),
            store.CashCeilingAmount is { } ceiling ? PosCatalogProjector.ToCents(ceiling) : null,
            string.IsNullOrWhiteSpace(identity.TradeName) ? identity.LegalName : identity.TradeName, identity.Document,
            store.FiscalMode, store.NonFiscalAcceptedFor(NonFiscalTerms.Version), store.ReceiptPrintMode, store.FreeRemoveLast);
    }

    private static PosSyncItem ToItem(CatalogItem row)
    {
        var p = JsonSerializer.Deserialize<PosItemPayload>(row.Payload, PosCatalogProjector.Json)!;
        return new PosSyncItem(p.ProductId, p.ParentProductId, p.Description, p.UnitCode, p.IsWeighable, p.Plu, p.Barcodes, p.PriceCents, p.PriceListVersion, row.ChangeSeq);
    }

    private static PosSyncPaymentMethod ToPaymentMethod(CatalogItem row)
    {
        var p = JsonSerializer.Deserialize<PosPaymentMethodPayload>(row.Payload, PosCatalogProjector.Json)!;
        return new PosSyncPaymentMethod(
            p.Id, p.Name, p.Type, p.AllowsChange, p.NfePaymentCode, p.Confirmed, p.CardBrands ?? [], p.MaxInstallments ?? PosCardTerms.MaxInstallments(p.Type, []), row.ChangeSeq);
    }
}
