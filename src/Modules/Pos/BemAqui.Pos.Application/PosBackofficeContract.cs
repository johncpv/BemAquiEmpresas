using System.Text.Json;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// Leituras do PDV para a retaguarda de vendas (PDV-07, <see cref="IPosBackoffice"/>). A trilha procura os eventos da venda só no caixa dela (índice
/// por caixa) e confere o id da venda no conteúdo; os eventos internos da venda saem do próprio evento <c>pos.sale</c>.
/// </summary>
public sealed class PosBackofficeContract(IPosDb db, IRegisterDevices devices) : IPosBackoffice
{
    private static readonly string[] TrailKinds = [PosEventIngestion.SaleKind, PosEventIngestion.CancelKind, PosEventIngestion.PrintKind];

    public async Task<PosSaleTrail> SaleTrailAsync(Guid saleId, Guid? registerId, CancellationToken cancellationToken = default)
    {
        var approvals = await db.SupervisorApprovals.AsNoTracking().Where(a => a.SaleId == saleId).OrderBy(a => a.ApprovedAt)
            .Select(a => new PosTrailApproval(a.Id, a.Action.ToString(), a.OperatorId, a.SupervisorId, a.Reason, a.BeforeAmount, a.AfterAmount, a.Online, a.ApprovedAt))
            .ToListAsync(cancellationToken);
        var prints = await db.PrintRecords.AsNoTracking().Where(p => p.SaleId == saleId).OrderBy(p => p.PrintedAt)
            .Select(p => new PosTrailPrint(p.Id, p.Kind.ToString(), p.Copy, p.OperatorId, p.PrintedAt))
            .ToListAsync(cancellationToken);
        var divergences = await db.SyncDivergences.AsNoTracking().Where(d => d.SaleId == saleId).OrderBy(d => d.OccurredAt)
            .Select(d => new PosTrailDivergence(d.Id, d.Kind.ToString(), d.Status.ToString(), d.Quarantined, d.Summary, d.OccurredAt))
            .ToListAsync(cancellationToken);
        if (registerId is not { } register)
        {
            return new PosSaleTrail([], [], approvals, prints, divergences);
        }

        var needle = "%" + saleId.ToString() + "%";
        var events = await db.DeviceEvents.AsNoTracking()
            .Where(e => e.RegisterId == register && TrailKinds.Contains(e.Kind) && EF.Functions.Like(e.Payload, needle))
            .OrderBy(e => e.DeviceEpoch).ThenBy(e => e.Seq)
            .Select(e => new { e.EventId, e.Kind, e.OccurredAt, e.ReceivedAt, e.Outcome, e.ChainOk, e.Payload })
            .Take(50).ToListAsync(cancellationToken);
        var syncs = new List<PosTrailSync>();
        var deviceEvents = new List<PosTrailEvent>();
        foreach (var e in events)
        {
            if (!Read(e.Payload, saleId, e.Kind == PosEventIngestion.SaleKind ? deviceEvents : null))
            {
                continue;
            }

            syncs.Add(new PosTrailSync(e.EventId, e.Kind, e.OccurredAt, e.ReceivedAt, e.Outcome.ToString(), e.ChainOk));
        }

        return new PosSaleTrail(deviceEvents.OrderBy(d => d.At).ToList(), syncs, approvals, prints, divergences);
    }

    public async Task<IReadOnlyList<PosOpenSession>> OpenSessionsAsync(CancellationToken cancellationToken = default)
    {
        var open = await (
            from s in db.CashSessions.AsNoTracking()
            where s.Status == CashSessionStatus.Open
            join r in db.Registers.AsNoTracking() on s.RegisterId equals r.Id
            join st in db.Stores.AsNoTracking() on r.StoreId equals st.Id
            where st.IsActive
            orderby s.OpenedAt
            select new
            {
                s.Id, StoreId = st.Id, StoreName = st.Name, RegisterId = r.Id, r.Number, s.OperatorId, s.OpenedAt, r.ActiveDeviceId, st.CancelAlertPercent,
                LastEvent = db.DeviceEvents.Where(e => e.RegisterId == r.Id).Max(e => (DateTimeOffset?)e.ReceivedAt),
            }).ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        var deviceIds = open.Where(o => o.ActiveDeviceId is not null).Select(o => o.ActiveDeviceId!.Value).Distinct().ToList();
        var info = deviceIds.Count == 0 ? new Dictionary<Guid, RegisterDeviceInfo>() : await devices.FindAsync(deviceIds, cancellationToken);
        return open.Select(o =>
        {
            var device = o.ActiveDeviceId is { } d ? info.GetValueOrDefault(d) : null;
            var last = new[] { o.LastEvent, device?.LastSyncAt, device?.LastSeenAt }.Where(x => x is not null).Max();
            return new PosOpenSession(o.Id, o.StoreId, o.StoreName, o.RegisterId, o.Number, o.OperatorId, o.OpenedAt, last, o.CancelAlertPercent);
        }).ToList();
    }

    /// <summary>Confere que o evento é da venda e, no <c>pos.sale</c>, junta os eventos internos (tipo, instante e operador).</summary>
    private static bool Read(string payload, Guid saleId, List<PosTrailEvent>? into)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || PosSaleReplay.GuidOf(root, "saleId") != saleId)
            {
                return false;
            }

            if (into is not null && root.TryGetProperty("events", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray().Take(500))
                {
                    if (PosSaleReplay.DateOf(item, "at") is not { } at)
                    {
                        continue;
                    }

                    var data = item.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
                    var type = PosSaleReplay.StringOf(item, "type") ?? "?";
                    into.Add(new PosTrailEvent(at, type.Length > 40 ? type[..40] : type, data.ValueKind == JsonValueKind.Object ? PosSaleReplay.GuidOf(data, "operatorId") : null));
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
