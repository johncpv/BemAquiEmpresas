using System.Collections.Concurrent;
using System.Net;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Security;
using Microsoft.AspNetCore.SignalR;

namespace BemAqui.Host.Realtime;

/// <summary>
/// Hub do Painel da TV (PNL-01a CA 12 a 15, ADR 0030 §3): só o principal de quiosque (esquema <c>Kiosk</c>). Os grupos vêm das claims,
/// nunca do cliente: <c>{tenantId}:production</c> (o <c>OrderBoardChanged</c> do Kanban, só com o id) e <c>{tenantId}:kiosk:{kioskId}</c>
/// (mensagens de controle <c>KioskConfigChanged</c> e <c>KioskRevoked</c>). Não tem método de cliente para servidor.
/// </summary>
public sealed class KioskHub(KioskConnectionRegistry registry) : Hub
{
    public const string Path = BemAqui.BuildingBlocks.Web.KioskEndpoints.HubPath;
    public const string OrderBoardChanged = ProductionHub.OrderBoardChanged;

    /// <summary>Plano mudou (PLN-01b CA 4): o <c>SignalRProductionBoardPush</c> manda também às TVs (PNL-01b CA 8).</summary>
    public const string ScheduleChanged = "ScheduleChanged";
    public const string KioskConfigChanged = "KioskConfigChanged";
    public const string KioskRevoked = "KioskRevoked";

    public static string ProductionGroup(Guid tenantId) => PingHub.Group(tenantId, ProductionHub.ProductionPanel);

    public static string ScreenGroup(Guid tenantId, Guid kioskId) => PingHub.Group(tenantId, "kiosk:" + kioskId.ToString("N"));

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.Identity?.AuthenticationType != KioskClaims.Scheme
            || !Guid.TryParse(user.FindFirst(KioskClaims.Tenant)?.Value, out var tenantId)
            || !Guid.TryParse(user.FindFirst(KioskClaims.KioskId)?.Value, out var kioskId)
            || !Guid.TryParse(user.FindFirst(KioskClaims.SessionId)?.Value, out var sessionId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ProductionGroup(tenantId), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, ScreenGroup(tenantId, kioskId), Context.ConnectionAborted);
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress;
        registry.Add(new KioskConnection(tenantId, kioskId, sessionId, ip, Context));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed record KioskConnection(Guid TenantId, Guid KioskId, Guid SessionId, IPAddress? Ip, HubCallerContext Context);

/// <summary>Conexões abertas do hub do quiosque, para fechar as de uma tela (revogação) e revalidar a cada 30 s. Uma instância da API.</summary>
public sealed class KioskConnectionRegistry
{
    private readonly ConcurrentDictionary<string, KioskConnection> _connections = new(StringComparer.Ordinal);

    public void Add(KioskConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connections[connection.Context.ConnectionId] = connection;
    }

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlyList<KioskConnection> Snapshot() => [.. _connections.Values];

    public int CountFor(Guid tenantId, Guid kioskId) => _connections.Values.Count(c => c.TenantId == tenantId && c.KioskId == kioskId);

    public void Close(KioskConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (_connections.TryRemove(connection.Context.ConnectionId, out _))
        {
            connection.Context.Abort();
        }
    }

    public void CloseFor(Guid tenantId, Guid kioskId)
    {
        foreach (var connection in _connections.Values.Where(c => c.TenantId == tenantId && c.KioskId == kioskId).ToList())
        {
            Close(connection);
        }
    }
}

/// <summary>Fim da sessão da TV: o motivo e a empresa ("Esta tela foi desligada por {empresa}."), nunca outro dado.</summary>
public sealed record KioskEndedMessage(string Reason, string CompanyName);

/// <summary>
/// Mensagens de controle da TV (CA 4, 14 e 15): configuração nova ao grupo da tela; no fim da sessão, <c>KioskRevoked</c> e as conexões
/// da tela fechadas logo depois (o atraso curto deixa a mensagem sair; se ela se perder, a reconexão recebe 401 com o motivo).
/// </summary>
public sealed class SignalRKioskRealtime(IHubContext<KioskHub> hub, KioskConnectionRegistry registry) : IKioskRealtime
{
    // Com long polling, o aviso só chega na próxima busca da TV; 2 s dão folga para ele sair antes de a conexão fechar.
    public static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(2);

