using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Platform.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>
/// Fiscal → Notas de entrada (FIS-04a): importação segura do XML do fornecedor, fornecedor pelo CNPJ, de-para dos itens, vínculo ao pedido,
/// conferência e recusa. O Fiscal não grava no Inventory nem no Purchasing: o de-para é gravado pelo <see cref="IItemSupplierCodes"/> e o
/// pedido é lido pelo <see cref="IPurchaseOrderLines"/>. Permissões de rota no endpoint; <c>purchasing.view</c> e <c>purchasing.approve</c>
/// conferidas aqui (CA 11 e 13). Log só com a chave mascarada, o fornecedor (id) e o resultado (CA 14).
/// </summary>
public sealed partial class IncomingInvoiceService(
    IFiscalDb db,
    AttachmentService attachments,
    ITenantContext tenant,
    ITenantProfile catalog,
    INfeTrustAnchors anchors,
    IParties parties,
    IItems items,
    IItemUnits units,
    IItemSupplierCodes supplierCodes,
    IItemSuggestions suggestions,
    IPurchaseOrderLines orders,
    IChartOfAccounts accounts,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    IUserDirectory users,
    TimeProvider clock,
    ILogger<IncomingInvoiceService> logger)
{
    public const string AttachmentOwner = "IncomingInvoice";
    public const string RateLimitPolicy = "fiscal-incoming-import";
    public const string Link = "/app/fiscal/entrada/";
    public const string NotFoundCode = "incoming_invoice_not_found";
    public const string NotEditableCode = "incoming_invoice_not_editable";

    /// <summary>
    /// "Importar XML" de um arquivo (CA 1 a 6): tamanho, triagem do dono <c>IncomingInvoice</c> (allowlist, magic bytes e antivírus) antes
    /// de qualquer leitura, conferência do XML (<see cref="NfeXmlValidator"/>), destinatário igual ao CNPJ do tenant, chave não importada,
    /// fornecedor pelo CNPJ e de-para lembrado. O XML fica guardado como anexo de sistema debaixo do prefixo do tenant.
    /// </summary>
    public async Task<Result<IncomingImportResult>> ImportAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return NfeXmlReader.Invalid("file", "Escolha o arquivo XML da nota.");
        }

        if (content.Length > IncomingInvoiceRules.MaxBytes)
        {
            return Refused(null, NfeXmlReader.Invalid("incoming_xml_size", "O arquivo precisa ter até 2 MB."));
        }

        var screened = await attachments.ScreenAsync(AttachmentOwner, fileName, content, cancellationToken);
        if (screened.IsFailure)
        {
            return Refused(null, screened.Error!);
        }

        var validated = NfeXmlValidator.Validate(content, anchors);
        if (validated.IsFailure)
        {
            return Refused(null, validated.Error!);
        }

        var nfe = validated.Value.Nfe;
        var document = await catalog.DocumentAsync(TenantId(), cancellationToken);
        if (document is null || !string.Equals(nfe.RecipientCnpj, document, StringComparison.Ordinal))
        {
            return Refused(nfe.AccessKey, NfeXmlReader.Invalid("incoming_not_recipient", IncomingInvoiceRules.NotRecipientMessage));
        }

        if (await db.IncomingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.AccessKey == nfe.AccessKey && i.Status != IncomingInvoiceStatus.Rejected, cancellationToken) is { } previous)
        {
            return Refused(nfe.AccessKey, await DuplicateAsync(previous, cancellationToken));
        }

        var party = await parties.FindByDocumentAsync(nfe.IssuerCnpj!, cancellationToken);
        var now = clock.GetUtcNow();
        var invoice = new IncomingInvoice
        {
            AccessKey = nfe.AccessKey, Number = nfe.Number, Series = nfe.Series, IssuedAt = nfe.IssuedAt.ToUniversalTime(), IssuerDocument = nfe.IssuerCnpj!,
            IssuerName = nfe.IssuerName, IssuerTradeName = nfe.IssuerTradeName, IssuerStateRegistration = nfe.IssuerStateRegistration, IssuerAddress = nfe.IssuerAddress,
            Source = IncomingInvoiceSource.Xml, Totals = nfe.Totals, Total = nfe.Totals.Total, Duplicates = nfe.Duplicates, SignatureValid = true,
            SignerDocument = validated.Value.Signer.Document, Protocol = nfe.Protocol!, AuthorizedAt = nfe.AuthorizedAt?.ToUniversalTime(), SefazStatusChecked = false,
            OrderReferences = [.. nfe.OrderReferences], ImportedBy = currentUser.UserId, ImportedAt = now,
        };
        invoice.SetParty(party?.Id, now);
        var lines = nfe.Items.Select(i => new IncomingInvoiceItem
        {
            InvoiceId = invoice.Id, Sequence = i.Sequence, SupplierCode = i.SupplierCode, Description = i.Description, Ncm = i.Ncm, Cfop = i.Cfop, Unit = i.Unit,
            Quantity = i.Quantity, UnitPrice = i.UnitPrice, Amount = i.Amount, Discount = i.Discount, Taxes = i.Taxes, LotNumber = i.LotNumber,
            OrderNumber = i.OrderNumber, OrderItem = i.OrderItem,
        }).ToList();
        var remembered = party is null ? 0 : await ApplyRememberedAsync(party.Id, lines, cancellationToken);

        var file = screened.Value with { FileName = nfe.AccessKey + "-nfe.xml" };
        var stored = await attachments.StoreScreenedAsync(AttachmentOwner, invoice.Id, file, TenantId().ToString("N"), AttachmentOrigin.System, cancellationToken);
        invoice.AttachXml(stored.Id);
        db.IncomingInvoices.Add(invoice);
        db.IncomingInvoiceItems.AddRange(lines);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, AccessKeyIndex))
        {
            db.ChangeTracker.Clear();
            var existing = await db.IncomingInvoices.AsNoTracking().FirstAsync(i => i.AccessKey == nfe.AccessKey && i.Status != IncomingInvoiceStatus.Rejected, cancellationToken);
            return Refused(nfe.AccessKey, await DuplicateAsync(existing, cancellationToken));
        }

        var importedKey = IncomingInvoiceRules.MaskKey(invoice.AccessKey);
        LogImported(logger, importedKey, party?.Id, invoice.Id, lines.Count, remembered);
        return new IncomingImportResult(invoice.Id, invoice.AccessKey, invoice.Number, invoice.Series, invoice.IssuerName, invoice.Total, lines.Count, remembered, party?.Roles.Contains(PartyRoleKind.Supplier) == true);
    }

    /// <summary>Índice único parcial da chave entre as não recusadas (CA 4).</summary>
    public const string AccessKeyIndex = "ux_incoming_invoices_access_key";

    /// <summary>
    /// Fornecedor de novo pelo CNPJ (CA 6), depois de "Cadastrar fornecedor com os dados da nota" ou "Adicionar papel": liga a nota e aplica
    /// o de-para lembrado aos itens ainda sem item.
    /// </summary>
    public async Task<Result<IncomingInvoiceView>> RefreshSupplierAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await EditableAsync(id, cancellationToken);
        if (invoice.IsFailure)
        {
            return invoice.Error!;
        }

        await EnsurePartyAsync(invoice.Value, cancellationToken);
        if (invoice.Value.PartyId is { } partyId)
        {
            var lines = await db.IncomingInvoiceItems.Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
            await ApplyRememberedAsync(partyId, lines.Where(l => l.ItemId is null && !l.NoStockItem).ToList(), cancellationToken);
            await LinkLinesAsync(invoice.Value, lines, onlyMissing: true, cancellationToken);
        }

        invoice.Value.Touch(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// De-para de um item (CA 7 a 9): o nosso item (ativo, fora Serviço) com o fator (informado, o lembrado, o da conversão do item ou 1 na
    /// mesma unidade; sem nenhum, fica pendente), ou "Sem item de estoque" com uma conta de despesa analítica e ativa; a linha do pedido tem
    /// de ser do pedido ligado (422). Item de outra nota: 404 (IDOR).
    /// </summary>
    public async Task<Result<IncomingInvoiceView>> MapItemAsync(Guid id, Guid itemId, IncomingItemMappingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await EditableAsync(id, cancellationToken);
        if (invoice.IsFailure)
        {
            return invoice.Error!;
        }

        var line = await db.IncomingInvoiceItems.FirstOrDefaultAsync(i => i.Id == itemId && i.InvoiceId == id, cancellationToken);
        if (line is null)
        {
            return Error.NotFound("incoming_invoice_item_not_found", "Item da nota não encontrado.");
        }

        await EnsurePartyAsync(invoice.Value, cancellationToken);
        if (request.NoStockItem)
        {
            if (request.ExpenseAccountId is not { } accountId || !await IsExpenseAccountAsync(accountId, cancellationToken))
            {
                return Invalid("expenseAccountId", "Escolha a conta de despesa do plano de contas.");
            }

            line.MarkNoStockItem(accountId);
        }
        else if (request.ItemId is { } stockItemId)
        {
            var found = await items.FindAsync([stockItemId], cancellationToken);
            var item = found.Count > 0 ? found[0] : null;
            if (item is null || !item.IsActive || item.IsDeleted || item.Type == ItemType.Service)
            {
                return Invalid("itemId", "Escolha um item ativo do estoque (serviço entra como Sem item de estoque).");
            }

            if (request.ConversionFactor is { } informed && (informed <= 0 || decimal.Round(informed, 6) != informed || informed >= 1_000_000_000_000m))
            {
                return Invalid("conversionFactor", "Informe um fator maior que zero, com até 6 casas.");
            }

            var factor = request.ConversionFactor ?? await FactorAsync(invoice.Value.PartyId, line, item, cancellationToken);
            line.MapTo(item.Id, factor, request.Suggested ? ItemMappingSource.Suggested : ItemMappingSource.Manual);
            if (request.PoLineId is { } poLineId)
            {
                if (invoice.Value.PurchaseOrderId is not { } orderId || await orders.FindAsync(orderId, cancellationToken) is not { } order || order.Lines.All(l => l.LineId != poLineId))
                {
                    return Invalid("poLineId", "A linha escolhida é de outro pedido. Escolha uma linha do pedido ligado à nota.");
                }

                line.LinkLine(poLineId);
            }
            else
            {
                await LinkLinesAsync(invoice.Value, [line], onlyMissing: false, cancellationToken);
            }
        }
        else
        {
            line.Unmap();
        }

        invoice.Value.Touch(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Pedido da nota (CA 9 e 11): um pedido <c>Approved</c>, <c>Sent</c> ou <c>PartiallyReceived</c> do mesmo fornecedor (as linhas são
    /// ligadas pelos itens) ou "Sem pedido" (a conferência pedirá <c>purchasing.approve</c> e o motivo).
    /// </summary>
    public async Task<Result<IncomingInvoiceView>> SetOrderAsync(Guid id, IncomingOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await EditableAsync(id, cancellationToken);
        if (invoice.IsFailure)
        {
            return invoice.Error!;
        }

        await EnsurePartyAsync(invoice.Value, cancellationToken);
        var lines = await db.IncomingInvoiceItems.Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (request.WithoutOrder)
        {
            invoice.Value.LinkOrder(null, true, now);
            lines.ForEach(l => l.LinkLine(null));
        }
        else
        {
            if (request.PurchaseOrderId is not { } orderId || await orders.FindAsync(orderId, cancellationToken) is not { } order)
            {
                return Invalid("purchaseOrderId", "Escolha o pedido de compra da nota.");
            }

            if (invoice.Value.PartyId is null || order.PartyId != invoice.Value.PartyId || !OpenStatuses.Contains(order.Status))
            {
                return Invalid("purchaseOrderId", "Escolha um pedido aprovado, enviado ou parcialmente recebido deste fornecedor.");
            }

            invoice.Value.LinkOrder(order.Id, false, now);
            lines.ForEach(l => l.LinkLine(null));
            await LinkLinesAsync(invoice.Value, lines, onlyMissing: true, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Conferir nota" (CA 11 e 12): fornecedor cadastrado, todos os itens ligados (ou sem item de estoque) e, com pedido, ligados às linhas
    /// dele; sem pedido, só com <c>purchasing.approve</c> (403) e o motivo. Grava as divergências, quem conferiu e o de-para novo pelo
    /// <see cref="IItemSupplierCodes.RememberAsync"/>, na mesma transação.
    /// </summary>
    public async Task<Result<IncomingInvoiceView>> CheckAsync(Guid id, CheckIncomingInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await EditableAsync(id, cancellationToken);
        if (invoice.IsFailure)
        {
            return invoice.Error!;
        }

        var nota = invoice.Value;
        await EnsurePartyAsync(nota, cancellationToken);
        var reason = request.WithoutOrderReason?.Trim();
        if (nota.WithoutOrder)
        {
            if (!await permissions.HasPermissionAsync("purchasing", StandardActions.Approve, cancellationToken))
            {
                return Error.Forbidden("incoming_without_order_forbidden", IncomingInvoiceRules.WithoutOrderBlockedMessage);
            }

            if (reason is not { Length: >= 3 and <= IncomingInvoice.ReasonMaxLength })
            {
                return Invalid("withoutOrderReason", "Informe o motivo da compra sem pedido (de 3 a 500 caracteres).");
            }
        }

        var lines = await db.IncomingInvoiceItems.Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var supplierOk = nota.PartyId is { } partyId && await parties.HasRoleAsync(partyId, PartyRoleKind.Supplier, cancellationToken);
        if (BlockedReason(supplierOk, nota, lines) is { } blocked)
        {
            return Error.Validation("incoming_invoice_incomplete", blocked);
        }

        var order = nota.PurchaseOrderId is { } orderId ? await orders.FindAsync(orderId, cancellationToken) : null;
        if (!nota.WithoutOrder && (order is null || !OpenStatuses.Contains(order.Status) || order.PartyId != nota.PartyId))
        {
            return Invalid("purchaseOrderId", "O pedido ligado não está mais aberto para recebimento. Escolha outro pedido.");
        }

        var divergences = order is null ? [] : Divergences(lines, order);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var line in lines.Where(l => l.ItemId is not null && l.ConversionFactor is not null && l.MappingSource != ItemMappingSource.Remembered && l.SupplierCode.Length > 0))
        {
            var remembered = await supplierCodes.RememberAsync(new SupplierCodeMemory(nota.PartyId!.Value, line.SupplierCode, line.ItemId!.Value, line.Unit, line.ConversionFactor!.Value), cancellationToken);
            if (remembered.IsFailure)
            {
                return remembered.Error!;
            }
        }

        nota.Check(currentUser.UserId, clock.GetUtcNow(), divergences, nota.WithoutOrder ? reason : null);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var checkedKey = IncomingInvoiceRules.MaskKey(nota.AccessKey);
        LogDecided(logger, checkedKey, nota.PartyId, nota.Id, nameof(IncomingInvoiceStatus.Checked));
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>"Recusar nota" (CA 12): da importada ou conferida (não da recebida), com o motivo; a chave fica livre para importar de novo.</summary>
    public async Task<Result<IncomingInvoiceView>> RejectAsync(Guid id, RejectIncomingInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await db.IncomingInvoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status is not (IncomingInvoiceStatus.Imported or IncomingInvoiceStatus.Checked))
        {
            return Error.Conflict(NotEditableCode, "Esta nota já foi recebida ou recusada e não pode ser recusada.");
        }

        var reason = request.Reason?.Trim();
        if (reason is not { Length: >= 3 and <= IncomingInvoice.ReasonMaxLength })
        {
            return Invalid("reason", "Informe o motivo da recusa (de 3 a 500 caracteres).");
        }

        invoice.Reject(currentUser.UserId, clock.GetUtcNow(), reason);
        await db.SaveChangesAsync(cancellationToken);
        var rejectedKey = IncomingInvoiceRules.MaskKey(invoice.AccessKey);
        LogDecided(logger, rejectedKey, invoice.PartyId, invoice.Id, nameof(IncomingInvoiceStatus.Rejected));
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>O XML guardado (CA 13): só pelo endpoint com <c>fiscal.view</c>, da nota do tenant (anexo autorizado pelo registro).</summary>
    public async Task<Result<AttachmentContent>> XmlAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var attachment = await db.IncomingInvoices.AsNoTracking().Where(i => i.Id == id).Select(i => i.XmlAttachmentId).FirstOrDefaultAsync(cancellationToken);
        return attachment is { } attachmentId ? await attachments.DownloadCheckedAsync(AttachmentOwner, id, attachmentId, cancellationToken) : NotFound();
    }

    private static readonly PurchaseOrderStatus[] OpenStatuses = [PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived];

    private async Task<Result<IncomingInvoice>> EditableAsync(Guid id, CancellationToken ct)
    {
        var invoice = await db.IncomingInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        return invoice.IsEditable ? invoice : Error.Conflict(NotEditableCode, "Esta nota já foi conferida, recebida ou recusada e não muda mais.");
    }

    /// <summary>Liga a nota ao fornecedor do CNPJ quando ainda não está ligada (cadastro feito depois da importação).</summary>
    private async Task EnsurePartyAsync(IncomingInvoice invoice, CancellationToken ct)
    {
        var party = await parties.FindByDocumentAsync(invoice.IssuerDocument, ct);
        if (party?.Id != invoice.PartyId && (invoice.PartyId is null || party is not null))
        {
            invoice.SetParty(party?.Id, clock.GetUtcNow());
        }
    }

    /// <summary>De-para lembrado (CA 7): (fornecedor, código) → item e fator, com a <c>Tag</c> "Lembrado".</summary>
    private async Task<int> ApplyRememberedAsync(Guid partyId, IReadOnlyList<IncomingInvoiceItem> lines, CancellationToken ct)
    {
        var count = 0;
        foreach (var line in lines)
        {
            if (line.SupplierCode.Length == 0 || await supplierCodes.FindAsync(partyId, line.SupplierCode, ct) is not { } match)
            {
                continue;
            }

            var found = await items.FindAsync([match.ItemId], ct);
            var item = found.Count > 0 ? found[0] : null;
            if (item is null || !item.IsActive || item.IsDeleted)
            {
                continue;
            }

            var factor = match.SupplierUnit is null || string.Equals(match.SupplierUnit, line.Unit, StringComparison.OrdinalIgnoreCase)
                ? match.ConversionFactor
                : await FactorAsync(null, line, item, ct);
            line.MapTo(item.Id, factor, ItemMappingSource.Remembered);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Fator da unidade do fornecedor para a de estoque (CA 8): o lembrado para o mesmo item e unidade; 1 na mesma unidade; o da conversão
    /// cadastrada do item (CAD-02b) com o mesmo código; senão null (o campo fica obrigatório).
    /// </summary>
    private async Task<decimal?> FactorAsync(Guid? partyId, IncomingInvoiceItem line, ItemSummary item, CancellationToken ct)
    {
        if (partyId is { } party && line.SupplierCode.Length > 0 && await supplierCodes.FindAsync(party, line.SupplierCode, ct) is { } match && match.ItemId == item.Id
            && (match.SupplierUnit is null || string.Equals(match.SupplierUnit, line.Unit, StringComparison.OrdinalIgnoreCase)))
        {
            return match.ConversionFactor;
        }

        if (string.Equals(item.UnitCode, line.Unit, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        var options = await units.ForItemsAsync([item.Id], ct);
        var option = options.GetValueOrDefault(item.Id)?.FirstOrDefault(o => !o.IsStockUnit && string.Equals(o.Code, line.Unit, StringComparison.OrdinalIgnoreCase));
        return option is null ? null : Math.Round(option.ToStock, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Liga cada item mapeado à linha do pedido ligado com o mesmo item (a do <c>nItemPed</c> primeiro; depois a com saldo).</summary>
    private async Task LinkLinesAsync(IncomingInvoice invoice, IReadOnlyList<IncomingInvoiceItem> lines, bool onlyMissing, CancellationToken ct)
    {
        if (invoice.PurchaseOrderId is not { } orderId || await orders.FindAsync(orderId, ct) is not { } order)
        {
            return;
        }

        foreach (var line in lines.Where(l => l.ItemId is not null && (!onlyMissing || l.PoLineId is null)))
        {
            var candidates = order.Lines.Where(l => l.ItemId == line.ItemId).ToList();
            var bySequence = int.TryParse(line.OrderItem, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) ? candidates.FirstOrDefault(c => c.Sequence == seq) : null;
            line.LinkLine((bySequence ?? candidates.FirstOrDefault(c => c.OpenQuantity > 0) ?? (candidates.Count > 0 ? candidates[0] : null))?.LineId);
        }
    }

    private static List<IncomingDivergence> Divergences(IReadOnlyList<IncomingInvoiceItem> lines, PurchaseOrderWithLines order)
    {
        var result = new List<IncomingDivergence>();
        foreach (var line in lines.Where(l => l.PoLineId is not null && l.StockQuantity is not null && l.StockUnitPrice is not null))
        {
            var poLine = order.Lines.First(l => l.LineId == line.PoLineId);
            var c = IncomingInvoiceRules.Compare(poLine.OpenQuantity, line.StockQuantity!.Value, poLine.UnitPrice, line.StockUnitPrice!.Value);
            if (c.QuantityLevel != DivergenceLevel.Ok || c.PriceLevel != DivergenceLevel.Ok)
            {
                result.Add(new IncomingDivergence(line.Sequence, line.PoLineId, c.QuantityDifference, c.PriceDifference, c.PricePercent, c.QuantityLevel, c.PriceLevel));
            }
        }

        return result;
    }

    /// <summary>O que falta para conferir (o mesmo texto no botão desabilitado e no 422).</summary>
    private static string? BlockedReason(bool supplierOk, IncomingInvoice invoice, IReadOnlyList<IncomingInvoiceItem> lines)
    {
        if (!supplierOk)
        {
            return "Cadastre o fornecedor da nota (ou adicione o papel Fornecedor) antes de conferir.";
        }

        var pending = lines.Count(l => !l.IsResolved);
        if (pending > 0)
        {
            return pending == 1
                ? "Falta 1 item: ligue a um item do estoque (com o fator) ou marque Sem item de estoque."
                : $"Faltam {pending} itens: ligue cada um a um item do estoque (com o fator) ou marque Sem item de estoque.";
        }

        if (!invoice.WithoutOrder && invoice.PurchaseOrderId is null)
        {
            return "Escolha o pedido de compra da nota ou marque Sem pedido.";
        }

        if (!invoice.WithoutOrder && lines.Any(l => !l.NoStockItem && l.PoLineId is null))
        {
            return "Ligue cada item do estoque a uma linha do pedido.";
        }

        return null;
    }

    private async Task<bool> IsExpenseAccountAsync(Guid accountId, CancellationToken ct) =>
        await accounts.FindAsync([accountId], ct) is [{ IsActive: true, AcceptsEntries: true, Type: AccountType.Expense }];

    private async Task<Error> DuplicateAsync(IncomingInvoice previous, CancellationToken ct)
    {
        var who = previous.ImportedBy is { } by ? await users.DisplayNameAsync(by, ct) ?? "um usuário" : "o sistema";
        var when = BusinessTime.ToBusiness(previous.ImportedAt).ToString("dd/MM", CultureInfo.InvariantCulture);
        return Error.Conflict(IncomingInvoiceRules.DuplicateCode, $"Esta nota já foi importada em {when} por {who}.") with
        {
            Data = new Dictionary<string, object?> { ["invoiceId"] = previous.Id, ["link"] = Link + previous.Id },
        };
    }

    private Error Refused(string? accessKey, Error error)
    {
        var refusedKey = IncomingInvoiceRules.MaskKey(accessKey);
        var code = error.Code;
        LogRefused(logger, refusedKey, code);
        return error;
    }

    private Guid TenantId() => tenant.Tenant?.Id ?? Guid.Empty;

    private static Error NotFound() => Error.NotFound(NotFoundCode, "Nota de entrada não encontrada.");

    private static Error Invalid(string field, string message) => Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    [LoggerMessage(Level = LogLevel.Information, Message = "Nota de entrada {AccessKey} importada ({InvoiceId}, fornecedor {PartyId}): {Items} itens, {Remembered} com de-para lembrado.")]
    private static partial void LogImported(ILogger logger, string accessKey, Guid? partyId, Guid invoiceId, int items, int remembered);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nota de entrada {AccessKey} recusada na importação: {Code}.")]
    private static partial void LogRefused(ILogger logger, string accessKey, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nota de entrada {AccessKey} ({InvoiceId}, fornecedor {PartyId}): {Result}.")]
    private static partial void LogDecided(ILogger logger, string accessKey, Guid? partyId, Guid invoiceId, string result);
}
