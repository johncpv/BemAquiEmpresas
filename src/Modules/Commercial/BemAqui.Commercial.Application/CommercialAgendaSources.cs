using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Detector de Projetos para a Minha pauta (DSH-04 CA 2): aditivo aguardando aprovação, para quem tem <c>projects.approve</c>, menos o
/// autor; a exceção é a mesma do <c>canDecide</c> do aditivo (COM-07, D6): o Proprietário único, ou com o parâmetro ligado, decide o próprio.
/// </summary>
public sealed class ProjectsAgendaSource(ICommercialDb db, IUserDirectory users, IFinanceParameters financeParameters) : IAgendaSource
{
    public const string AmendmentsPending = "projects.amendments_pending";

    private static readonly AgendaCategory[] Declared =
    [
        new(AmendmentsPending, "projects", "projects.approve", AgendaTier.Unblocks, "os aditivos aguardando aprovação"),
    ];

    public string Module => "projects";

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Wants(AmendmentsPending))
        {
            return [];
        }

        var me = request.UserId;
        var pending = db.ContractAmendments.AsNoTracking().Where(a => a.Status == AmendmentStatus.PendingApproval);
        if (!await MayDecideOwnAsync(me, cancellationToken))
        {
            pending = pending.Where(a => a.CreatedBy != me);
        }

        var count = await pending.CountAsync(cancellationToken);
        if (count == 0)
        {
            return [];
        }

        var total = await pending.SumAsync(a => a.ValueDelta, cancellationToken);
        var rows = await pending.OrderBy(a => a.SubmittedAt).Take(AgendaLimits.EntriesPerCategory)
            .Join(db.Contracts.AsNoTracking(), a => a.ContractId, c => c.Id, (a, c) => new { a.Id, a.ContractId, a.Number, a.Description, a.ValueDelta, a.SubmittedAt, Contract = c.Number })
            .ToListAsync(cancellationToken);
        var oldest = rows.Where(r => r.SubmittedAt != null).Select(r => r.SubmittedAt!.Value).DefaultIfEmpty().Min();
        var since = oldest == default ? null : "o mais antigo foi enviado em " + AgendaText.DayMonth(DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(oldest).DateTime), request.Today);
        return
        [
            new AgendaItem(
                AmendmentsPending,
                AgendaText.Count(count, "aditivo de contrato aguarda aprovação", "aditivos de contrato aguardam aprovação"),
                AgendaText.Sentence((total >= 0 ? "+" : string.Empty) + AgendaText.Money(total) + " no valor dos contratos", since),
                null, null, total, AgendaTier.Unblocks, count, "Aprovar",
                [.. rows.OrderBy(r => r.SubmittedAt).Select(r => new AgendaEntry(
                    $"Aditivo {r.Number} do {r.Contract}", Short(r.Description), $"/app/contratos/{r.ContractId}/aditivos/{r.Id}", null, r.ValueDelta))]),
        ];
    }

    /// <summary>Regra do autor (D6): só o Proprietário de um membro, ou com o parâmetro ligado, aprova o próprio aditivo.</summary>
    private async Task<bool> MayDecideOwnAsync(Guid me, CancellationToken ct)
    {
        var owners = await users.OwnerIdsAsync(ct);
        return owners.Contains(me) && (owners.Count == 1 || (await financeParameters.LimitsAsync(ct)).OwnerCanApproveOwn);
    }

    private static string? Short(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Length > 80 ? text[..79] + "…" : text;
}

/// <summary>
/// Detector de Orçamentos para a Minha pauta (DSH-04 CA 2): orçamentos enviados do autor com validade em até 7 dias, para quem tem
/// <c>quotes.edit</c>. O autor é o criador da revisão atual ou da revisão 0 (o mesmo do "Vencendo" do funil).
/// </summary>
public sealed class QuotesAgendaSource(ICommercialDb db, IParties parties) : IAgendaSource
{
    public const string Expiring = "quotes.expiring";

    /// <summary>Validade em até 7 dias (ponto 16 da revisão).</summary>
    public const int HorizonDays = 7;

    private static readonly AgendaCategory[] Declared =
    [
        new(Expiring, "quotes", "quotes.edit", AgendaTier.DueSoon, "os orçamentos vencendo"),
    ];

    public string Module => "quotes";

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Wants(Expiring))
        {
            return [];
        }

        var me = request.UserId;
        var today = request.Today;
        var limit = today.AddDays(HorizonDays);
        var expiring = db.Quotes.AsNoTracking().Where(q => q.IsCurrent && q.Status == QuoteStatus.Sent && q.ValidUntil != null && q.ValidUntil >= today && q.ValidUntil <= limit
            && (q.CreatedBy == me || db.Quotes.Any(o => o.Number == q.Number && o.Revision == 0 && o.CreatedBy == me)));
        var count = await expiring.CountAsync(cancellationToken);
        if (count == 0)
        {
            return [];
        }

        var total = await expiring.SumAsync(q => q.TotalPrice, cancellationToken);
        var first = (await expiring.MinAsync(q => q.ValidUntil, cancellationToken))!.Value;
        var rows = await expiring.OrderBy(q => q.ValidUntil).ThenByDescending(q => q.TotalPrice).Take(AgendaLimits.EntriesPerCategory)
            .Select(q => new { q.Id, q.Number, q.Revision, q.PartyId, q.ValidUntil, q.TotalPrice }).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.PartyId).Distinct().ToList();
        var names = (await parties.FindManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        return
        [
            new AgendaItem(
                Expiring,
                AgendaText.Count(count, "orçamento seu vence em breve", "orçamentos seus vencem em breve"),
                AgendaText.Sentence(AgendaText.Total(total), AgendaText.Due("o primeiro", first, today)),
                "/app/orcamentos/funil", first, total, AgendaTier.DueSoon, count, "Prorrogar validade",
                [.. rows.Select(r => new AgendaEntry(
                    r.Revision > 0 ? $"{r.Number} rev. {r.Revision}" : r.Number, names.GetValueOrDefault(r.PartyId), $"/app/orcamentos/{r.Id}?acao=prorrogar", r.ValidUntil, r.TotalPrice))]),
        ];
    }
}