    public Task ConfigChangedAsync(Guid tenantId, Guid kioskId, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(KioskHub.ScreenGroup(tenantId, kioskId)).SendAsync(KioskHub.KioskConfigChanged, cancellationToken);

    public async Task EndedAsync(Guid tenantId, Guid kioskId, KioskEndReason reason, string companyName, CancellationToken cancellationToken = default)
    {
        await hub.Clients.Group(KioskHub.ScreenGroup(tenantId, kioskId)).SendAsync(KioskHub.KioskRevoked, Message(reason, companyName), CancellationToken.None);
        _ = Task.Delay(CloseDelay, CancellationToken.None).ContinueWith(_ => registry.CloseFor(tenantId, kioskId), TaskScheduler.Default);
    }

    public static KioskEndedMessage Message(KioskEndReason reason, string companyName) => new(reason switch
    {
        KioskEndReason.Revoked => "revoked",
        KioskEndReason.Unavailable => "unavailable",
        _ => "session_ended",
    }, companyName);

    public async Task EndConnectionAsync(KioskConnection connection, KioskEndReason reason, string companyName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await hub.Clients.Client(connection.Context.ConnectionId).SendAsync(KioskHub.KioskRevoked, Message(reason, companyName), CancellationToken.None);
        _ = Task.Delay(CloseDelay, CancellationToken.None).ContinueWith(_ => registry.Close(connection), TaskScheduler.Default);
    }
}

public sealed class KioskRealtimeOptions
{
    public const string Section = "Kiosk";

    /// <summary>Revalidação das conexões abertas do hub (ADR 0030 §2): a cada 30 s.</summary>
    public int RevalidateSeconds { get; set; } = 30;
}

/// <summary>
/// Revalida as TVs conectadas a cada 30 s (CA 14 e 15): tenant suspenso ou módulo <c>kiosk</c> não liberado ("Painel indisponível"),
/// tela revogada ou sessão vencida fecham a conexão. A revogação manual já fecha na hora; esta é a rede de segurança.
/// </summary>
public sealed partial class KioskHubRevalidator(
    KioskConnectionRegistry registry,
    SignalRKioskRealtime realtime,
    ITenantResolver resolver,
    ITenantScopeFactory scopes,
    Microsoft.Extensions.Options.IOptions<KioskRealtimeOptions> options,
    ILogger<KioskHubRevalidator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.RevalidateSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RevalidateAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revalidação das TVs conectadas falhou; tenta de novo no próximo ciclo")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    public async Task RevalidateAsync(CancellationToken cancellationToken)
    {
        foreach (var byTenant in registry.Snapshot().GroupBy(c => c.TenantId))
        {
            var resolved = await resolver.FindByIdAsync(byTenant.Key, cancellationToken);
            var company = resolved is null ? "a empresa" : resolved.TradeName ?? resolved.LegalName;
            if (resolved is null || resolved.Tenant.Status != TenantStatus.Active || !resolved.IsReleased("kiosk"))
            {
                foreach (var connection in byTenant)
                {
                    await realtime.EndConnectionAsync(connection, KioskEndReason.Unavailable, company);
                }

                continue;
            }

            await using var scope = await scopes.CreateScopeAsync(resolved.Tenant, cancellationToken);
            var validator = scope.ServiceProvider.GetRequiredService<IKioskSessionValidator>();
            foreach (var connection in byTenant)
            {
                var check = await validator.CheckSessionAsync(connection.SessionId, connection.Ip, cancellationToken);
                if (check.Status != KioskSessionStatus.Valid)
                {
                    await realtime.EndConnectionAsync(connection, check.Status == KioskSessionStatus.Revoked ? KioskEndReason.Revoked : KioskEndReason.SessionEnded, company);
                }
            }
        }
    }
}
