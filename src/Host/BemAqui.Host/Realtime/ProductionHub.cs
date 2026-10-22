using System.Collections.Concurrent;
using System.Security.Claims;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Host.Security;
using BemAqui.Production.Application;
using Microsoft.AspNetCore.SignalR;

namespace BemAqui.Host.Realtime;

/// <summary>
/// Hub da Produção (PRD-02 CA 10, 16, 17): primeiro hub de negócio, base do Kanban, do <c>/fab</c> (PRD-03a) e do painel da TV
/// (PNL-01). Os grupos vêm só do servidor: o tenant da claim <c>tid</c> (conferida contra o host na conexão) e as permissões
/// efetivas do usuário; com <c>production.view</c> entra em <c>{tenantId}:production</c>, com <c>shopfloor.view</c> em
/// <c>{tenantId}:shopfloor</c>. Não tem método de servidor: o cliente só recebe <c>OrderBoardChanged</c> com o id da OS.
/// </summary>
public sealed class ProductionHub(HubConnectionRegistry registry, ITenantDirectory tenants, ITenantScopeFactory scopes) : Hub
{
    public const string Path = "/hubs/production";
    public const string ProductionPanel = "production";
    public const string ShopfloorPanel = "shopfloor";
    public const string OrderBoardChanged = "OrderBoardChanged";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (!Guid.TryParse(user?.FindFirstValue(TenantJwtAuthentication.TenantClaim), out var tenantId)
            || !Guid.TryParse(user?.FindFirstValue("sub"), out var userId)
            || await tenants.FindAsync(tenantId, Context.ConnectionAborted) is not { } tenant)
        {
            Context.Abort();
            return;
        }

        // Escopo próprio do tenant: o do request de conexão pode não existir mais (long polling).
        EffectivePermissions permissions;
        await using (var scope = await scopes.CreateScopeAsync(tenant, Context.ConnectionAborted))
        {
            permissions = await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId, Context.ConnectionAborted);
        }

        var panels = Panels(permissions).ToList();
        if (panels.Count == 0)
        {
            Context.Abort();
            return;
        }

        foreach (var panel in panels)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PingHub.Group(tenantId, panel), Context.ConnectionAborted);
        }

        registry.Add(tenantId, userId, Context);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Remove(Context);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Grupos permitidos pela permissão efetiva (CA 10).</summary>
    public static IEnumerable<string> Panels(EffectivePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Has("production.view"))
        {
            yield return ProductionPanel;
        }

        if (permissions.Has("shopfloor.view"))
        {
            yield return ShopfloorPanel;
        }
    }
}

/// <summary>
/// Conexões abertas dos hubs por tenant e usuário (CA 17): fecha as do usuário quando a permissão ou o security stamp mudam, logo
/// depois do commit. Vale para uma instância da API (a VPS roda uma); com várias, precisa de backplane.
/// </summary>
public sealed class HubConnectionRegistry : IRealtimeConnections
{
    private readonly ConcurrentDictionary<(Guid Tenant, Guid User), ConcurrentDictionary<string, HubCallerContext>> _byUser = new();
    private readonly ConcurrentDictionary<string, (Guid Tenant, Guid User)> _owners = new(StringComparer.Ordinal);

    public void Add(Guid tenantId, Guid userId, HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _byUser.GetOrAdd((tenantId, userId), _ => new ConcurrentDictionary<string, HubCallerContext>(StringComparer.Ordinal))[context.ConnectionId] = context;
        _owners[context.ConnectionId] = (tenantId, userId);
    }

    public void Remove(HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_owners.TryRemove(context.ConnectionId, out var owner) && _byUser.TryGetValue(owner, out var connections))
        {
            connections.TryRemove(context.ConnectionId, out _);
        }
    }

    public int CountFor(Guid tenantId, Guid userId) => _byUser.TryGetValue((tenantId, userId), out var connections) ? connections.Count : 0;

    public void CloseFor(Guid tenantId, IReadOnlyCollection<Guid> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        foreach (var userId in userIds)
        {
            if (_byUser.TryRemove((tenantId, userId), out var connections))
            {
                foreach (var context in connections.Values)
                {
                    _owners.TryRemove(context.ConnectionId, out _);
                    context.Abort();
                }
            }
        }
    }
}

/// <summary>Envio do <c>OrderBoardChanged</c> aos grupos do tenant (CA 12): só o id, nunca dado.</summary>
public sealed class SignalRProductionBoardPush(IHubContext<ProductionHub> hub) : IProductionBoardPush
{
    public async Task OrdersChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        var groups = new[] { PingHub.Group(tenantId, ProductionHub.ProductionPanel), PingHub.Group(tenantId, ProductionHub.ShopfloorPanel) };
        foreach (var id in orderIds)
        {
            await hub.Clients.Groups(groups).SendAsync(ProductionHub.OrderBoardChanged, new BoardChangedMessage(id), cancellationToken);
        }
    }
}

/// <summary>Corpo da mensagem: só o id da OS (CA 12 e 16).</summary>
public sealed record BoardChangedMessage(Guid ProductionOrderId);

/// <summary>
/// Acesso ao hub na negociação e na conexão (CA 10 e 15): módulo liberado no tenant (403 <c>module_not_released</c>) e ao menos
/// uma das ações, de módulos diferentes (403 <c>forbidden</c>). Os filtros de endpoint não rodam em hub, por isso a regra é policy.
/// </summary>
public static class HubAccess
{
    public const string PolicyPrefix = "hub:";

    public static HubEndpointConventionBuilder RequireHubAccess(this HubEndpointConventionBuilder builder, string module, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(permissions);
        if (ModuleCatalog.Find(module) is null || permissions.Length == 0 || permissions.Any(p => !ModuleCatalog.IsKnownPermission(p)))
        {
            throw new ArgumentException("Módulo ou permissão do hub fora do ModuleCatalog: " + module + " " + string.Join(", ", permissions), nameof(permissions));
        }

        builder.WithMetadata(new RequiresModuleMetadata(module));
        return builder.RequireAuthorization(PolicyPrefix + module + ":" + string.Join('|', permissions));
    }
}
