using System.Globalization;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// <see cref="IItemRegistration"/> (CAT-01 CA 3): o Catálogo cria a mercadoria pelas mesmas regras da tela de itens (<see cref="ItemService"/>).
/// Sem código, gera o próximo <c>MER-000001</c> (o maior <c>MER-</c> numérico + 1, inclusive dos excluídos); a gravação simultânea cai no
/// índice único do código e responde 409, como a tela.
/// </summary>
public sealed class ItemRegistrationContract(IInventoryDb db, ItemService items) : IItemRegistration
{
    public const string GeneratedPrefix = "MER-";

    public async Task<IReadOnlyList<UnitChoice>> UnitsAsync(CancellationToken cancellationToken = default) =>
        await db.Units.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Code).Select(u => new UnitChoice(u.Id, u.Code, u.Name)).ToListAsync(cancellationToken);

    public async Task<Result<Guid>> CreateAsync(NewItemData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var code = string.IsNullOrWhiteSpace(data.Code) ? await NextCodeAsync(cancellationToken) : data.Code.Trim();
        var created = await items.CreateAsync(
            new ItemInput(data.Type, code, data.Name, data.UnitId, null, data.Ncm, null, 0, 0m, false, false, null), cancellationToken);
        return created.IsFailure ? created.Error! : created.Value.Id;
    }

    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        // O maior número tem mais dígitos ou, com o mesmo tamanho, vem por último na ordem do texto: uma linha só, pelo índice do código.
        var codes = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Code.StartsWith(GeneratedPrefix))
            .OrderByDescending(i => i.Code.Length).ThenByDescending(i => i.Code).Select(i => i.Code).Take(20).ToListAsync(ct);
        var last = codes.Select(c => long.TryParse(c[GeneratedPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return GeneratedPrefix + (last + 1).ToString("000000", CultureInfo.InvariantCulture);
    }
}
