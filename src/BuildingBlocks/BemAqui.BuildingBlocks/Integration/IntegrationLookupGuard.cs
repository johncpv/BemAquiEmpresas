using System.Collections.Concurrent;
using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Integration;

/// <summary>
/// Limites das buscas de cliente pela API de integração (INT-03 CA 9, ADR 0053 §8), na seção <c>Integrations</c>: por minuto e conexão, teto diário
/// por tenant e o número de documentos distintos por hora que dispara o alerta de anomalia.
/// </summary>
public sealed class IntegrationLookupOptions
{
    public const string Section = "Integrations";

    public int LookupPerMinute { get; set; } = 30;

    public int LookupDailyCap { get; set; } = 2000;

    public int LookupAnomalyDocumentsPerHour { get; set; } = 50;
}

/// <summary>Resultado de uma tentativa: permitido ou o tempo até a próxima janela.</summary>
public readonly record struct LookupDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static LookupDecision Ok => new(true, TimeSpan.Zero);
}

/// <summary>
/// Contadores em memória (uma instância hoje, ADR 0053 §11; "Tabelas: nenhuma" da INT-03): janela fixa de 1 minuto por tenant e conexão, contagem do
/// dia (horário de Brasília) por tenant e os hashes de documento distintos da hora por conexão. Nunca guarda o termo, só o hash.
/// </summary>
public sealed class IntegrationLookupGuard(IOptionsMonitor<IntegrationLookupOptions> options, TimeProvider clock)
{
    private const int MaxKeys = 10_000;

    private readonly ConcurrentDictionary<string, Window> _minute = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Window> _daily = new();
    private readonly ConcurrentDictionary<string, HourSet> _documents = new(StringComparer.Ordinal);

    private sealed class Window
    {
        public DateTimeOffset Start;
        public int Count;
    }

    private sealed class HourSet
    {
        public DateTimeOffset Start;
        public HashSet<string> Hashes { get; } = new(StringComparer.Ordinal);
        public bool Alerted;
    }

    private IntegrationLookupOptions Limits => options.CurrentValue;

    /// <summary>Confere sem gastar (o <c>POST /customers</c> recusa antes de gravar quando o limite já acabou).</summary>
    public LookupDecision Peek(Guid tenantId, Guid connectionId) => Decide(tenantId, connectionId, consume: false);

    /// <summary>Gasta uma busca do minuto da conexão e do dia do tenant; acima de qualquer um, recusa sem gastar.</summary>
    public LookupDecision Acquire(Guid tenantId, Guid connectionId) => Decide(tenantId, connectionId, consume: true);

    /// <summary>Registra o hash do documento buscado; true na primeira vez que a conexão passa do limite de distintos na hora (o alerta sai uma vez).</summary>
    public bool TrackDocument(Guid tenantId, Guid connectionId, string hash)
    {
        Prune();
        var now = clock.GetUtcNow();
        var set = _documents.GetOrAdd(tenantId.ToString("N") + "|" + connectionId.ToString("N"), _ => new HourSet { Start = now });
        lock (set)
        {
            if (now - set.Start >= TimeSpan.FromHours(1))
            {
                set.Start = now;
                set.Hashes.Clear();
                set.Alerted = false;
            }

            set.Hashes.Add(hash);
            if (set.Hashes.Count > Limits.LookupAnomalyDocumentsPerHour && !set.Alerted)
            {
                set.Alerted = true;
                return true;
            }

            return false;
        }
    }

    private LookupDecision Decide(Guid tenantId, Guid connectionId, bool consume)
    {
        Prune();
        var now = clock.GetUtcNow();
        var minute = _minute.GetOrAdd(tenantId.ToString("N") + "|" + connectionId.ToString("N"), _ => new Window { Start = now });
        var day = _daily.GetOrAdd(tenantId, _ => new Window { Start = BusinessTime.StartOfDayUtc(BusinessTime.Today(clock)) });
        lock (day)
        {
            lock (minute)
            {
                if (now - minute.Start >= TimeSpan.FromMinutes(1))
                {
                    minute.Start = now;
                    minute.Count = 0;
                }

                var today = BusinessTime.StartOfDayUtc(BusinessTime.Today(clock));
                if (day.Start != today)
                {
                    day.Start = today;
                    day.Count = 0;
                }

                if (day.Count >= Math.Max(1, Limits.LookupDailyCap))
                {
                    return new LookupDecision(false, today.AddDays(1) - now);
                }

                if (minute.Count >= Math.Max(1, Limits.LookupPerMinute))
                {
                    return new LookupDecision(false, minute.Start.AddMinutes(1) - now);
                }

                if (consume)
                {
                    minute.Count++;
                    day.Count++;
                }

                return LookupDecision.Ok;
            }
        }
    }

