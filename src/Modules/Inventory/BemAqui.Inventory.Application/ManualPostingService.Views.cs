using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class ManualPostingService
{
    /// <summary>Lançamentos (CA 9, 10): "Aguardando você" é o filtro padrão da tela (padrão 5 da Fase 3).</summary>
    public async Task<PagedList<ManualPostingSummary>> ListAsync(ManualPostingListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var me = currentUser.UserId;
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", cancellationToken);
        var postings = db.ManualPostings.AsNoTracking();
        if (query.Status is { } status)
        {
            postings = postings.Where(p => p.Status == status);
        }

        if (query.Kind is { } kind)
        {
            postings = postings.Where(p => p.Kind == kind);
        }

        if (query.AwaitingMe)
        {
            postings = canApprove ? postings.Where(p => p.Status == ManualPostingStatus.Pending && p.RequestedBy != me) : postings.Where(_ => false);
        }

        var rows = await postings.OrderByDescending(p => p.RequestedAt).ThenByDescending(p => p.Id)
            .Select(p => new
            {
                Posting = p,
                Item = (from l in db.ManualPostingLines where l.PostingId == p.Id orderby l.Sequence join i in db.Items.IgnoreQueryFilters() on l.ItemId equals i.Id select i.Code + " — " + i.Name).FirstOrDefault(),
                Location = (from l in db.ManualPostingLines where l.PostingId == p.Id orderby l.Sequence join loc in db.Locations.IgnoreQueryFilters() on l.LocationId equals loc.Id select loc.Name).FirstOrDefault(),
                Lines = db.ManualPostingLines.Count(l => l.PostingId == p.Id),
            })
            .ToPagedListAsync(page, cancellationToken);
        var names = await NamesAsync(rows.Items.Select(r => r.Posting.RequestedBy), cancellationToken);
        var items = rows.Items.Select(r => new ManualPostingSummary(
            r.Posting.Id, r.Posting.Number, r.Posting.Kind, r.Posting.Status, r.Posting.ApprovalReason, r.Posting.EstimatedValue, r.Item, r.Location, r.Lines,
            Name(names, r.Posting.RequestedBy), r.Posting.RequestedAt, canApprove && r.Posting.Status == ManualPostingStatus.Pending && r.Posting.RequestedBy != me)).ToList();
        return new PagedList<ManualPostingSummary>(items, rows.Page, rows.PageSize, rows.Total);
    }

    /// <summary>Detalhe do lançamento com o impacto da aprovação (saldo e custo médio antes → depois, calculados agora).</summary>
    public async Task<Result<ManualPostingDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var posting = await db.ManualPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (posting is null)
        {
            return NotFound();
        }

        var lines = await LineViewsAsync(db.ManualPostingLines.AsNoTracking().Where(l => l.PostingId == id), cancellationToken);
        var names = await NamesAsync([posting.RequestedBy, posting.DecidedBy], cancellationToken);
        var project = posting.ProjectId is { } projectId ? await projects.FindAsync(projectId, cancellationToken) : null;
        var me = currentUser.UserId;
        var pending = posting.Status == ManualPostingStatus.Pending;
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", cancellationToken);
        var mine = me is not null && posting.RequestedBy == me;
        var alone = pending && canApprove && mine && await IsDecidingAloneAsync(me!.Value, cancellationToken);
        var canDecide = pending && canApprove && (!mine || alone);
        string? blocked = !pending ? null
            : !canApprove ? "Quem decide o lançamento é quem aprova no Estoque."
            : mine && !alone ? "Quem lança não aprova o próprio lançamento." : null;
        var impact = pending && lines.Count == 1 ? await ImpactAsync(posting, lines[0], cancellationToken) : null;
        return new ManualPostingDetail(
            posting.Id, posting.Number, posting.Kind, posting.Status, posting.ApprovalReason, posting.EstimatedValue, posting.Reason, posting.ProjectId, project?.Number,
            posting.CutoffDate, posting.RequestedBy, Name(names, posting.RequestedBy), posting.RequestedAt, Name(names, posting.DecidedBy), posting.DecidedAt, posting.DecisionComment,
            posting.SelfApproved, posting.PostedAt, lines, impact, canDecide, pending && mine, blocked, posting.Version);
    }

    /// <summary>Linha pelo lançamento (CA 31, IDOR): linha de outro lançamento responde como inexistente.</summary>
    public async Task<Result<ManualPostingLineView>> LineAsync(Guid id, Guid lineId, CancellationToken cancellationToken = default)
    {
        var lines = await LineViewsAsync(db.ManualPostingLines.AsNoTracking().Where(l => l.PostingId == id && l.Id == lineId), cancellationToken);
        return lines is [var line] ? line : Error.NotFound("manual_posting_line_not_found", "Linha não encontrada neste lançamento.");
    }

    private async Task<ManualPostingImpact> ImpactAsync(ManualPosting posting, ManualPostingLineView line, CancellationToken ct)
    {
        var here = await db.StockPositions.AsNoTracking().Where(p => p.ItemId == line.ItemId && p.LocationId == line.LocationId)
            .Select(p => new { p.QuantityOnHand, p.QuantityReserved }).FirstOrDefaultAsync(ct);
        var onHand = here?.QuantityOnHand ?? 0m;
        // EST-02 CA 13: a saída e a transferência respeitam o disponível (físico − reservado).
        var available = onHand - (here?.QuantityReserved ?? 0m);
        var total = await db.StockPositions.AsNoTracking().Where(p => p.ItemId == line.ItemId).SumAsync(p => p.QuantityOnHand, ct);
        var average = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == line.ItemId).Select(i => i.AverageCost).FirstOrDefaultAsync(ct);
        return posting.Kind switch
        {
            ManualPostingKind.Receipt or ManualPostingKind.OpeningBalance => new ManualPostingImpact(
                onHand, onHand + line.Quantity, average, AverageCostCalculator.RoundCost(AverageCostCalculator.AfterEntry(total, average, line.Quantity, line.UnitCost ?? average)),
                AverageCostCalculator.LineTotal(line.Quantity, line.UnitCost ?? average), false),
            _ => new ManualPostingImpact(onHand, onHand - line.Quantity, average, average, AverageCostCalculator.LineTotal(line.Quantity, average), available < line.Quantity),
        };
    }

    private async Task<List<ManualPostingLineView>> LineViewsAsync(IQueryable<ManualPostingLine> lines, CancellationToken ct)
    {
        var rows = await (from l in lines
                          join i in db.Items.IgnoreQueryFilters() on l.ItemId equals i.Id
                          join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                          join loc in db.Locations.IgnoreQueryFilters() on l.LocationId equals loc.Id
                          orderby l.Sequence
                          select new
                          {
                              Line = l, ItemCode = i.Code, ItemName = i.Name, UnitCode = u.Code, LocationName = loc.Name,
                              ToLocationName = db.Locations.IgnoreQueryFilters().Where(t => t.Id == l.ToLocationId).Select(t => t.Name).FirstOrDefault(),
                              EnteredUnit = db.Units.IgnoreQueryFilters().Where(e => e.Id == l.EnteredUnitId).Select(e => e.Code).FirstOrDefault(),
                          }).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.Line.ReversedBy), ct);
        return rows.Select(r => new ManualPostingLineView(
            r.Line.Id, r.Line.Sequence, r.Line.ItemId, r.ItemCode, r.ItemName, r.UnitCode, r.Line.LocationId, r.LocationName, r.Line.ToLocationId, r.ToLocationName, r.Line.LotNumber,
            r.Line.Quantity, r.Line.EnteredQuantity, r.EnteredUnit, r.Line.UnitCost, r.Line.MovementId, r.Line.InMovementId, r.Line.ReversedAt, Name(names, r.Line.ReversedBy))).ToList();
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static string? Name(Dictionary<Guid, string?> names, Guid? id) => id is { } key ? names.GetValueOrDefault(key) : null;
}
