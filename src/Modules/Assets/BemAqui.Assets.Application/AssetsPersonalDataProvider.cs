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
/// registrou; as OS de manutenção que o usuário abriu, executou ou cancelou (com o texto dele, PAT-03a) e as que o prestador executou. Só etiqueta,
/// nome do bem, datas e o dado dele; o valor da venda só para o comprador.
/// </summary>
[PersonalDataCovers(typeof(AssetMovement), typeof(MaintenanceOrder))]
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

            // PAT-03b: a mão de obra interna que o funcionário lançou nas OS de manutenção (horas; sem o custo-hora, que é do cargo).
            var labor = await (from c in db.MaintenanceOrderCosts.AsNoTracking()
                               join o in db.MaintenanceOrders.AsNoTracking() on c.OrderId equals o.Id
                               join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on o.AssetId equals a.Id
                               where c.EmployeeId == employee
                               orderby c.CreatedAt
                               select new { o.Number, a.Tag, c.Hours, c.CreatedAt }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Mão de obra em manutenções", "assets.maintenance_order_costs", labor.Select(l => PersonalDataRecord.Of(l.CreatedAt,
                ("OS", l.Number), ("Etiqueta", l.Tag), ("Horas", l.Hours?.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))), ("Lançada em", PersonalDataMask.At(l.CreatedAt)))).ToList()));
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

            // PAT-03a: as OS de manutenção que a pessoa abriu, iniciou, concluiu ou cancelou, com o texto que ela escreveu em cada passo.
            var orders = await (from o in db.MaintenanceOrders.AsNoTracking()
                                join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on o.AssetId equals a.Id
                                where o.CreatedBy == user || o.StartedBy == user || o.DoneBy == user || o.CancelledBy == user
                                orderby o.CreatedAt
                                select new { o.Number, a.Tag, o.Type, o.CreatedBy, o.DoneBy, o.CancelledBy, o.Problem, o.WorkDone, o.CancelReason, o.CreatedAt }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("OS de manutenção registradas", "assets.maintenance_orders", orders.Select(o => PersonalDataRecord.Of(o.CreatedAt,
                ("Número", o.Number), ("Etiqueta", o.Tag), ("Tipo", o.Type == MaintenanceOrderType.Preventive ? "Preventiva" : "Corretiva"),
                ("Problema", o.CreatedBy == user ? o.Problem : null), ("O que foi feito", o.DoneBy == user ? o.WorkDone : null),
                ("Motivo do cancelamento", o.CancelledBy == user ? o.CancelReason : null), ("Aberta em", PersonalDataMask.At(o.CreatedAt)))).ToList()));
        }

        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } supplier)
        {
            // PAT-03a: as OS de manutenção executadas pelo prestador (sem os textos de quem registrou).
            var served = await (from o in db.MaintenanceOrders.AsNoTracking()
                                join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on o.AssetId equals a.Id
                                where o.SupplierPartyId == supplier
                                orderby o.ScheduledOn
                                select new { o.Number, a.Tag, a.Name, o.Type, o.Status, o.ScheduledOn, o.CreatedAt }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Manutenções como prestador", "assets.maintenance_orders", served.Select(o => PersonalDataRecord.Of(o.CreatedAt,
                ("Número", o.Number), ("Etiqueta", o.Tag), ("Bem", o.Name), ("Tipo", o.Type == MaintenanceOrderType.Preventive ? "Preventiva" : "Corretiva"),
                ("Situação", o.Status.ToString()), ("Agendada para", Date(o.ScheduledOn)))).ToList()));
        }

        return blocks.Count == 0 ? null : new PersonalDataSection(Module, Title, blocks);
    }

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 2 e 4: os bens e os movimentos ficam (histórico imutável do patrimônio, com etiqueta, datas, locais e valores; o titular
    /// aparece só pelo id). O motivo do movimento é texto livre que pode citar o titular: nos movimentos em que o funcionário entregou ou
    /// recebeu o bem, ou em que o terceiro comprou o bem, ele vira o marcador, como os outros textos livres. Sem obrigação legal, sai já.
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var employee = subject.EmployeeId;
        var party = subject.Kind == PersonalDataSubjectKind.Party ? subject.PartyId : null;
        if (employee is null && party is null)
        {
            return null;
        }

        var plan = new PersonalDataPlanBuilder(Module, Title);
        if (employee is not null)
        {
            plan.Keep("Bens sob responsabilidade: mantidos, com o responsável", "assets.assets", await db.Assets.CountAsync(a => a.ResponsibleEmployeeId == employee, ct));
            plan.Keep("Mão de obra em manutenções: mantida (custo da OS), só com o id do funcionário", "assets.maintenance_order_costs",
                await db.MaintenanceOrderCosts.CountAsync(c => c.EmployeeId == employee, ct));
        }

        var movements = await db.AssetMovements
            .Where(m => (employee != null && (m.FromEmployeeId == employee || m.ToEmployeeId == employee)) || (party != null && m.BuyerPartyId == party))
            .ToListAsync(ct);
        plan.Keep("Movimentações de bens: mantidas, com etiqueta, datas, locais e valores", "assets.asset_movements", movements.Count);
        var withReason = movements.Where(m => !string.IsNullOrWhiteSpace(m.Reason) && m.Reason != AssetMovementRules.TerminatedReason).ToList();
        if (apply)
        {
            foreach (var m in withReason.Where(m => m.Reason != context.Marker))
            {
                db.Entry(m).Property(x => x.Reason).CurrentValue = context.Marker;
            }

            await db.SaveChangesAsync(ct);
        }

        plan.Anonymize("Motivo das movimentações (texto livre)", "assets.asset_movements", withReason.Count);
        var built = plan.Build();
        return built.Items.Count == 0 ? null : built;
    }

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
