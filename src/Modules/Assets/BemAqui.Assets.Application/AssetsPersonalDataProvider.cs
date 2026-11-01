using System.Globalization;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Onde o titular aparece no Patrimônio (LGPD-01a, ADR 0037 §2; PAT-02): o funcionário nos bens de que é responsável e nos movimentos em
/// que entregou ou recebeu o bem (com o motivo, texto livre); o terceiro comprador nas baixas por venda; o usuário nos movimentos que
/// registrou. Só etiqueta, nome do bem, datas e o dado dele; o valor da venda só para o comprador.
/// </summary>
[PersonalDataCovers(typeof(AssetMovement))]
public sealed class AssetsPersonalDataProvider(IAssetsDb db) : IPersonalDataProvider
{
    public string Module => "assets";

    public string Title => "Patrimônio";

    private static readonly Dictionary<AssetMovementKind, string> Kinds = new()
    {
        [AssetMovementKind.Transfer] = "Transferência", [AssetMovementKind.Disposal] = "Baixa", [AssetMovementKind.Reactivation] = "Reativação",
    };

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var blocks = new List<PersonalDataBlock>();
        if (subject.EmployeeId is { } employee)
        {
            var held = await db.Assets.AsNoTracking().Where(a => a.ResponsibleEmployeeId == employee).OrderBy(a => a.Tag)
                .Select(a => new { a.Tag, a.Name, a.Status }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Bens sob responsabilidade", "assets.assets", held.Select(a => PersonalDataRecord.Of(
                ("Etiqueta", a.Tag), ("Bem", a.Name), ("Situação", a.Status.ToString()))).ToList()));

            var moved = await (from m in db.AssetMovements.AsNoTracking()
                               join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on m.AssetId equals a.Id
                               where m.FromEmployeeId == employee || m.ToEmployeeId == employee
                               orderby m.MovedOn, m.CreatedAt
                               select new { a.Tag, a.Name, m.Kind, m.MovedOn, m.FromEmployeeId, m.ToEmployeeId, m.Reason, m.CreatedAt }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Movimentações de bens", "assets.asset_movements", moved.Select(m => PersonalDataRecord.Of(m.CreatedAt,
                ("Etiqueta", m.Tag), ("Bem", m.Name), ("Tipo", Kinds[m.Kind]), ("Data", Date(m.MovedOn)),
                ("Papel", m.FromEmployeeId == m.ToEmployeeId ? "Responsável" : m.ToEmployeeId == employee ? "Recebeu o bem" : "Entregou o bem"), ("Motivo", m.Reason))).ToList()));
        }

        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            var bought = await (from m in db.AssetMovements.AsNoTracking()
                                join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on m.AssetId equals a.Id
                                where m.BuyerPartyId == party
                                orderby m.MovedOn
                                select new { a.Tag, a.Name, m.MovedOn, m.DisposalValue, m.CreatedAt }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Bens comprados", "assets.asset_movements", bought.Select(m => PersonalDataRecord.Of(m.CreatedAt,
                ("Etiqueta", m.Tag), ("Bem", m.Name), ("Data da venda", Date(m.MovedOn)), ("Valor", PersonalDataMask.Money(m.DisposalValue)))).ToList()));
        }

        if (subject.UserId is { } user)
        {
            var registered = await (from m in db.AssetMovements.AsNoTracking()
                                    join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on m.AssetId equals a.Id
                                    where m.CreatedBy == user
                                    orderby m.CreatedAt
                                    select new { a.Tag, m.Kind, m.MovedOn, m.CreatedAt }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Movimentações registradas", "assets.asset_movements", registered.Select(m => PersonalDataRecord.Of(m.CreatedAt,
                ("Etiqueta", m.Tag), ("Tipo", Kinds[m.Kind]), ("Data", Date(m.MovedOn)), ("Registrada em", PersonalDataMask.At(m.CreatedAt)))).ToList()));
        }

        return blocks.Count == 0 ? null : new PersonalDataSection(Module, Title, blocks);
    }

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
