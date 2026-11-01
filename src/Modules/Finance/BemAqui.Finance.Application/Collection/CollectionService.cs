using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Bloco "Lembretes" do título e do cliente (FIN-09 CA 6 e 8): os lembretes (passo, data, situação do e-mail, inclusive ignorado com o
/// motivo), o próximo previsto e "Pausar cobrança" / "Retomar cobrança" (<c>finance.edit</c>, motivo obrigatório, fim opcional, auditado).
/// O título é sempre lido pelo id da URL (IDOR).
/// </summary>
public sealed class CollectionService(
    IFinanceDb db, IParties parties, CollectionPlanner planner, EmailLogReader emailLogs, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const int MaxReasonLength = 200;
    public const string ActivePauseCode = "collection_pause_exists";
    public const string NoPauseCode = "collection_pause_missing";

    private const int RecentLimit = 50;

    private DateOnly Today => BusinessTime.Today(clock);

    public async Task<Result<ReceivableCollectionView>> ForReceivableAsync(Guid receivableId, CancellationToken cancellationToken = default)
    {
        var receivable = await db.Receivables.AsNoTracking().FirstOrDefaultAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable is null)
        {
            return ReceivableService.NotFound();
        }

        var rule = await db.CollectionRules.AsNoTracking().OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        var rows = await db.CollectionNotices.AsNoTracking().Where(n => n.ReceivableId == receivableId).OrderByDescending(n => n.CreatedAt).ToListAsync(cancellationToken);
        var notices = await ViewsAsync(rows, new Dictionary<Guid, string> { [receivable.Id] = receivable.Number }, cancellationToken);
        var pauses = await db.CollectionPauses.AsNoTracking().Where(p => p.ReceivableId == receivableId).OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        var partyPause = await db.CollectionPauses.AsNoTracking().Where(p => p.PartyId == receivable.PartyId && p.ResumedAt == null).OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        var active = pauses.FirstOrDefault(p => p.ActiveOn(Today));
        var party = partyPause.FirstOrDefault(p => p.ActiveOn(Today));

        CollectionNext next;
        if (rule is not { IsActive: true })
        {
            next = new CollectionNext(null, null, null, "A régua está desligada.");
        }
        else if (CollectionEligibility.StateReason(receivable) is { } state)
        {
            next = new CollectionNext(null, null, null, state + ".");
        }
        else if (active is not null || party is not null)
        {
            next = new CollectionNext(null, null, null, active is not null ? "Cobrança pausada neste título." : "Cobrança pausada para o cliente.");
        }
        else
        {
            var planned = await planner.NextForAsync(receivable, rule.Steps, rows.Select(n => n.Step).ToHashSet(), cancellationToken);
            next = planned is { } p ? new CollectionNext(p.Date, p.Step, CollectionRules.StepLabel(p.Step), null) : new CollectionNext(null, null, null, "Nenhum passo da régua falta para este título.");
        }

        return new ReceivableCollectionView(rule?.IsActive ?? false, next, notices, await PauseViewAsync(active, "Título", cancellationToken), await PauseViewAsync(party, "Cliente", cancellationToken),
            await PauseViewsAsync(pauses, "Título", cancellationToken), await permissions.HasPermissionAsync("finance", "edit", cancellationToken));
    }

    public async Task<PartyCollectionView> ForPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
    {
        var rule = await db.CollectionRules.AsNoTracking().OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        var rows = await db.CollectionNotices.AsNoTracking().Where(n => n.PartyId == partyId).OrderByDescending(n => n.CreatedAt).Take(RecentLimit).ToListAsync(cancellationToken);
        var ids = rows.Select(n => n.ReceivableId).Distinct().ToList();
        var numbers = await db.Receivables.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);
        var notices = await ViewsAsync(rows, numbers, cancellationToken);
        var pauses = await db.CollectionPauses.AsNoTracking().Where(p => p.PartyId == partyId).OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        return new PartyCollectionView(rule?.IsActive ?? false, await SentThisMonthAsync(partyId, cancellationToken), notices,
            await PauseViewAsync(pauses.FirstOrDefault(p => p.ActiveOn(Today)), "Cliente", cancellationToken), await PauseViewsAsync(pauses, "Cliente", cancellationToken),
            await permissions.HasPermissionAsync("finance", "edit", cancellationToken));
    }

    /// <summary>"Lembretes enviados no mês" (FIN-06b, CA 8): e-mails <c>Sent</c> dos lembretes do cliente desde o dia 1 (São Paulo).</summary>
    public async Task<int> SentThisMonthAsync(Guid partyId, CancellationToken cancellationToken = default)
    {
        var since = BusinessTime.StartOfDayUtc(new DateOnly(Today.Year, Today.Month, 1));
        var ids = await db.CollectionNotices.AsNoTracking().Where(n => n.PartyId == partyId && n.EmailLogId != null && n.QueuedAt >= since).Select(n => n.EmailLogId!.Value).ToListAsync(cancellationToken);
        return ids.Count == 0 ? 0 : (await emailLogs.StatusAsync(ids, cancellationToken)).Count(s => s.Status == EmailStatus.Sent);
    }

    public async Task<Result<ReceivableCollectionView>> PauseReceivableAsync(Guid receivableId, CollectionPauseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Receivables.AnyAsync(r => r.Id == receivableId, cancellationToken))
        {
            return ReceivableService.NotFound();
        }

        var created = await PauseAsync(input, receivableId, null, cancellationToken);
        return created.IsFailure ? created.Error! : await ForReceivableAsync(receivableId, cancellationToken);
    }

    public async Task<Result<ReceivableCollectionView>> ResumeReceivableAsync(Guid receivableId, CancellationToken cancellationToken = default)
    {
        if (!await db.Receivables.AnyAsync(r => r.Id == receivableId, cancellationToken))
        {
            return ReceivableService.NotFound();
        }

        var resumed = await ResumeAsync(db.CollectionPauses.Where(p => p.ReceivableId == receivableId), cancellationToken);
        return resumed.IsFailure ? resumed.Error! : await ForReceivableAsync(receivableId, cancellationToken);
    }

    public async Task<Result<PartyCollectionView>> PausePartyAsync(Guid partyId, CollectionPauseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await parties.FindAsync(partyId, cancellationToken) is null)
        {
            return Error.NotFound("party_not_found", "Terceiro não encontrado.");
        }

        var created = await PauseAsync(input, null, partyId, cancellationToken);
        return created.IsFailure ? created.Error! : await ForPartyAsync(partyId, cancellationToken);
    }

    public async Task<Result<PartyCollectionView>> ResumePartyAsync(Guid partyId, CancellationToken cancellationToken = default)
    {
        var resumed = await ResumeAsync(db.CollectionPauses.Where(p => p.PartyId == partyId), cancellationToken);
        return resumed.IsFailure ? resumed.Error! : await ForPartyAsync(partyId, cancellationToken);
    }

    private async Task<Result<bool>> PauseAsync(CollectionPauseInput input, Guid? receivableId, Guid? partyId, CancellationToken ct)
    {
        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return ReceivableService.Invalid("reason", "Informe o motivo da pausa (ex.: negociação).");
        }

        if (reason.Length > MaxReasonLength)
        {
            return ReceivableService.Invalid("reason", "O motivo pode ter até 200 caracteres.");
        }

        if (input.Until is { } until && until < Today)
        {
            return ReceivableService.Invalid("until", "O fim da pausa não pode ser antes de hoje.");
        }

        var current = db.CollectionPauses.AsNoTracking().Where(p => p.ResumedAt == null && (p.Until == null || p.Until >= Today));
        current = receivableId is { } r ? current.Where(p => p.ReceivableId == r) : current.Where(p => p.PartyId == partyId);
        if (await current.AnyAsync(ct))
        {
            return Error.Conflict(ActivePauseCode, "A cobrança já está pausada. Retome antes de pausar de novo.");
        }

        db.CollectionPauses.Add(new CollectionPause
        {
            ReceivableId = receivableId, PartyId = partyId, Reason = EmailAddresses.OneLine(reason), From = Today, Until = input.Until, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<Result<bool>> ResumeAsync(IQueryable<CollectionPause> scope, CancellationToken ct)
    {
        var today = Today;
        var active = await scope.Where(p => p.ResumedAt == null && (p.Until == null || p.Until >= today)).ToListAsync(ct);
        if (active.Count == 0)
        {
            return Error.Conflict(NoPauseCode, "A cobrança não está pausada.");
        }

        foreach (var pause in active)
        {
            pause.Resume(currentUser.UserId, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<IReadOnlyList<CollectionNoticeView>> ViewsAsync(IReadOnlyList<CollectionNotice> rows, IReadOnlyDictionary<Guid, string> numbers, CancellationToken ct)
    {
        var ids = rows.Where(n => n.EmailLogId != null).Select(n => n.EmailLogId!.Value).ToList();
        var emails = ids.Count == 0 ? new Dictionary<Guid, EmailLogStatus>() : (await emailLogs.StatusAsync(ids, ct)).ToDictionary(e => e.Id);
        return rows.Select(n =>
        {
            var email = n.EmailLogId is { } id ? emails.GetValueOrDefault(id) : null;
            var (status, reason) = CollectionStatusText.Of(n, email);
            return new CollectionNoticeView(n.Id, n.ReceivableId, numbers.GetValueOrDefault(n.ReceivableId) ?? string.Empty, n.Step, CollectionRules.StepLabel(n.Step), n.ScheduledFor, status, reason, n.CreatedAt, email?.SentAt);
        }).ToList();
    }

    private async Task<CollectionPauseView?> PauseViewAsync(CollectionPause? pause, string target, CancellationToken ct) =>
        pause is null ? null : (await PauseViewsAsync([pause], target, ct))[0];

    private async Task<IReadOnlyList<CollectionPauseView>> PauseViewsAsync(IReadOnlyList<CollectionPause> pauses, string target, CancellationToken ct)
    {
        var views = new List<CollectionPauseView>();
        foreach (var p in pauses)
        {
            var by = p.CreatedBy is { } c ? await users.DisplayNameAsync(c, ct) : null;
            var resumed = p.ResumedBy is { } r ? await users.DisplayNameAsync(r, ct) : null;
            views.Add(new CollectionPauseView(p.Id, target, p.Reason, p.From, p.Until, by, p.CreatedAt, resumed, p.ResumedAt, p.ActiveOn(Today)));
        }

        return views;
    }
}
