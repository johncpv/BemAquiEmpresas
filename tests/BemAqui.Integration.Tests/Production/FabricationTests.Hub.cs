using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Host.Realtime;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Production.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-02, tempo real: <c>ProductionHub</c> com grupos do tenant pela permissão (CA 10, 15, 16), aviso só com o id e só depois do
/// commit, também no consumidor do outbox (CA 12), conexão fechada ao perder a permissão ou mudar o stamp (CA 17) e o token fora
/// do log (CA 18). As conexões e os escopos usam o mesmo host (<see cref="Factory"/>), dono do registro de conexões.
/// </summary>
public partial class FabricationTests
{
    private static readonly TimeSpan PushWindow = TimeSpan.FromSeconds(2);

    private static HubConnection ProductionHubFor(WebApplicationFactory<Program> factory, string host, string token) =>
        new HubConnectionBuilder()
            .WithUrl($"http://{host}{ProductionHub.Path}", o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    private static string TokenOf(HttpClient client) => client.DefaultRequestHeaders.Authorization!.Parameter!;

    /// <summary>Escopo do tenant A no host das conexões (o registro de conexões é dele).</summary>
    private async Task<AsyncServiceScope> HubScopeAsync(Guid? tenantId = null)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenantId ?? tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Recebe os ids do OrderBoardChanged e guarda o corpo bruto para conferir que só leva o id.</summary>
    private static (List<Guid> Ids, List<JsonElement> Raw) Listen(HubConnection hub)
    {
        var ids = new List<Guid>();
        var raw = new List<JsonElement>();
        hub.On<JsonElement>(ProductionHub.OrderBoardChanged, message =>
        {
            lock (ids)
            {
                raw.Add(message);
                ids.Add(message.GetProperty("productionOrderId").GetGuid());
            }
        });
        return (ids, raw);
    }

    private static async Task<bool> ReceivedAsync(List<Guid> ids, Guid id, TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        while (DateTime.UtcNow < until)
        {
            lock (ids)
            {
                if (ids.Contains(id))
                {
                    return true;
                }
            }

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>Usuário do tenant B no grupo Proprietário de B, com token para b.localhost.</summary>
    private async Task<string> TenantBOwnerTokenAsync()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantB, "b" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using var scope = await HubScopeAsync(tenancy.TenantB.Id);
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var owners = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
        db.UserGroups.Add(new UserGroup { UserId = id, GroupId = owners });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        return TestTokens.Create(tenancy.TenantB.Id, "b.localhost", userId: id, stamp: stamp);
    }

    [Fact]
    public async Task Hub_avisa_o_cartao_so_com_o_id_e_so_no_proprio_tenant()
    {
        var (_, _, _, orders, _) = await KanbanDatasetAsync(1);
        var (_, supervisor) = await ActorAsync("production:view,approve");
        await using var hubA = ProductionHubFor(Factory, Host, TokenOf(supervisor));
        await using var hubB = ProductionHubFor(Factory, "b.localhost", await TenantBOwnerTokenAsync());
        var (idsA, rawA) = Listen(hubA);
        var (idsB, _) = Listen(hubB);
        await hubA.StartAsync();
        await hubB.StartAsync();

        var order = await GetOrderAsync(supervisor, orders[0]);
        var first = order.GetProperty("steps")[0].GetProperty("id").GetGuid();
        (await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{first}/complete", new { reason = "Ao vivo", version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReceivedAsync(idsA, orders[0], PushWindow)).Should().BeTrue("o cartão muda de coluna em até 2 s (PRD §6)");
        lock (idsA)
        {
            rawA.First().EnumerateObject().Select(p => p.Name).Should().Equal(["productionOrderId"], "o push nunca leva dado além do id");
        }

        (await ReceivedAsync(idsB, orders[0], TimeSpan.FromSeconds(1))).Should().BeFalse("o tenant B não recebe o push do A");
        await hubA.Invoking(h => h.InvokeAsync("JoinGroup", "x")).Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>("o hub não tem método que entra em grupo");
    }

    [Fact]
    public async Task Hub_recusa_sem_permissao_com_modulo_nao_liberado_e_aceita_o_chao_de_fabrica()
    {
        var (_, none) = await ActorAsync(null);
        var (_, operatorClient) = await ActorAsync("shopfloor:view");
        await using var refused = ProductionHubFor(Factory, Host, TokenOf(none));
        (await refused.Invoking(h => h.StartAsync()).Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var shopfloor = ProductionHubFor(Factory, Host, TokenOf(operatorClient));
        await shopfloor.StartAsync();
        shopfloor.State.Should().Be(HubConnectionState.Connected, "shopfloor.view entra no grupo do chão de fábrica (/fab)");

        await using var otherHost = ProductionHubFor(Factory, "b.localhost", TokenOf(operatorClient));
        (await otherHost.Invoking(h => h.StartAsync()).Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await SetProductionReleasedAsync(false);
        try
        {
            var (_, viewer) = await ActorAsync("production:view");
            var negotiate = Factory.CreateClient();
            negotiate.DefaultRequestHeaders.Host = Host;
            negotiate.DefaultRequestHeaders.Authorization = viewer.DefaultRequestHeaders.Authorization;
            var response = await negotiate.PostAsync(ProductionHub.Path + "/negotiate?negotiateVersion=1", null);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(response)).Should().Be("module_not_released");
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }
    }

    [Fact]
    public async Task Aviso_sai_so_depois_do_commit_e_nada_no_rollback_tambem_no_consumidor_do_outbox()
    {
        var (_, _, project, orders, _) = await KanbanDatasetAsync(2);
        var (_, viewer) = await ActorAsync("production:view");
        await using var hub = ProductionHubFor(Factory, Host, TokenOf(viewer));
        var (ids, _) = Listen(hub);
        await hub.StartAsync();

        await using (var scope = await HubScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET priority = 1 WHERE id = {orders[0]}");
            await scope.ServiceProvider.GetRequiredService<ProductionBoardNotifier>().ChangedAsync(orders[0]);
            (await ReceivedAsync(ids, orders[0], TimeSpan.FromMilliseconds(600))).Should().BeFalse("antes do commit nada sai");
            await transaction.RollbackAsync();
        }

        (await ReceivedAsync(ids, orders[0], TimeSpan.FromSeconds(1))).Should().BeFalse("rollback não envia nada");

        await using (var scope = await HubScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<ProductionBoardNotifier>().ChangedAsync(orders[0]);
            await transaction.CommitAsync();
        }

        (await ReceivedAsync(ids, orders[0], PushWindow)).Should().BeTrue();

        // Consumidor do outbox (como o OutboxProcessor: escopo e transação próprios): o cancelamento pelo contrato avisa no commit.
        await using (var scope = await HubScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var contract = await db.ProductionOrders.AsNoTracking().Where(o => o.Id == orders[1]).Select(o => o.ContractId).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<FabricationContractCancelledConsumer>().HandleAsync(new ContractCancelled(contract, project), CancellationToken.None);
            (await ReceivedAsync(ids, orders[1], TimeSpan.FromMilliseconds(600))).Should().BeFalse("o consumidor também avisa só depois do commit");
            await transaction.CommitAsync();
        }

        (await ReceivedAsync(ids, orders[1], PushWindow)).Should().BeTrue();
    }

    [Fact]
    public async Task Perder_a_permissao_fecha_a_conexao_e_a_reconexao_e_recusada()
    {
        var (userId, viewer) = await ActorAsync("production:view");
        await using var hub = ProductionHubFor(Factory, Host, TokenOf(viewer));
        var closed = new TaskCompletionSource();
        hub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await hub.StartAsync();
        // O OnConnectedAsync roda depois da resposta do handshake: espera o registro da conexão.
        var registry = Factory.Services.GetRequiredService<HubConnectionRegistry>();
        for (var i = 0; i < 40 && registry.CountFor(tenancy.TenantA.Id, userId) == 0; i++)
        {
            await Task.Delay(50);
        }

        registry.CountFor(tenancy.TenantA.Id, userId).Should().Be(1);

        await using (var scope = await HubScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            await db.UserGroups.Where(g => g.UserId == userId).ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
        }

        (await Task.WhenAny(closed.Task, Task.Delay(PushWindow))).Should().Be(closed.Task, "a conexão cai logo depois do commit da mudança");
        await using var again = ProductionHubFor(Factory, Host, TokenOf(viewer));
        (await again.Invoking(h => h.StartAsync()).Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Usuario_desativado_tem_a_conexao_fechada_e_nao_reconecta()
    {
        var (userId, viewer) = await ActorAsync("production:view");
        await using var hub = ProductionHubFor(Factory, Host, TokenOf(viewer));
        var closed = new TaskCompletionSource();
        hub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await hub.StartAsync();

        await using (var scope = await HubScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<UserLifecycle>().DeactivateAsync(userId, null)).IsSuccess.Should().BeTrue();
        }

        (await Task.WhenAny(closed.Task, Task.Delay(PushWindow))).Should().Be(closed.Task, "o security stamp mudou");
        await using var again = ProductionHubFor(Factory, Host, TokenOf(viewer));
        (await again.Invoking(h => h.StartAsync()).Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_do_hub_nao_aparece_no_log()
    {
        var sink = new CapturingSink();
        await using var logged = tenancy.Factory.WithWebHostBuilder(b => b
            .UseSetting("Serilog:MinimumLevel:Override:Microsoft.AspNetCore", "Information")
            .ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var (_, viewer) = await ActorAsync("production:view");
        var token = TokenOf(viewer);
        await using var hub = ProductionHubFor(logged, Host, token);

        await hub.StartAsync();
        await hub.StopAsync();

        sink.Count.Should().BeGreaterThan(0, "o request do hub foi logado");
        sink.Text.Should().NotContain(token);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<string> _lines = [];

        public int Count
        {
            get
            {
                lock (_lines)
                {
                    return _lines.Count;
                }
            }
        }

        public string Text
        {
            get
            {
                lock (_lines)
                {
                    return string.Join('\n', _lines);
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            var line = logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(';', logEvent.Properties.Values);
            lock (_lines)
            {
                _lines.Add(line);
            }
        }
    }
}
