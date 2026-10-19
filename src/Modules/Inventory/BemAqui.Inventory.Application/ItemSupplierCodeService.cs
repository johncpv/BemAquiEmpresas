using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Códigos do fornecedor (CAD-02b CA 8 a 14) e o contrato <see cref="IItemSupplierCodes"/>. O terceiro é validado só por
/// <see cref="IParties"/> (existe e é Fornecedor); toda rota filha confere que o vínculo é do item da URL (IDOR pai/filho).
/// </summary>
public sealed class ItemSupplierCodeService(IInventoryDb db, IParties parties, TimeProvider clock) : IItemSupplierCodes, IPartyUsageProvider
{
    public const string CodeIndex = "ix_item_supplier_codes_party_code";

    public async Task<IReadOnlyList<SupplierOption>> SupplierOptionsAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Supplier, 20, cancellationToken)).Select(p => new SupplierOption(p.Id, Name(p), Cnpj(p))).ToList();

    public async Task<Result<IReadOnlyList<SupplierCodeDetail>>> ListAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        if (!await db.Items.AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        var rows = await db.ItemSupplierCodes.AsNoTracking().Where(c => c.ItemId == itemId).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var details = new List<SupplierCodeDetail>(rows.Count);
        foreach (var row in rows)
        {
            details.Add(ToDetail(row, await parties.FindAsync(row.PartyId, cancellationToken)));
        }

        return details;
    }

    public async Task<Result<SupplierCodeDetail>> CreateAsync(Guid itemId, SupplierCodeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Items.AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        var row = new ItemSupplierCode { ItemId = itemId, CreatedAt = clock.GetUtcNow() };
        return await SaveAsync(row, input, cancellationToken);
    }

    public async Task<Result<SupplierCodeDetail>> UpdateAsync(Guid itemId, Guid codeId, SupplierCodeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await db.ItemSupplierCodes.FirstOrDefaultAsync(c => c.Id == codeId && c.ItemId == itemId, cancellationToken);
        return row is null ? NotFound() : await SaveAsync(row, input, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid itemId, Guid codeId, CancellationToken cancellationToken = default)
    {
        var row = await db.ItemSupplierCodes.FirstOrDefaultAsync(c => c.Id == codeId && c.ItemId == itemId, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        db.ItemSupplierCodes.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<SupplierCodeMatch?> FindAsync(Guid partyId, string supplierCode, CancellationToken cancellationToken = default)
    {
        var code = (supplierCode ?? string.Empty).Trim();
        var party = await parties.FindAsync(partyId, cancellationToken);
        if (party is null || party.IsDeleted || code.Length == 0)
        {
            return null;
        }

        return await (from c in db.ItemSupplierCodes.AsNoTracking()
                      join i in db.Items.AsNoTracking() on c.ItemId equals i.Id
                      where c.PartyId == partyId && c.SupplierCode == code
                      select new SupplierCodeMatch(i.Id, i.Code, i.Name, i.UnitId, c.SupplierUnit, c.ConversionFactor)).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Terceiro com código de fornecedor não é excluído, só inativado (CAD-01a CA 20).</summary>
    public async Task<string?> UsageAsync(Guid partyId, CancellationToken cancellationToken = default) =>
        await db.ItemSupplierCodes.AnyAsync(c => c.PartyId == partyId, cancellationToken) ? "códigos de fornecedor dos itens" : null;

    private async Task<Result<SupplierCodeDetail>> SaveAsync(ItemSupplierCode row, SupplierCodeInput input, CancellationToken cancellationToken)
    {
        var code = (input.SupplierCode ?? string.Empty).Trim();
        var unit = string.IsNullOrWhiteSpace(input.SupplierUnit) ? null : input.SupplierUnit.Trim().ToUpperInvariant();
        if (code.Length is 0 or > 60)
        {
            return ItemRules.Invalid("supplierCode", "Informe o código que o fornecedor usa, com até 60 caracteres.");
        }

        if (unit is { Length: > 10 })
        {
            return ItemRules.Invalid("supplierUnit", "A unidade do fornecedor tem até 10 caracteres.");
        }

        if (input.ConversionFactor <= 0 || decimal.Round(input.ConversionFactor, 6) != input.ConversionFactor || input.ConversionFactor >= 1_000_000_000_000m)
        {
            return ItemRules.Invalid("conversionFactor", "Informe um fator maior que zero, com até 6 casas.");
        }

        var party = await parties.FindAsync(input.PartyId, cancellationToken);
        if (party is null || party.IsDeleted || (!party.IsActive && party.Id != row.PartyId))
        {
            return ItemRules.Invalid("partyId", "Escolha o fornecedor da lista.");
        }

        if (!party.Roles.Contains(PartyRoleKind.Supplier))
        {
            return Error.Validation("party_not_supplier", $"{Name(party)} não é fornecedor. Adicione o papel Fornecedor no cadastro.",
                new Dictionary<string, string[]> { ["partyId"] = [$"{Name(party)} não é fornecedor. Adicione o papel Fornecedor no cadastro."] });
        }

        var taken = await (from c in db.ItemSupplierCodes
                           join i in db.Items.IgnoreQueryFilters() on c.ItemId equals i.Id
                           where c.PartyId == party.Id && c.SupplierCode == code && c.Id != row.Id
                           select i.Code).FirstOrDefaultAsync(cancellationToken);
        if (taken is not null)
        {
            return Taken(code, party, taken);
        }

        row.PartyId = party.Id;
        row.SupplierCode = code;
        row.SupplierUnit = unit;
        row.ConversionFactor = input.ConversionFactor;
        if (db.Entry(row).State == EntityState.Detached)
        {
            db.ItemSupplierCodes.Add(row);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CodeIndex))
        {
            db.Entry(row).State = EntityState.Detached;
            return Taken(code, party, "outro item");
        }

        return ToDetail(row, party);
    }

    private static string Name(PartySummary p) => p.TradeName is { Length: > 0 } t ? t : p.LegalName;

    /// <summary>Só o CNPJ: CPF é dado pessoal e não aparece fora do cadastro do terceiro.</summary>
    private static string? Cnpj(PartySummary p) => p.Kind == PartyKind.PJ ? p.Document : null;

    private static SupplierCodeDetail ToDetail(ItemSupplierCode c, PartySummary? p) =>
        new(c.Id, c.ItemId, c.PartyId, p is null ? "Terceiro não encontrado" : Name(p), p is null ? null : Cnpj(p), p is null || p.IsDeleted, c.SupplierCode, c.SupplierUnit, c.ConversionFactor);

    private static Error Taken(string code, PartySummary party, string item) =>
        Error.Conflict("supplier_code_taken", $"O código {code} do fornecedor {Name(party)} já está ligado ao item {item}.");

    private static Error NotFound() => Error.NotFound("supplier_code_not_found", "Código do fornecedor não encontrado.");
}
