using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Importação de produtos por planilha (CAT-02). <b>Prévia</b> (CA 2, 5 e 7): lê o xlsx (mesma allowlist e teto de tamanho dos anexos), confere
/// cada linha e grava as válidas e as com erro em <c>import_runs</c>, sem tocar no catálogo. <b>Confirmar</b> (CA 3 e 6): só as válidas entram,
/// cada uma na sua transação, pelos serviços do Catálogo (<see cref="ProductService"/>, <see cref="PriceService"/>) e pelo motor do Estoque
/// (<see cref="IStockMovements"/>, saldo inicial com a chave do produto, então reimportar não lança de novo). Até <see cref="SyncMaxRows"/> linhas
/// roda no pedido; acima, num job do Hangfire com o <c>tenantId</c>, com progresso e aviso no sino. <b>Chave</b> (CA 4): o código interno (código
/// do item no Estoque); na falta dele, o código de barras.
/// </summary>
public sealed partial class ProductImportService(
    ICatalogDb db,
    CatalogDefaults defaults,
    ProductService products,
    PriceService prices,
    CategoryService categories,
    IItems items,
    IItemRegistration registration,
    ILocations locations,
    IStockMovements stock,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IAuditWriter audit,
    INotificationService notifications,
    IBackgroundJobClient jobs,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<ProductImportService> logger)
{
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const int DefaultSyncMaxRows = 500;
    public const int ErrorsShown = 200;
    public const string AuditEntity = "ProductImport";
    public const string NotificationType = "catalog.import";
    public const string Route = "/app/catalogo/produtos/importar";
    public const string NotFoundCode = "catalog_import_not_found";
    public const string AlreadyConfirmedCode = "catalog_import_already_confirmed";
    public const string StoreLocationName = "Loja";

    private const int Chunk = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Acima disso, a confirmação vai para o job (<c>Catalog:ImportSyncMaxRows</c>, padrão 500).</summary>
    public int SyncMaxRows => int.TryParse(configuration["Catalog:ImportSyncMaxRows"], out var n) && n >= 0 ? n : DefaultSyncMaxRows;

    /// <summary>CA 1: o modelo para baixar.</summary>
    public static byte[] Template() => ProductImportSheet.Template();

    /// <summary>
    /// CA 2, 5 e 7: lê e confere a planilha e grava a prévia. Arquivo fora da allowlist (só xlsx) ou acima de 10 MB = 422 com o que fazer.
    /// </summary>
    public async Task<Result<ProductImportView>> PreviewAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxFileBytes)
        {
            return Error.Validation(ProductImportSheet.InvalidFileCode, "A planilha passa de 10 MB. Divida em arquivos menores.");
        }

        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (AttachmentPolicy.Match(name, content.AsSpan(0, Math.Min(content.Length, 64))) is not { Name: "XLSX" })
        {
            return Error.Validation(ProductImportSheet.InvalidFileCode, "Envie a planilha no formato .xlsx (Excel). Baixe o modelo se precisar.");
        }

        var read = ProductImportSheet.Read(content);
        if (read.IsFailure)
        {
            return read.Error!;
        }

        var pricesIgnored = !await permissions.HasPermissionAsync(ProductService.Module, "edit_price", cancellationToken);
        var lookups = await LookupsAsync(read.Value.Select(r => r.Cells).ToList(), cancellationToken);
        var valid = new List<ProductImportRow>();
        var errors = new List<ProductImportError>();
        var seenCodes = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenGtins = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (line, cells) in read.Value)
        {
            var code = ProductImportRules.Cell(cells, ProductImportColumns.Code);
            var gtin = ProductImportRules.Gtin(cells);
            var plan = Resolve(line, cells, lookups, pricesIgnored);
            var message = plan.Error
                ?? (code is not null && seenCodes.TryGetValue(code, out var codeLine) ? $"Linha {line}: código interno repetido na planilha (já está na linha {codeLine})." : null)
                ?? (gtin is not null && seenGtins.TryGetValue(gtin, out var gtinLine) ? $"Linha {line}: código de barras repetido na planilha (já está na linha {gtinLine})." : null);
            if (message is not null)
            {
                errors.Add(new ProductImportError(line, message, cells));
                continue;
            }

            if (code is not null)
            {
                seenCodes[code] = line;
            }

            if (gtin is not null)
            {
                seenGtins[gtin] = line;
            }

            valid.Add(new ProductImportRow(line, cells, plan.Action));
        }

        var run = new ProductImportRun
        {
            FileName = name.Length > 255 ? name[..255] : name,
            TotalRows = read.Value.Count,
            ToCreate = valid.Count(r => r.Action == ProductImportAction.Create),
            ToUpdate = valid.Count(r => r.Action == ProductImportAction.Update),
            ErrorRows = errors.Count,
            StockRows = valid.Count(r => ProductImportRules.Stock(r.Cells) is not null),
            PricesIgnored = pricesIgnored,
            RowsJson = JsonSerializer.Serialize(valid, Json),
            ErrorsJson = JsonSerializer.Serialize(errors, Json),
            CreatedBy = currentUser.UserId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.ProductImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(run, valid, errors, cancellationToken);
    }

    public async Task<Result<ProductImportView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.ProductImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return run is null ? NotFound() : await ViewAsync(run, Rows(run), Errors(run), cancellationToken);
    }

    /// <summary>CA 3: as linhas com erro (da prévia e as que falharem ao gravar) numa planilha para corrigir e enviar de novo.</summary>
    public async Task<Result<byte[]>> ErrorsFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.ProductImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return run is null ? NotFound() : ProductImportSheet.Errors(Errors(run).OrderBy(e => e.Line).ToList());
    }

    /// <summary>
    /// CA 3 e 6: confirma uma vez só (a troca Previewed → Running é atômica; a segunda dá 409). Preço só com <c>catalog.edit_price</c> agora
    /// (CA 5); saldo inicial só com "Lançar saldo inicial", <c>inventory.adjust</c> e um local ativo. Grande: vai para o job.
    /// </summary>
    public async Task<Result<ProductImportView>> ConfirmAsync(Guid id, ProductImportConfirmInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var run = await db.ProductImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status != ProductImportStatus.Previewed)
        {
            return AlreadyConfirmed();
        }

        if (run.ToCreate + run.ToUpdate == 0)
        {
            return Error.Validation("catalog_import_nothing", "Nenhuma linha válida para importar. Corrija a planilha e envie de novo.");
        }

        var launch = input.LaunchInitialStock && run.StockRows > 0;
        Guid? locationId = null;
        if (launch)
        {
            if (!await permissions.HasPermissionAsync("inventory", "adjust", cancellationToken))
            {
                return Error.Forbidden(Error.Codes.Forbidden, "Você não tem permissão para lançar saldo. Peça a quem tem \"Ajustar estoque\" no Estoque, ou importe sem o saldo inicial.");
            }

            var active = input.LocationId is { } wanted ? (await locations.ActiveAsync(cancellationToken)).FirstOrDefault(l => l.Id == wanted) : null;
            if (active is null)
            {
                return Error.Validation("validation", "Escolha um local de estoque ativo para o saldo inicial.", new Dictionary<string, string[]> { ["locationId"] = ["Escolha um local de estoque ativo para o saldo inicial."] });
            }

            locationId = active.Id;
        }

        var applyPrices = !run.PricesIgnored && await permissions.HasPermissionAsync(ProductService.Module, "edit_price", cancellationToken);
        var background = run.ToCreate + run.ToUpdate > SyncMaxRows;
        var user = currentUser.UserId;
        var now = clock.GetUtcNow();
        var claimed = await db.ProductImportRuns.Where(r => r.Id == id && r.Status == ProductImportStatus.Previewed).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, ProductImportStatus.Running)
            .SetProperty(r => r.ConfirmedBy, user)
            .SetProperty(r => r.ConfirmedAt, now)
            .SetProperty(r => r.ApplyPrices, applyPrices)
            .SetProperty(r => r.LaunchStock, launch)
            .SetProperty(r => r.StockLocationId, locationId)
            .SetProperty(r => r.InBackground, background), cancellationToken);
        if (claimed == 0)
        {
            return AlreadyConfirmed();
        }

        // Auditoria (regra da story): a importação confirmada, com os totais da prévia e quem confirmou; o resultado fica na importação.
        await audit.RecordImportAsync(AuditEntity, id.ToString(), "xlsx", run.TotalRows, run.ToCreate + run.ToUpdate, cancellationToken);
        if (background)
        {
            var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Importação de produtos fora do escopo de tenant.");
            var args = new ProductImportJobArgs(id, user ?? Guid.Empty);
            jobs.Create<ProductImportJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
        }
        else
        {
            await ExecuteAsync(id, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Executa a importação confirmada (no pedido ou no job): retoma de <see cref="ProductImportRun.Processed"/>, em blocos de 200 linhas (uma
    /// leitura do banco por bloco), cada linha na sua transação. A linha que falhar ao gravar vai para as com erro, com o motivo, e as outras seguem.
    /// </summary>
    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.ProductImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is not { Status: ProductImportStatus.Running })
        {
            return;
        }

        var rows = Rows(run);
        var errors = Errors(run).ToList();
        var counters = new Counters(run.Processed, run.Created, run.Updated, run.Failed, run.StockPosted, run.StockAlreadyPosted);
        try
        {
            await defaults.EnsureAsync(cancellationToken);
            var categoryIds = await EnsureCategoriesAsync(rows, cancellationToken);
            for (var start = counters.Processed; start < rows.Count; start += Chunk)
            {
                var block = rows.Skip(start).Take(Chunk).ToList();
                var lookups = await LookupsAsync(block.Select(r => r.Cells).ToList(), cancellationToken);
                foreach (var row in block)
                {
                    var failure = await ApplyRowAsync(run, row, lookups, categoryIds, counters, cancellationToken);
                    if (failure is not null)
                    {
                        errors.Add(new ProductImportError(row.Line, failure, row.Cells));
                        counters.Failed++;
                    }

                    counters.Processed++;
                }

                await SaveProgressAsync(id, counters, errors, null, null, cancellationToken);
            }

            await SaveProgressAsync(id, counters, errors, ProductImportStatus.Completed, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, id, counters.Processed, ex);
            db.ChangeTracker.Clear();
            var line = counters.Processed < rows.Count ? rows[counters.Processed].Line : 0;
            await SaveProgressAsync(id, counters, errors, ProductImportStatus.Failed,
                $"A importação parou na linha {line}. As linhas anteriores foram gravadas: envie a mesma planilha de novo para terminar (o que já entrou é atualizado, não duplicado).",
                CancellationToken.None);
        }
    }

    /// <summary>O job terminou: o aviso no sino leva ao relatório (padrão propositivo).</summary>
    public async Task NotifyDoneAsync(ProductImportJobArgs args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        var run = await db.ProductImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == args.RunId, cancellationToken);
        if (run is null || args.RequestedBy == Guid.Empty)
        {
            return;
        }

        var title = run.Status == ProductImportStatus.Completed
            ? $"Importação de produtos concluída: {run.Created} criados, {run.Updated} atualizados, {run.ErrorRows + run.Failed} ignorados."
            : "A importação de produtos parou antes do fim. Veja o que fazer.";
        notifications.Notify(args.RequestedBy, NotificationType, title, $"{Route}?id={run.Id}");
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Error NotFound() => Error.NotFound(NotFoundCode, "Importação não encontrada. Envie a planilha de novo.");

    private static Error AlreadyConfirmed() => Error.Conflict(AlreadyConfirmedCode, "Esta importação já foi confirmada. Para importar de novo, envie a planilha outra vez.");

    private static List<ProductImportRow> Rows(ProductImportRun run) => JsonSerializer.Deserialize<List<ProductImportRow>>(run.RowsJson, Json) ?? [];

    private static List<ProductImportError> Errors(ProductImportRun run) => JsonSerializer.Deserialize<List<ProductImportError>>(run.ErrorsJson, Json) ?? [];

    private async Task SaveProgressAsync(Guid id, Counters c, List<ProductImportError> errors, ProductImportStatus? status, string? failure, CancellationToken ct)
    {
        var errorsJson = JsonSerializer.Serialize(errors, Json);
        var now = clock.GetUtcNow();
        await db.ProductImportRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s =>
        {
            s.SetProperty(r => r.Processed, c.Processed)
                .SetProperty(r => r.Created, c.Created)
                .SetProperty(r => r.Updated, c.Updated)
                .SetProperty(r => r.Failed, c.Failed)
                .SetProperty(r => r.StockPosted, c.StockPosted)
                .SetProperty(r => r.StockAlreadyPosted, c.StockAlreadyPosted)
                .SetProperty(r => r.ErrorsJson, errorsJson);
            if (status is { } done)
            {
                s.SetProperty(r => r.Status, done).SetProperty(r => r.CompletedAt, now);
            }

            if (failure is not null)
            {
                s.SetProperty(r => r.FailureMessage, failure);
            }
        }, ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Importação de produtos {RunId} parou depois de {Processed} linhas.")]
    private static partial void LogFailed(ILogger logger, Guid runId, int processed, Exception exception);

    /// <summary>Contadores da execução, gravados a cada bloco (o job retoma de <see cref="Processed"/>).</summary>
    private sealed class Counters(int processed, int created, int updated, int failed, int stockPosted, int stockAlreadyPosted)
    {
        public int Processed { get; set; } = processed;

        public int Created { get; set; } = created;

        public int Updated { get; set; } = updated;

        public int Failed { get; set; } = failed;

        public int StockPosted { get; set; } = stockPosted;

        public int StockAlreadyPosted { get; set; } = stockAlreadyPosted;
    }
}
