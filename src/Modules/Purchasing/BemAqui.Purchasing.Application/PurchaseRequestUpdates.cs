using System.Globalization;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>Mudanças comuns da linha e da SC (cancelar, recalcular a situação), usadas pela tela, pela recomposição e pelo job.</summary>
internal static class PurchaseRequestUpdates
{
    /// <summary>Cancela a linha aberta com o motivo (CA 6, 9 e 14).</summary>
    public static void Cancel(PurchaseRequestLine line, string reason, DateTimeOffset now)
    {
        line.Status = PurchaseRequestStatus.Cancelled;
        line.CancelReason = reason;
        line.CancelledAt = now;
        line.UpdatedAt = now;
    }

    /// <summary>
    /// Situação de cada SC tocada derivada das linhas gravadas (regra da story). A SC que ficou toda cancelada leva o motivo da última
    /// linha cancelada.
    /// </summary>
    public static async Task RefreshStatusAsync(IPurchasingDb db, IReadOnlyCollection<Guid> requestIds, DateTimeOffset now, CancellationToken ct, Guid? by = null)
    {
        var saved = await db.PurchaseRequestLines.AsNoTracking().Where(l => requestIds.Contains(l.RequestId))
            .Select(l => new { l.Id, l.RequestId, l.Status, l.CancelReason, l.CancelledAt }).ToListAsync(ct);

        // As linhas mudadas e ainda não gravadas (pedido e cotação mudam a linha e a SC na mesma gravação) valem pelo que está na memória.
        var local = db.PurchaseRequestLines.Local.Where(l => requestIds.Contains(l.RequestId)).ToDictionary(l => l.Id);
        var lines = saved.Select(l => local.TryGetValue(l.Id, out var t) ? new { l.Id, t.RequestId, t.Status, t.CancelReason, t.CancelledAt } : l)
            .Concat(local.Values.Where(t => saved.All(s => s.Id != t.Id)).Select(t => new { t.Id, t.RequestId, t.Status, t.CancelReason, t.CancelledAt }))
            .ToList();
        var requests = await db.PurchaseRequests.Where(r => requestIds.Contains(r.Id)).ToListAsync(ct);
        foreach (var request in requests)
        {
            var mine = lines.Where(l => l.RequestId == request.Id).ToList();
            var status = PurchaseRequestRules.DeriveStatus(mine.Select(l => l.Status));
            if (status == request.Status)
            {
                continue;
            }

            request.Status = status;
            request.UpdatedAt = now;
            if (status == PurchaseRequestStatus.Cancelled && request.CancelledAt is null)
            {
                request.CancelledAt = now;
                request.CancelledBy = by;
                request.CancelReason = mine.OrderByDescending(l => l.CancelledAt).Select(l => l.CancelReason).FirstOrDefault();
            }
        }
    }
}

/// <summary>Formatos de texto de Compras (avisos e notificações em pt-BR).</summary>
public static class PurchaseFormat
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Quantidade em pt-BR, até 4 casas ("4,5").</summary>
    public static string Quantity(decimal value) => value.ToString("#,##0.####", PtBr);

    /// <summary>Quantidade estável para chaves de deduplicação.</summary>
    public static string Key(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
