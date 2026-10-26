using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>Linha de "Eventos recebidos" (CA 15): sem o corpo.</summary>
public sealed record WebhookEventRow(
    Guid Id,
    string Provider,
    string ProviderName,
    string EventType,
    DateTimeOffset ReceivedAt,
    string Status,
    int Attempts,
    string? LastError,
    bool CanViewContent,
    bool CanReprocess,
    bool ContentPurged);

public sealed record WebhookProviderOption(string Code, string Name);

/// <summary>Página da lista, os provedores que a pessoa vê (filtro) e quantos estão parados (abre o filtro "Parados").</summary>
public sealed record WebhookEventList(PagedList<WebhookEventRow> Page, IReadOnlyList<WebhookProviderOption> Providers, int DeadLettered);

/// <summary>Corpo mascarado (CA 15), com a faixa "Dados pessoais mascarados".</summary>
public sealed record WebhookEventContent(Guid Id, string Provider, string EventType, DateTimeOffset ReceivedAt, string? MaskedJson, bool Purged);

public sealed record WebhookEventFilter(string? Provider, string? Status, DateOnly? From, DateOnly? To);

/// <summary>
/// Configurações → Integrações → Eventos recebidos (FND-16 CA 15 e 16): cada pessoa vê só os provedores cujo módulo dono está
/// liberado e que ela pode ver (<c>{dono}.view</c>); o corpo mascarado exige a aprovação do dono (ou o Proprietário, quando o
/// catálogo não tem a ação) e fica na auditoria como <c>SensitiveRead</c>; "Reprocessar" exige a edição do dono e só vale para
/// evento parado. Evento de provedor que a pessoa não vê (ou de outro tenant, que nem está neste banco) responde 404.
/// </summary>
public sealed class WebhookEventsAdmin(
    BemAquiDbContext db,
    TenantContextHolder tenant,
    WebhookProviderRegistry providers,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IEventPublisher publisher,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const string NoProvider = "Nenhum aviso de sistema externo está liberado para você.";

    private sealed record Access(WebhookProvider Provider, bool Content, bool Reprocess);

    public async Task<Result<WebhookEventList>> ListAsync(WebhookEventFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var access = await AccessAsync(cancellationToken);
        if (access.Error is { } error)
        {
            return error;
        }

        var visible = access.Items!;
        var codes = visible.Keys.ToList();
        var query = db.WebhookEvents.AsNoTracking().Where(e => codes.Contains(e.Provider));
        var deadLettered = await query.CountAsync(e => e.Status == WebhookEventStatus.DeadLettered, cancellationToken);
        if (!string.IsNullOrEmpty(filter.Provider))
        {
            query = query.Where(e => e.Provider == filter.Provider);
        }

        if (Enum.TryParse<WebhookEventStatus>(filter.Status, ignoreCase: false, out var status))
        {
            query = query.Where(e => e.Status == status);
        }

        if (filter.From is { } from)
        {
            var start = Time.BusinessTime.StartOfDayUtc(from);
            query = query.Where(e => e.ReceivedAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = Time.BusinessTime.StartOfDayUtc(to.AddDays(1));
            query = query.Where(e => e.ReceivedAt < end);
        }

        var raw = await query.OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id)
            .Select(e => new { e.Id, e.Provider, e.EventType, e.ReceivedAt, e.Status, e.Attempts, e.LastError, Purged = e.Payload == null })
            .ToPagedListAsync(page, cancellationToken);
        var rows = raw.Items.Select(e =>
        {
            var a = visible[e.Provider];
            return new WebhookEventRow(e.Id, e.Provider, a.Provider.Name, e.EventType, e.ReceivedAt, e.Status.ToString(), e.Attempts, e.LastError,
                a.Content && !e.Purged, a.Reprocess && e.Status == WebhookEventStatus.DeadLettered, e.Purged);
        }).ToList();
        var options = visible.Values.Select(a => new WebhookProviderOption(a.Provider.Code, a.Provider.Name)).OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
        return new WebhookEventList(new PagedList<WebhookEventRow>(rows, raw.Page, raw.PageSize, raw.Total), options, deadLettered);
    }

    public async Task<Result<WebhookEventContent>> ContentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var access = await AccessAsync(cancellationToken);
        if (access.Error is { } error)
        {
            return error;
        }

        var evt = await db.WebhookEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (evt is null || !access.Items!.TryGetValue(evt.Provider, out var a))
        {
            return NotFound();
        }

        if (!a.Content)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Ver o conteúdo do aviso exige a aprovação do módulo dono.");
        }

        await audit.RecordReadAsync(WebhookAudit.Entity, evt.Id.ToString(), cancellationToken);
        return new WebhookEventContent(evt.Id, evt.Provider, evt.EventType, evt.ReceivedAt, evt.Payload is null ? null : JsonPersonalDataMask.MaskJson(evt.Payload), evt.Payload is null);
    }

    public async Task<Result> ReprocessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var access = await AccessAsync(cancellationToken);
        if (access.Error is { } error)
        {
            return error;
        }

        var evt = await db.WebhookEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (evt is null || !access.Items!.TryGetValue(evt.Provider, out var a))
        {
            return NotFound();
        }

        if (!a.Reprocess)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Reprocessar exige a edição do módulo dono.");
        }

        if (evt.Status != WebhookEventStatus.DeadLettered)
        {
            // Sem reenvio enquanto processa (padrão 6 das Fases 4 a 8).
            return Error.Conflict("webhook_event_not_stalled", "O evento não está parado: só o aviso parado pode ser reprocessado.");
        }

        var now = clock.GetUtcNow();
        db.AuditEntries.Add(WebhookAudit.Reprocessed(evt, currentUser.UserId, now));
        evt.Status = WebhookEventStatus.Received;
        evt.Attempts = 0;
        evt.LastError = null;
        evt.Requeues = 0;
        evt.LastQueuedAt = now;
        publisher.Publish(new WebhookReceived(evt.Id, evt.Provider));
        await db.SaveChangesAsync(cancellationToken); // xmin: dois "Reprocessar" ao mesmo tempo, o segundo dá 409
        return Result.Success();
    }

    private sealed record AccessResult(Dictionary<string, Access>? Items, Error? Error);

    private async Task<AccessResult> AccessAsync(CancellationToken cancellationToken)
    {
        var released = providers.All.Where(p => tenant.Resolved?.IsReleased(p.OwnerModule) == true).ToList();
        if (released.Count == 0)
        {
            return new AccessResult(null, Error.Forbidden(Error.Codes.ModuleNotReleased, Web.ModuleEndpoints.NotReleasedMessage));
        }

        var effective = currentUser.UserId is { } userId ? await permissions.GetAsync(userId, cancellationToken) : EffectivePermissions.None;
        var visible = released
            .Where(p => effective.IsOwner || effective.Has(p.ViewPermission))
            .ToDictionary(p => p.Code, p => new Access(
                p,
                p.ContentPermission is { } content ? effective.IsOwner || effective.Has(content) : effective.IsOwner,
                effective.IsOwner || effective.Has(p.EditPermission)), StringComparer.Ordinal);
        return visible.Count == 0
            ? new AccessResult(null, Error.Forbidden(Error.Codes.Forbidden, NoProvider))
            : new AccessResult(visible, null);
    }

    private static Error NotFound() => Error.NotFound(Error.Codes.NotFound, "Evento não encontrado.");
}
