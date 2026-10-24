using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Cotações (CMP-02): criar a partir das linhas de SC (que passam a <c>Quoting</c>) ou com itens digitados, incluir e remover
/// fornecedores, registrar as respostas com quem lançou e o anexo acima do limite (ADR 0024 §5), comparar, concluir com o vencedor por
/// linha, cancelar, gerar os pedidos pelo serviço de pedido da CMP-03a e enviar todos para aprovação. Fornecedor validado por
/// <see cref="IParties.HasRoleAsync"/>; fornecedor e linha sempre lidos pela cotação da URL (IDOR). Sem efeito de estoque nem financeiro.
/// </summary>
public sealed partial class RfqService(
    IPurchasingDb db, IItems items, IItemUnits itemUnits, IUnitConversion conversion, IItemSupplierCodes supplierCodes, IParties parties, IPaymentTerms terms,
    INumberSequence numbers, PurchasingParametersService parameters, PurchaseOrderService orders, AttachmentService attachments, IPermissionChecker permissions,
    ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    /// <summary>Dono dos anexos da proposta (ADR 0019 §7, adendo de 21/10/2026): o id é o do fornecedor na cotação (<c>rfq_suppliers.id</c>).</summary>
    public const string AttachmentOwner = "Rfq";

    public const string ClosedCode = "rfq_closed";
    public const string CancelledCode = "rfq_cancelled";
    public const string NotClosedCode = "rfq_not_closed";
    public const string AttachmentRequiredCode = "quote_attachment_required";
    public const string RespondedCode = "rfq_supplier_responded";

    private DateOnly Today => BusinessTime.Today(clock);

    internal static string Link(Guid id) => "/app/compras/cotacoes/" + id;

    /// <summary>
    /// "Criar cotação" (CA 1): nasce <c>Open</c> com o número <c>COT</c>, uma linha por item (quantidade somada das linhas de SC, com o
    /// vínculo a cada uma) e os fornecedores; as linhas de SC passam a <c>Quoting</c>. Sem SC, com os itens digitados (compra pontual).
    /// </summary>
    public async Task<Result<RfqDetail>> CreateAsync(RfqInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var requestIds = input.RequestLineIds ?? [];
        var typed = input.Lines ?? [];
        if (requestIds.Count > 0 && typed.Count > 0)
        {
            return Invalid("lines", "Cote as linhas das solicitações ou digite os itens, não os dois juntos.");
        }

        if (requestIds.Count == 0 && typed.Count == 0)
        {
            return Invalid("lines", "Inclua pelo menos um item.");
        }

        if (requestIds.Count != requestIds.Distinct().Count())
        {
            return Invalid("requestLineIds", "Cada linha de solicitação entra uma vez na cotação.");
        }

        var notes = Clean(input.Notes);
        if (notes is { Length: > RfqRules.MaxNotes })
        {
            return Invalid("notes", $"Escreva a observação com até {RfqRules.MaxNotes} caracteres.");
        }

        var supplierIds = input.SupplierIds ?? [];
        if (supplierIds.Count == 0)
        {
            return Invalid("supplierIds", "Escolha pelo menos um fornecedor.");
        }

        if (supplierIds.Count != supplierIds.Distinct().Count())
        {
            return Invalid("supplierIds", "O mesmo fornecedor não se repete na cotação.");
        }

        if (supplierIds.Count > RfqRules.MaxSuppliers)
        {
            return Invalid("supplierIds", $"Uma cotação tem até {RfqRules.MaxSuppliers} fornecedores.");
        }

        var suppliers = new List<PartySummary>();
        foreach (var partyId in supplierIds)
        {
            var checkedParty = await SupplierAsync(partyId, "supplierIds", cancellationToken);
            if (checkedParty.IsFailure)
            {
                return checkedParty.Error!;
            }

            suppliers.Add(checkedParty.Value);
        }

        var drafts = requestIds.Count > 0 ? await FromRequestsAsync(requestIds, cancellationToken) : await FromTypedAsync(typed, cancellationToken);
        if (drafts.IsFailure)
        {
            return drafts.Error!;
        }

        if (drafts.Value.Count > RfqRules.MaxLines)
        {
            return Invalid("lines", $"Uma cotação tem até {RfqRules.MaxLines} itens.");
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rfq = new Rfq { Number = await numbers.NextAsync(RfqRules.DocType, cancellationToken), Notes = notes, CreatedBy = currentUser.UserId, CreatedAt = now, UpdatedAt = now };
        db.Rfqs.Add(rfq);
        var sequence = 0;
        var touched = new HashSet<Guid>();
        foreach (var draft in drafts.Value)
        {
            var line = new RfqLine { RfqId = rfq.Id, Sequence = ++sequence, ItemId = draft.ItemId, Quantity = draft.Quantity };
            db.RfqLines.Add(line);
            foreach (var request in draft.Requests)
            {
                db.RfqLineRequests.Add(new RfqLineRequest { RfqId = rfq.Id, RfqLineId = line.Id, RequestLineId = request.Id, Quantity = request.Quantity });
                request.Status = PurchaseRequestStatus.Quoting;
                request.UpdatedAt = now;
                touched.Add(request.RequestId);
            }
        }

        var position = 0;
        foreach (var party in suppliers)
        {
            db.RfqSuppliers.Add(new RfqSupplier { RfqId = rfq.Id, Sequence = ++position, PartyId = party.Id, PaymentTermId = party.DefaultPaymentTermId });
        }

        await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(rfq.Id, cancellationToken);
    }

    private sealed record LineDraft(Guid ItemId, decimal Quantity, IReadOnlyList<PurchaseRequestLine> Requests);

    /// <summary>Linhas de SC em aberto, somadas por item, na ordem das SC (CA 1); linha que já não está aberta: 409.</summary>
    private async Task<Result<List<LineDraft>>> FromRequestsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var lines = await (from l in db.PurchaseRequestLines
                           join r in db.PurchaseRequests on l.RequestId equals r.Id
                           where ids.Contains(l.Id)
                           orderby r.Number, l.Sequence
                           select l).ToListAsync(ct);
        if (lines.Count != ids.Count)
        {
            return Error.NotFound("purchase_request_line_not_found", "Linha de solicitação não encontrada. Recarregue.");
        }

        if (lines.Any(l => l.Status != PurchaseRequestStatus.Open))
        {
            return Error.Conflict(PurchaseRequestService.LineNotOpenCode, "Uma linha de solicitação já está em cotação, virou pedido ou foi cancelada. Recarregue.");
        }

        var found = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        if (lines.FirstOrDefault(l => !found.TryGetValue(l.ItemId, out var i) || !i.IsActive || i.IsDeleted) is { } inactive)
        {
            return Invalid("requestLineIds", $"O item {found.GetValueOrDefault(inactive.ItemId)?.Code ?? "da solicitação"} está inativo. Reative o item ou cancele a linha.");
        }

        return lines.GroupBy(l => l.ItemId).Select(g => new LineDraft(g.Key, g.Sum(l => l.Quantity), g.ToList())).ToList();
    }

    /// <summary>Itens digitados (compra pontual, CA 1): ativo, fora Serviço, um por linha, quantidade convertida para a unidade de estoque.</summary>
    private async Task<Result<List<LineDraft>>> FromTypedAsync(IReadOnlyList<RfqLineInput> lines, CancellationToken ct)
    {
        var found = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var drafts = new List<LineDraft>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}].";
            if (!found.TryGetValue(line.ItemId, out var item) || item.IsDeleted || !item.IsActive)
            {
                return Invalid(prefix + "itemId", "Escolha um item ativo.");
            }

            if (item.Type == ItemType.Service)
            {
                return Error.Validation(PurchaseRequestService.ServiceNotAllowedCode, PurchaseRequestService.ServiceNotAllowed,
                    new Dictionary<string, string[]> { [prefix + "itemId"] = [PurchaseRequestService.ServiceNotAllowed] });
            }

            if (drafts.Any(d => d.ItemId == item.Id))
            {
                return Invalid(prefix + "itemId", "Este item já está em outra linha. Some as quantidades numa linha só.");
            }

            if (line.Quantity is <= 0m or > PurchaseOrderRules.MaxQuantity)
            {
                return Invalid(prefix + "quantity", "Informe a quantidade, maior que zero.");
            }

            var quantity = decimal.Round(line.Quantity, 4, MidpointRounding.AwayFromZero);
            if (line.UnitId is { } unit && unit != item.UnitId)
            {
                var converted = await conversion.ConvertAsync(item.Id, line.Quantity, unit, item.UnitId, ct);
                if (converted.IsFailure)
                {
                    return Invalid(prefix + "unitId", converted.Error!.Message);
                }

                quantity = converted.Value;
            }

            drafts.Add(new LineDraft(item.Id, quantity, []));
        }

        return drafts;
    }

    /// <summary>"Incluir fornecedor" (CA 2): só com a cotação aberta; o mesmo fornecedor não se repete (422).</summary>
    public async Task<Result<RfqDetail>> AddSupplierAsync(Guid id, RfqSupplierInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (OpenError(rfq, input.Version) is { } refused)
        {
            return refused;
        }

        var current = await db.RfqSuppliers.Where(s => s.RfqId == id).ToListAsync(cancellationToken);
        if (current.Any(s => s.PartyId == input.PartyId))
        {
            return Invalid("partyId", "Este fornecedor já está na cotação. O mesmo fornecedor não se repete.");
        }

        if (current.Count >= RfqRules.MaxSuppliers)
        {
            return Invalid("partyId", $"Uma cotação tem até {RfqRules.MaxSuppliers} fornecedores.");
        }

        var party = await SupplierAsync(input.PartyId, "partyId", cancellationToken);
        if (party.IsFailure)
        {
            return party.Error!;
        }

        var now = clock.GetUtcNow();
        Touch(rfq!, input.Version, now);
        db.RfqSuppliers.Add(new RfqSupplier
        {
            RfqId = id, Sequence = current.Count == 0 ? 1 : current.Max(s => s.Sequence) + 1, PartyId = party.Value.Id, PaymentTermId = party.Value.DefaultPaymentTermId,
        });
        return await SavedAsync(id, cancellationToken);
    }

    /// <summary>"Remover fornecedor" (CA 2): só com a cotação aberta e sem resposta registrada (a resposta fica no histórico); fica ao menos um.</summary>
    public async Task<Result<RfqDetail>> RemoveSupplierAsync(Guid id, Guid supplierId, RfqVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var supplier = rfq is null ? null : await db.RfqSuppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.RfqId == id, cancellationToken);
        if (rfq is null || supplier is null)
        {
            return SupplierNotFound();
        }

        if (OpenError(rfq, input.Version) is { } refused)
        {
            return refused;
        }

        if (supplier.RespondedAt is not null)
        {
            return Error.Conflict(RespondedCode, "Este fornecedor já tem resposta registrada e não sai da cotação. Se ele desistiu, marque Não cotou.");
        }

        if (await db.RfqSuppliers.CountAsync(s => s.RfqId == id, cancellationToken) <= 1)
        {
            return Invalid("supplierId", "A cotação precisa de pelo menos um fornecedor. Inclua outro antes de remover este.");
        }

        Touch(rfq, input.Version, clock.GetUtcNow());
        db.RfqSuppliers.Remove(supplier);
        return await SavedAsync(id, cancellationToken);
    }

    /// <summary>"Cancelar cotação" (CA 12): só aberta, com motivo; as linhas de SC em cotação voltam a <c>Open</c>.</summary>
    public async Task<Result<RfqDetail>> CancelAsync(Guid id, RfqCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (StateError(rfq) is { } state)
        {
            return state;
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > RfqRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo, com até {RfqRules.MaxReason} caracteres.");
        }

        if (rfq!.Version != input.Version)
        {
            return Stale();
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Touch(rfq, input.Version, now);
        rfq.Status = RfqStatus.Cancelled;
        rfq.CancelReason = reason;
        rfq.CancelledAt = now;
        rfq.CancelledBy = currentUser.UserId;
        var lineIds = await db.RfqLines.Where(l => l.RfqId == id).Select(l => l.Id).ToListAsync(cancellationToken);
        var touched = await ReleaseRequestsAsync(lineIds, now, cancellationToken);
        await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Linhas de SC das linhas da cotação que ainda estão em cotação voltam a <c>Open</c> (cancelar, ou linha sem vencedor na conclusão).</summary>
    private async Task<List<Guid>> ReleaseRequestsAsync(List<Guid> rfqLineIds, DateTimeOffset now, CancellationToken ct)
    {
        if (rfqLineIds.Count == 0)
        {
            return [];
        }

        var requestLineIds = await db.RfqLineRequests.Where(r => rfqLineIds.Contains(r.RfqLineId)).Select(r => r.RequestLineId).ToListAsync(ct);
        var lines = await db.PurchaseRequestLines.Where(l => requestLineIds.Contains(l.Id) && l.Status == PurchaseRequestStatus.Quoting).ToListAsync(ct);
        foreach (var line in lines)
        {
            line.Status = PurchaseRequestStatus.Open;
            line.UpdatedAt = now;
        }

        return lines.Select(l => l.RequestId).Distinct().ToList();
    }

    /// <summary>Terceiro ativo com o papel Fornecedor (<see cref="IParties.HasRoleAsync"/>, CA 13); senão 422 no campo.</summary>
    private async Task<Result<PartySummary>> SupplierAsync(Guid partyId, string field, CancellationToken ct)
    {
        var party = await parties.FindAsync(partyId, ct);
        if (party is null || party.IsDeleted || !party.IsActive)
        {
            return Invalid(field, "Escolha um fornecedor ativo.");
        }

        if (!await parties.HasRoleAsync(partyId, PartyRoleKind.Supplier, ct))
        {
            return Invalid(field, "Este terceiro não tem o papel Fornecedor. Marque o papel no cadastro dele.");
        }

        return party;
    }

    /// <summary>A cotação existe, está aberta e na versão que o usuário viu (CA 14: concluída não muda, 409).</summary>
    private static Error? OpenError(Rfq? rfq, uint version) => StateError(rfq) ?? (rfq!.Version != version ? Stale() : null);

    private static Error? StateError(Rfq? rfq) => rfq is null ? NotFound()
        : rfq.Status == RfqStatus.Closed ? Error.Conflict(ClosedCode, "A cotação está concluída.")
        : rfq.Status == RfqStatus.Cancelled ? Error.Conflict(CancelledCode, "A cotação está cancelada.")
        : null;

    /// <summary>Toda mudança passa pela versão da cotação: a gravação muda o <c>xmin</c> e a gravação concorrente recebe 409.</summary>
    private void Touch(Rfq rfq, uint version, DateTimeOffset now)
    {
        db.Entry(rfq).Property(r => r.Version).OriginalValue = version;
        rfq.UpdatedAt = now;
    }

    private async Task<Result<RfqDetail>> SavedAsync(Guid id, CancellationToken ct) => await SaveAsync(ct) is { } conflict ? conflict : await DetailAsync(id, ct);

    /// <summary>Grava; duas gravações ao mesmo tempo na cotação ou na mesma linha de SC: a segunda recebe 409.</summary>
    private async Task<Error?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }
    }

    internal static Error NotFound() => Error.NotFound("rfq_not_found", "Cotação não encontrada.");

    private static Error SupplierNotFound() => Error.NotFound("rfq_supplier_not_found", "Fornecedor não encontrado nesta cotação.");

    private static Error LineNotFound() => Error.NotFound("rfq_line_not_found", "Linha não encontrada nesta cotação.");

    private static Error Stale() => Error.Conflict("concurrency_conflict", "A cotação mudou desde que você abriu. Recarregue.");

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