    private void Prune()
    {
        if (_minute.Count < MaxKeys && _documents.Count < MaxKeys)
        {
            return;
        }

        var limit = clock.GetUtcNow().AddHours(-1);
        foreach (var (key, window) in _minute)
        {
            if (window.Start < limit)
            {
                _minute.TryRemove(key, out _);
            }
        }

        foreach (var (key, set) in _documents)
        {
            if (set.Start < limit)
            {
                _documents.TryRemove(key, out _);
            }
        }
    }
}

/// <summary>
/// Busca de cliente por documento ou telefone pela API (INT-03 CA 9): limite por minuto e teto diário (429 <c>rate_limited</c> com <c>Retry-After</c>),
/// auditoria de cada busca com o termo em hash (origem "Integração {aplicativo} (autorizada por {pessoa})") e o alerta
/// <c>integration_lookup_anomaly</c> aos Proprietários quando a conexão passa de 50 documentos distintos na hora. O <c>409 party_exists</c> do
/// <c>POST /customers</c> conta no mesmo limite (<see cref="RecordConflictAsync"/>).
/// </summary>
public sealed class IntegrationLookupService(
    IntegrationLookupGuard guard, TenantContextHolder tenant, ConnectedAppContext app, IAuditWriter audit, INotificationService notifications, IUserDirectory users,
    TimeProvider clock)
{
    public const string AuditEntity = "IntegrationLookup";
    public const string AnomalyType = "integration_lookup_anomaly";
    public const string Link = "/app/configuracoes/integracoes/conecta";

    /// <summary>Gasta uma busca e audita; null = pode buscar; senão, o 429.</summary>
    public async Task<IResult?> BeginAsync(string kind, string term, CancellationToken cancellationToken = default)
    {
        if (Keys() is not { } keys)
        {
            return IntegrationApi.NotFound();
        }

        var decision = guard.Acquire(keys.Tenant, keys.Connection);
        if (!decision.Allowed)
        {
            return IntegrationApi.RateLimited(decision.RetryAfter);
        }

        await RecordAsync(keys, kind, term, cancellationToken);
        return null;
    }

    /// <summary>Recusa antes de gravar quando o limite já acabou (o <c>POST /customers</c> com documento).</summary>
    public IResult? Exhausted()
    {
        if (Keys() is not { } keys)
        {
            return IntegrationApi.NotFound();
        }

        var decision = guard.Peek(keys.Tenant, keys.Connection);
        return decision.Allowed ? null : IntegrationApi.RateLimited(decision.RetryAfter);
    }

    /// <summary>O 409 <c>party_exists</c> revela que o documento existe: gasta uma busca, audita e conta para a anomalia.</summary>
    public async Task RecordConflictAsync(string document, CancellationToken cancellationToken = default)
    {
        if (Keys() is { } keys)
        {
            guard.Acquire(keys.Tenant, keys.Connection);
            await RecordAsync(keys, "document", document, cancellationToken);
        }
    }

    private (Guid Tenant, Guid Connection)? Keys() =>
        tenant.Tenant is { } t && app.Current is { } grant ? (t.Id, grant.ConnectionId) : null;

    private async Task RecordAsync((Guid Tenant, Guid Connection) keys, string kind, string term, CancellationToken ct)
    {
        var hash = IntegrationData.TermHash(keys.Tenant, kind, term);
        await audit.RecordReadAsync(AuditEntity, keys.Connection.ToString(), kind + ":" + hash, ct);
        if (kind == "document" && guard.TrackDocument(keys.Tenant, keys.Connection, hash))
        {
            var name = app.Current?.AppName ?? "O aplicativo";
            var hour = BusinessTime.ToBusiness(clock.GetUtcNow()).ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
            foreach (var owner in await users.OwnerIdsAsync(ct))
            {
                await notifications.NotifyOnceAsync(owner, AnomalyType,
                    $"{name} consultou muitos CPFs e CNPJs na última hora. Confira se é esperado; se não, desconecte.", Link,
                    AnomalyType + ":" + keys.Connection.ToString("N") + ":" + hour, ct);
            }
        }
    }
}
