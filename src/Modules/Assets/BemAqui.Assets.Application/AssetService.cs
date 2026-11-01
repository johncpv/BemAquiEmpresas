using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Contracts;
using BemAqui.HR.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Cadastro de bens (PAT-01a). A etiqueta <c>PAT-00001</c> vem do <see cref="INumberSequence"/> (tipo <c>PAT</c>, sem ano) na
/// transação da criação. Funcionário, centro, fornecedor e nota são validados pelos Contracts dos donos (CA 13); o valor de
/// aquisição só aparece e só é aceito com <c>assets.edit</c> (CA 2 e 9, ADR 0038 §3).
/// </summary>
public sealed partial class AssetService(
    IAssetsDb db,
    INumberSequence numbers,
    IPermissionChecker permissions,
    IEmployees employees,
    IWorkCenters workCenters,
    IParties parties,
    IIncomingInvoices invoices,
    AttachmentService attachments,
    ITenantContext tenant,
    IEnumerable<IAssetUsageProvider> usages,
    TimeProvider clock)
{
    public const string Module = "assets";
    public const string DocType = "PAT";
    public const string PhotoOwner = "AssetPhoto";
    public const string DocumentOwner = "Asset";
    public const string NotFoundCode = "asset_not_found";
    public const string SerialTakenCode = "asset_serial_taken";
    public const string HasHistoryCode = "asset_has_history";
    public const string DisposedCode = "asset_disposed";
    public const string PlacementByMovementCode = "asset_placement_by_movement";
    public const string MovedByMovementMessage = "Troque o local, o responsável ou o centro por \"Registrar movimentação\".";

    public async Task<PagedList<AssetListItem>> ListAsync(AssetListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var assets = Filtered(db.Assets.AsNoTracking(), query, query.ResponsibleTerminated ? await TerminatedResponsiblesAsync(cancellationToken) : null);
        assets = page.ParseSort() switch
        {
            ("name", true) => assets.OrderByDescending(a => a.Name).ThenByDescending(a => a.Tag),
            ("name", false) => assets.OrderBy(a => a.Name).ThenBy(a => a.Tag),
            ("tag", true) => assets.OrderByDescending(a => a.Tag),
            _ => assets.OrderBy(a => a.Tag),
        };
        var paged = await assets.ToPagedListAsync(page, cancellationToken);
        var items = await ItemsAsync(paged.Items, cancellationToken);
        return new PagedList<AssetListItem>(items, paged.Page, paged.PageSize, paged.Total);
    }

    /// <summary>Filtros da lista (CA 9), também usados por "Imprimir etiquetas" com todos do filtro (PAT-01b CA 3).</summary>
    internal static IQueryable<Asset> Filtered(IQueryable<Asset> assets, AssetListQuery query, IReadOnlyCollection<Guid>? terminatedResponsibles = null)
    {
        // PAT-02 CA 5: só os bens cujo responsável foi desligado (os ids vêm do IEmployees, dono do funcionário).
        if (query.ResponsibleTerminated)
        {
            var terminated = (terminatedResponsibles ?? []).ToList();
            assets = assets.Where(a => a.ResponsibleEmployeeId != null && terminated.Contains(a.ResponsibleEmployeeId.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = "%" + query.Q.Trim() + "%";
            assets = assets.Where(a => EF.Functions.ILike(a.Tag, term) || EF.Functions.ILike(a.Name, term) || (a.SerialNumber != null && EF.Functions.ILike(a.SerialNumber, term)));
        }

        if (query.Type is { } type)
        {
            assets = assets.Where(a => a.Type == type);
        }

        if (query.SiteId is { } site)
        {
            assets = assets.Where(a => a.SiteId == site);
        }

        if (query.WorkCenterId is { } center)
        {
            assets = assets.Where(a => a.WorkCenterId == center);
        }

        if (query.ResponsibleEmployeeId is { } responsible)
        {
            assets = assets.Where(a => a.ResponsibleEmployeeId == responsible);
        }

        // PAT-02 CA 6: o bem baixado some da lista padrão; aparece só no filtro "Baixados".
        assets = query.Status is { } status ? assets.Where(a => a.Status == status) : assets.Where(a => a.Status != AssetStatus.Disposed);
        return assets;
    }

    /// <summary>Responsáveis desligados entre os bens não baixados (CA 5): o RH diz quem está ativo.</summary>
    internal async Task<IReadOnlyCollection<Guid>> TerminatedResponsiblesAsync(CancellationToken ct)
    {
        var ids = await db.Assets.AsNoTracking().Where(a => a.ResponsibleEmployeeId != null && a.Status != AssetStatus.Disposed)
            .Select(a => a.ResponsibleEmployeeId!.Value).Distinct().ToListAsync(ct);
        return ids.Count == 0 ? [] : (await employees.FindAsync(ids, ct)).Where(e => !e.IsActive).Select(e => e.Id).ToList();
    }

    public async Task<Result<AssetDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return asset is null ? NotFound() : await DetailAsync(asset, cancellationToken);
    }

    /// <summary>Novo bem (CA 2): nasce <c>Active</c> com a etiqueta <c>PAT</c> da sequência, na mesma transação.</summary>
    public async Task<Result<AssetDetail>> CreateAsync(AssetInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asset = new Asset { CreatedAt = clock.GetUtcNow(), Status = AssetStatus.Active };
        if (await ApplyAsync(asset, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        asset.AssignTag(await numbers.NextAsync(DocType, cancellationToken));
        db.Assets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(asset, cancellationToken);
    }

    /// <summary>Editar (CA 2): a etiqueta e a situação não mudam por aqui (a situação muda pela manutenção e pela baixa).</summary>
    public async Task<Result<AssetDetail>> UpdateAsync(Guid id, AssetInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        if (input.Version is { } version)
        {
            db.Entry(asset).Property(a => a.Version).OriginalValue = version;
        }

        // PAT-02: o bem baixado não é editado (reative antes); local, responsável e centro só mudam por "Registrar movimentação", para o
        // histórico ficar completo (recomendação aceita, PO 24/10).
        if (asset.Status == AssetStatus.Disposed)
        {
            return Error.Conflict(DisposedCode, "Bem baixado não é alterado. Reative o bem para alterar.");
        }

        if (AssetMovementRules.Changes(AssetPlacement.Of(asset), new AssetPlacement(input.SiteId, input.ResponsibleEmployeeId, input.WorkCenterId)))
        {
            return Error.Validation(PlacementByMovementCode, MovedByMovementMessage, new Dictionary<string, string[]>
            {
                [input.SiteId != asset.SiteId ? "siteId" : input.ResponsibleEmployeeId != asset.ResponsibleEmployeeId ? "responsibleEmployeeId" : "workCenterId"] = [MovedByMovementMessage],
            });
        }

        if (await ApplyAsync(asset, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(asset, cancellationToken);
    }

    /// <summary>
    /// Excluir (CA 12): exclusão lógica só do bem sem histórico (ainda ativo e sem movimentação, manutenção nem custo); com uso, 409 e
    /// o atalho para a baixa (PAT-02).
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        var used = new List<string?>();
        foreach (var usage in usages)
        {
            used.Add(await usage.UsageAsync(id, cancellationToken));
        }

        if (!AssetRules.CanDelete(asset.Status, used))
        {
            return Error.Conflict(HasHistoryCode, "Bem com histórico não pode ser excluído. Use Baixar bem.") with
            {
                Data = new Dictionary<string, object?> { ["assetId"] = asset.Id, ["tag"] = asset.Tag },
            };
        }

        db.Assets.Remove(asset);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Sugestão do centro (CA 3, propositivo): local do último bem do centro e o responsável mais comum entre os bens dele, só entre os
    /// funcionários ativos. O front só aplica enquanto o usuário não mexeu no campo.
    /// </summary>
    public async Task<AssetSuggestionView> SuggestAsync(Guid workCenterId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Assets.AsNoTracking().Where(a => a.WorkCenterId == workCenterId && a.Status != AssetStatus.Disposed)
            .Select(a => new CenterAsset(a.CreatedAt, a.SiteId, a.ResponsibleEmployeeId)).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new AssetSuggestionView(null, null, null, null);
        }

        var active = (await employees.ActiveAsync(cancellationToken)).ToDictionary(e => e.Id);
        var activeSites = await db.AssetSites.AsNoTracking().Where(s => s.IsActive).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var suggestion = AssetRules.Suggest(rows.Where(r => r.SiteId is null || activeSites.ContainsKey(r.SiteId.Value)), active.Keys.ToHashSet());
        return new AssetSuggestionView(
            suggestion.SiteId, suggestion.SiteId is { } s ? activeSites.GetValueOrDefault(s) : null,
            suggestion.ResponsibleEmployeeId, suggestion.ResponsibleEmployeeId is { } r ? active.GetValueOrDefault(r)?.Name : null);
    }

    private async Task<bool> CanEditAsync(CancellationToken ct) => await permissions.HasPermissionAsync(Module, StandardActions.Edit, ct);

    private string StoragePrefix() => (tenant.Tenant?.Id ?? Guid.Empty).ToString("N");

    private async Task<Error?> ApplyAsync(Asset asset, AssetInput input, CancellationToken ct)
    {
        var name = AssetRules.Clean(input.Name);
        if (name is null || name.Length > AssetRules.NameMax)
        {
            return Invalid("name", "Informe o nome do bem, com até 150 caracteres.");
        }

        if (input.Type is not { } type || !Enum.IsDefined(type))
        {
            return Invalid("type", "Escolha o tipo do bem.");
        }

        var (brand, model, serial) = (AssetRules.Clean(input.Brand), AssetRules.Clean(input.Model), AssetRules.Clean(input.SerialNumber));
        foreach (var (field, text, label) in new[] { ("brand", brand, "A marca"), ("model", model, "O modelo"), ("serialNumber", serial, "O número de série") })
        {
            if (text is { Length: > AssetRules.TextMax })
            {
                return Invalid(field, label + " aceita até 80 caracteres.");
            }
        }

        var notes = AssetRules.Clean(input.Notes);
        if (notes is { Length: > AssetRules.NotesMax })
        {
            return Invalid("notes", "A observação aceita até 1.000 caracteres.");
        }

        if (input.AcquisitionValue is { } value)
        {
            if (!await CanEditAsync(ct))
            {
                return Error.Forbidden(Error.Codes.Forbidden, "Informar o valor de aquisição exige editar o patrimônio. Fale com o administrador.");
            }

            if (!AssetRules.IsValidValue(value))
            {
                return Invalid("acquisitionValue", "Informe o valor de aquisição de zero para cima, com até 2 casas.");
            }
        }

        if (await LinksAsync(asset, input, ct) is { } badLink)
        {
            return badLink;
        }

        var (supplier, date, invoiceRef) = (input.SupplierPartyId, input.AcquisitionDate, AssetRules.Clean(input.InvoiceRef));
        if (input.IncomingInvoiceId is { } invoiceId)
        {
            if (invoiceId != asset.IncomingInvoiceId || supplier is null || date is null)
            {
                var found = await invoices.FindAsync([invoiceId], ct);
                var invoice = found.Count > 0 ? found[0] : null;
                if (invoice is null)
                {
                    return Invalid("incomingInvoiceId", "Escolha uma nota de entrada da lista.");
                }

                // R-PRP-3: fornecedor e data de aquisição vêm da nota quando não informados.
                supplier ??= invoice.IssuerPartyId;
                date ??= invoice.IssueDate;
            }

            invoiceRef = null;
        }
        else if (invoiceRef is { Length: > AssetRules.InvoiceRefMax })
        {
            return Invalid("invoiceRef", "A referência da nota aceita até 30 caracteres (número e série).");
        }

        if (date is { } d && (d > BusinessTime.Today(clock) || d.Year < 1900))
        {
            return Invalid("acquisitionDate", "A data de aquisição não pode ser futura.");
        }

        if (supplier is { } partyId && partyId != asset.SupplierPartyId && !await IsActiveSupplierAsync(partyId, ct))
        {
            return Invalid("supplierPartyId", "Escolha um fornecedor ativo da lista.");
        }

        if (serial is not null && await SerialOwnerAsync(asset.Id, brand, model, serial, ct) is { } other)
        {
            return Error.Conflict(SerialTakenCode, $"Já existe o bem {other.Tag} com esta série.") with
            {
                Fields = new Dictionary<string, string[]> { ["serialNumber"] = [$"Já existe o bem {other.Tag} com esta série."] },
                Data = new Dictionary<string, object?> { ["assetId"] = other.Id, ["tag"] = other.Tag },
            };
        }

        asset.Name = name;
        asset.Type = type;
        asset.Brand = brand;
        asset.Model = model;
        asset.SerialNumber = serial;
        asset.AcquisitionDate = date;
        if (await CanEditAsync(ct))
        {
            asset.AcquisitionValue = input.AcquisitionValue;
        }

        asset.IncomingInvoiceId = input.IncomingInvoiceId;
        asset.InvoiceRef = invoiceRef;
        asset.SupplierPartyId = supplier;
        asset.SiteId = input.SiteId;
        asset.ResponsibleEmployeeId = input.ResponsibleEmployeeId;
        asset.WorkCenterId = input.WorkCenterId;
        asset.Notes = notes;
        return null;
    }

    /// <summary>Local, responsável e centro (CA 4, 5 e 13): o novo valor precisa estar ativo; o que já estava no bem continua aceito.</summary>
    private async Task<Error?> LinksAsync(Asset asset, AssetInput input, CancellationToken ct)
    {
        if (input.SiteId is { } site && site != asset.SiteId && !await db.AssetSites.AnyAsync(s => s.Id == site && s.IsActive, ct))
        {
            return Invalid("siteId", "Escolha um local ativo da lista.");
        }

        if (input.ResponsibleEmployeeId is { } employee && employee != asset.ResponsibleEmployeeId && !(await employees.ActiveAsync(ct)).Any(e => e.Id == employee))
        {
            return Invalid("responsibleEmployeeId", "Escolha um funcionário ativo da lista.");
        }

        if (input.WorkCenterId is { } center && center != asset.WorkCenterId && !(await workCenters.ActiveAsync(ct)).Any(c => c.Id == center))
        {
            return Invalid("workCenterId", "Escolha um centro de trabalho ativo da lista.");
        }

        return null;
    }

    private async Task<bool> IsActiveSupplierAsync(Guid partyId, CancellationToken ct) =>
        await parties.FindAsync(partyId, ct) is { IsActive: true, IsDeleted: false } party && party.Roles.Contains(PartyRoleKind.Supplier);

    /// <summary>CA 6: outro bem não excluído com a mesma marca, modelo e série (sem caixa).</summary>
    private async Task<Asset?> SerialOwnerAsync(Guid id, string? brand, string? model, string serial, CancellationToken ct)
    {
        var key = AssetRules.SerialKey(brand, model, serial);
        var same = await db.Assets.AsNoTracking().Where(a => a.Id != id && a.SerialNumber != null && EF.Functions.ILike(a.SerialNumber, AssetRules.LikeExact(serial))).ToListAsync(ct);
        return same.FirstOrDefault(a => AssetRules.SerialKey(a.Brand, a.Model, a.SerialNumber!) == key);
    }

    private async Task<List<AssetListItem>> ItemsAsync(IReadOnlyList<Asset> assets, CancellationToken ct)
    {
        var names = await NamesAsync(assets, ct);
        var photos = await attachments.LatestCleanAsync(PhotoOwner, assets.Select(a => a.Id).ToList(), ct);
        var canEdit = await CanEditAsync(ct);
        return assets.Select(a => new AssetListItem(
            a.Id, a.Tag, a.Name, a.Type, a.Status, a.SerialNumber,
            a.SiteId, a.SiteId is { } s ? names.Sites.GetValueOrDefault(s) : null,
            a.ResponsibleEmployeeId, a.ResponsibleEmployeeId is { } r ? names.Employees.GetValueOrDefault(r)?.Name : null,
            a.ResponsibleEmployeeId is { } t && names.Employees.GetValueOrDefault(t) is { IsActive: false },
            a.WorkCenterId, a.WorkCenterId is { } c ? names.Centers.GetValueOrDefault(c) : null,
            photos.TryGetValue(a.Id, out var photo) ? photo : null,
            canEdit ? a.AcquisitionValue : null)).ToList();
    }

    private async Task<AssetDetail> DetailAsync(Asset a, CancellationToken ct)
    {
        var names = await NamesAsync([a], ct);
        var photos = await attachments.LatestCleanAsync(PhotoOwner, [a.Id], ct);
        var supplier = a.SupplierPartyId is { } p ? await parties.FindAsync(p, ct) : null;
        var invoice = a.IncomingInvoiceId is { } i && await invoices.FindAsync([i], ct) is [var first, ..] ? first : null;
        var employee = a.ResponsibleEmployeeId is { } r ? names.Employees.GetValueOrDefault(r) : null;
        return new AssetDetail(
            a.Id, a.Tag, a.Name, a.Type, a.Status, a.Brand, a.Model, a.SerialNumber, a.AcquisitionDate,
            a.IncomingInvoiceId, invoice is null ? null : new AssetInvoiceView(invoice.Id, invoice.Number, invoice.Series, invoice.IssuerName, invoice.IssueDate),
            a.InvoiceRef, a.SupplierPartyId, supplier is null ? null : supplier.TradeName ?? supplier.LegalName,
            a.SiteId, a.SiteId is { } s ? names.Sites.GetValueOrDefault(s) : null,
            a.ResponsibleEmployeeId, employee?.Name, employee is { IsActive: false },
            a.WorkCenterId, a.WorkCenterId is { } c ? names.Centers.GetValueOrDefault(c) : null,
            a.Notes, photos.TryGetValue(a.Id, out var photo) ? photo : null, a.CreatedAt, a.Version, await NextMaintenanceAsync(a.Id, ct),
            await CanEditAsync(ct) ? a.AcquisitionValue : null);
    }

    private sealed record Names(IReadOnlyDictionary<Guid, string> Sites, IReadOnlyDictionary<Guid, EmployeeSummary> Employees, IReadOnlyDictionary<Guid, string> Centers);

    /// <summary>Nomes de local, responsável e centro numa consulta por fonte (inclusive excluídos, para o bem antigo continuar legível).</summary>
    private async Task<Names> NamesAsync(IReadOnlyList<Asset> assets, CancellationToken ct)
    {
        var siteIds = assets.Select(a => a.SiteId).OfType<Guid>().Distinct().ToList();
        var sites = siteIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.AssetSites.IgnoreQueryFilters().AsNoTracking().Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var employeeIds = assets.Select(a => a.ResponsibleEmployeeId).OfType<Guid>().Distinct().ToList();
        var people = employeeIds.Count == 0 ? new Dictionary<Guid, EmployeeSummary>() : (await employees.FindAsync(employeeIds, ct)).ToDictionary(e => e.Id);
        var centerIds = assets.Select(a => a.WorkCenterId).OfType<Guid>().Distinct().ToList();
        var centers = centerIds.Count == 0 ? new Dictionary<Guid, string>() : (await workCenters.FindAsync(centerIds, ct)).ToDictionary(c => c.Id, c => c.Name);
        return new Names(sites, people, centers);
    }

    internal static Error NotFound() => Error.NotFound(NotFoundCode, "Bem não encontrado.");

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
