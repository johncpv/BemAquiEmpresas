using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class AgingService
{
    public const string NoticeType = "finance.overdue";

    /// <summary>
    /// Aviso de ontem (CA 4): uma notificação consolidada por usuário com <c>finance.edit</c>; sem vencidos, nada; a
    /// <c>dedup_key</c> <c>overdue:{data}</c> impede repetir no mesmo dia, mesmo em paralelo. Devolve quantos avisou.
    /// </summary>
    public async Task<int> NotifyOverdueAsync(CancellationToken cancellationToken = default)
    {
        var yesterday = BusinessTime.Today(clock).AddDays(-1);
        var due = await db.Receivables.AsNoTracking().Where(r => Open.Contains(r.Status) && r.DueDate == yesterday)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Total = g.Sum(r => r.Amount - r.PaidAmount) }).FirstOrDefaultAsync(cancellationToken);
        if (due is null || due.Count == 0)
        {
            return 0;
        }

        var title = due.Count == 1
            ? $"1 título a receber venceu ontem ({ReceivableService.Money(due.Total)})."
            : $"{due.Count} títulos a receber venceram ontem ({ReceivableService.Money(due.Total)}).";
        var sent = 0;
        foreach (var user in await users.ActiveUserIdsAsync(cancellationToken))
        {
            if (!(await permissions.GetAsync(user, cancellationToken)).Has("finance.edit"))
            {
                continue;
            }

            var result = await notifications.NotifyOnceAsync(user, NoticeType, title, "/app/financeiro/receber?situacao=vencido&vencimento=ontem", $"overdue:{yesterday:yyyy-MM-dd}", cancellationToken);
            sent += result.IsSuccess ? 1 : 0;
        }

        return sent;
    }
}
