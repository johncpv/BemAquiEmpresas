using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Receita pelo título (FIN-08 CA 3 e 5, ADR 0028 §4), o "provisório" de antes das notas reais: os recebíveis não cancelados, não
/// previstos e fora da implantação, pelo valor, na conta do título, na data de emissão. O sinal (marco com gatilho Assinatura),
/// com o parâmetro <see cref="DreParametersService.DownPaymentAsAdvance"/> ligado (padrão da D4), fica em "Adiantamentos de
/// clientes" e entra na receita no primeiro romaneio expedido do projeto ou, sem romaneio, junto com o título do marco seguinte.
/// Agregação no banco por conta, projeto e dia; consultas do EF com parâmetros.
/// </summary>
public sealed class TitleRevenueRecognition(IFinanceDb db, DreParametersService parameters, IProjectShipments shipments) : IRevenueRecognition
{
    public RevenueBasis Basis => RevenueBasis.Title;

    public async Task<RevenueRecognitionResult> RecognizeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default)
    {
        var advance = await parameters.DownPaymentAsAdvanceAsync(cancellationToken);
        var query = Included(advance, projectIds).Where(r => r.IssueDate >= from && r.IssueDate <= to);
        var grouped = await query.GroupBy(r => new { r.AccountId, r.ProjectId, r.IssueDate })
            .Select(g => new { g.Key.AccountId, g.Key.ProjectId, g.Key.IssueDate, Sum = g.Sum(r => r.Amount) }).ToListAsync(cancellationToken);
        var amounts = grouped.Select(g => new RevenueAmount(g.AccountId, g.ProjectId, g.IssueDate, g.Sum)).ToList();
        var advances = advance ? await DownPaymentsAsync(to, projectIds, cancellationToken) : [];
        amounts.AddRange(advances.Where(a => a.RecognizedOn is { } on && on >= from && on <= to).Select(a => new RevenueAmount(a.AccountId, a.ProjectId, a.RecognizedOn!.Value, a.Amount)));
        return new RevenueRecognitionResult(amounts, advances.Where(a => a.RecognizedOn is null || a.RecognizedOn >= from).ToList());
    }

    public async Task<IReadOnlyList<RevenueDocument>> DocumentsAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        var advance = await parameters.DownPaymentAsAdvanceAsync(cancellationToken);
        var ids = accountIds.ToList();
        var titles = await Included(advance, null).Where(r => ids.Contains(r.AccountId) && r.IssueDate >= from && r.IssueDate <= to)
            .Select(r => new { r.Id, r.Number, r.PartyId, r.AccountId, r.ProjectId, r.IssueDate, r.Amount }).ToListAsync(cancellationToken);
        var documents = titles.Select(r => new RevenueDocument("Receivable", r.Id, r.Number, r.PartyId, r.AccountId, r.ProjectId, r.IssueDate, r.Amount, null, Link(r.Id))).ToList();
        if (advance)
        {
            documents.AddRange((await DownPaymentsAsync(to, null, cancellationToken))
                .Where(a => ids.Contains(a.AccountId) && a.RecognizedOn is { } on && on >= from && on <= to)
                .Select(a => new RevenueDocument("DownPayment", a.ReceivableId, a.Number, a.PartyId, a.AccountId, a.ProjectId, a.RecognizedOn!.Value, a.Amount, a.Note, Link(a.ReceivableId))));
        }

        return documents;
    }

    private static string Link(Guid id) => "/app/financeiro/receber/" + id;

    /// <summary>Recebíveis que entram (CA 3); com o sinal como adiantamento, o sinal sai daqui e vai para <see cref="DownPaymentsAsync"/>.</summary>
    private IQueryable<Receivable> Included(bool advance, IReadOnlyCollection<Guid>? projectIds)
    {
        var query = db.Receivables.AsNoTracking().Where(r => !DreRules.ExcludedReceivableStatuses.Contains(r.Status) && r.Origin != DocumentOrigin.Opening);
        if (projectIds is not null)
        {
            var projects = projectIds.ToList();
            query = query.Where(r => r.ProjectId != null && projects.Contains(r.ProjectId.Value));
        }

        return advance ? query.Where(r => !(r.Origin == DocumentOrigin.Contract && r.TriggerEvent == TriggerEvent.Signature)) : query;
    }

    /// <summary>Sinais emitidos até <paramref name="to"/>, com a data em que viram receita (CA 5, <see cref="DownPaymentRule"/>).</summary>
    private async Task<List<RevenueAdvance>> DownPaymentsAsync(DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken ct)
    {
        var query = db.Receivables.AsNoTracking().Where(r => r.Origin == DocumentOrigin.Contract && r.TriggerEvent == TriggerEvent.Signature
            && !DreRules.ExcludedReceivableStatuses.Contains(r.Status) && r.IssueDate <= to);
        if (projectIds is not null)
        {
            var projects = projectIds.ToList();
            query = query.Where(r => r.ProjectId != null && projects.Contains(r.ProjectId.Value));
        }

        var downs = await query.Select(r => new { r.Id, r.Number, r.PartyId, r.AccountId, r.ProjectId, r.ContractId, r.IssueDate, r.Amount }).ToListAsync(ct);
        if (downs.Count == 0)
        {
            return [];
        }

        var contractIds = downs.Where(d => d.ContractId != null).Select(d => d.ContractId!.Value).Distinct().ToList();
        // Marco seguinte: o primeiro título do contrato, depois do sinal, que já é documento (não previsto nem cancelado).
        var nexts = (await db.Receivables.AsNoTracking().Where(r => r.ContractId != null && contractIds.Contains(r.ContractId.Value) && r.Origin == DocumentOrigin.Contract
                    && r.TriggerEvent != TriggerEvent.Signature && !DreRules.ExcludedReceivableStatuses.Contains(r.Status))
                .Select(r => new { ContractId = r.ContractId!.Value, r.IssueDate, r.Description, r.CreatedAt, r.Number }).ToListAsync(ct))
            .GroupBy(r => r.ContractId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.CreatedAt).ThenBy(r => r.Number, StringComparer.Ordinal).First());
        var projectList = downs.Where(d => d.ProjectId != null).Select(d => d.ProjectId!.Value).Distinct().ToList();
        var shipped = projectList.Count == 0 ? new Dictionary<Guid, DateOnly>() : await shipments.FirstShippedOnAsync(projectList, ct);
        return downs.Select(d =>
        {
            var next = d.ContractId is { } c ? nexts.GetValueOrDefault(c) : null;
            var firstShipped = d.ProjectId is { } p && shipped.TryGetValue(p, out var on) ? on : (DateOnly?)null;
            var recognition = DownPaymentRule.Recognition(firstShipped, next?.IssueDate, next is null ? null : DownPaymentRule.MilestoneName(next.Description));
            // Nunca antes da emissão do próprio sinal.
            DateOnly? recognizedOn = recognition is { } r ? (r.On < d.IssueDate ? d.IssueDate : r.On) : null;
            return new RevenueAdvance(d.Id, d.Number, d.PartyId, d.AccountId, d.ProjectId, d.IssueDate, recognizedOn, d.Amount, recognition?.Note, RevenueBasis.Title);
        }).ToList();
    }
}
