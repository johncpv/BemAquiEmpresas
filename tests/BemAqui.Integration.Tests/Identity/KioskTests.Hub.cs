using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Host.Persistence;
using BemAqui.Host.Realtime;
using BemAqui.Production.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>PNL-01a CA 12 a 15: hub do quiosque com os grupos das claims, push depois do commit em menos de 2 s, revogação com a conexão aberta.</summary>
public partial class KioskTests
{
    private static readonly TimeSpan PushWindow = TimeSpan.FromSeconds(2);

    /// <summary>Conexão da TV: só o cookie da sessão, nenhum token na query.</summary>
    private HubConnection KioskHubFor(string cookie, string host = Host, string? ip = null) =>
        new HubConnectionBuilder()
            .WithUrl($"http://{host}{KioskHub.Path}", o =>
            {
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                o.Headers["Cookie"] = KioskCookie + "=" + cookie;
                o.Headers["X-Forwarded-For"] = ip ?? _ip;
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    private sealed class Received
    {
        public List<Guid> Orders { get; } = [];

        public List<string> Control { get; } = [];

        public List<JsonElement> Raw { get; } = [];

        public bool Closed { get; set; }
    }

    private static Received Listen(HubConnection hub)
    {
        var received = new Received();
        hub.On<JsonElement>(KioskHub.OrderBoardChanged, message =>
        {
            lock (received)
            {
                received.Raw.Add(message);
                received.Orders.Add(message.GetProperty("productionOrderId").GetGuid());
            }
        });
        hub.On(KioskHub.KioskConfigChanged, () =>
        {
            lock (received)
            {
                received.Control.Add("config");
            }
        });
        hub.On<JsonElement>(KioskHub.KioskRevoked, message =>
        {
            lock (received)
            {
                received.Control.Add(message.GetProperty("reason").GetString()!);
            }
        });
        hub.Closed += _ =>
        {
            lock (received)
            {
                received.Closed = true;
            }

            return Task.CompletedTask;
        };
        return received;
    }

    private static Task<bool> SeenAsync(Received received, Func<Received, bool> condition, TimeSpan within) =>
        EventuallyAsync(() =>
        {
            lock (received)
            {
                return Task.FromResult(condition(received));
            }
        }, within);

    [Fact]
    public async Task Hub_sem_sessao_de_quiosque_ou_com_usuario_e_recusado()
    {
        await using var anonymous = KioskHubFor("cookie-que-nao-existe-" + new string('x', 30));
        var refused = () => anonymous.StartAsync();
        await refused.Should().ThrowAsync<HttpRequestException>();

        var (_, owner) = await OwnerOnDutyAsync();
        await using var user = new HubConnectionBuilder()
            .WithUrl($"http://{Host}{KioskHub.Path}", o =>
            {
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(owner);
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var asUser = () => user.StartAsync();
        await asUser.Should().ThrowAsync<HttpRequestException>("o hub do quiosque não aceita o JWT do usuário");
    }

    [Fact]
    public async Task TV_recebe_o_aviso_do_quadro_so_com_o_id_em_menos_de_2s_do_commit_e_so_do_proprio_tenant()
    {
        var (_, cookie, _) = await PairedAsync();
        await using var hub = KioskHubFor(cookie);
        var received = Listen(hub);
        await hub.StartAsync();
        var orderId = Guid.CreateVersion7();

        // A transação do Production faz commit; o aviso sai pelo IProductionBoardNotifier da PRD-02, depois do commit.
        Stopwatch watch;
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await scope.ServiceProvider.GetRequiredService<ProductionBoardNotifier>().ChangedAsync(orderId);
            db.AuditEntries.Add(new AuditEntry { Entity = "ProductionOrder", EntityId = orderId.ToString(), Action = "Teste", At = DateTimeOffset.UtcNow, Changes = "{}" });
            await db.SaveChangesAsync();
            watch = Stopwatch.StartNew();
        }

        (await SeenAsync(received, r => r.Orders.Contains(orderId), PushWindow)).Should().BeTrue("o aviso chega à TV");
        (await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie))).StatusCode.Should().Be(HttpStatusCode.OK, "a TV recarrega pela API de quiosque");
        watch.Elapsed.Should().BeLessThan(PushWindow, "menos de 2 s do commit à resposta da API na TV (PRD §6)");
        lock (received)
        {
            received.Raw.Should().OnlyContain(m => m.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "productionOrderId" }), "só o id, nunca dado");
        }
    }

    [Fact]
    public async Task Grupos_vem_das_claims_a_TV_de_outro_tenant_nao_recebe_e_a_configuracao_nova_chega_a_tela()
    {
        var (row, cookie, manager) = await PairedAsync();
        var (_, otherHost, _, otherCookie, _) = await PairedInOwnTenantAsync();
        await using var hub = KioskHubFor(cookie);
        await using var other = KioskHubFor(otherCookie, otherHost);
        var received = Listen(hub);
        var otherReceived = Listen(other);
        await hub.StartAsync();
        await other.StartAsync();

        var orderId = Guid.CreateVersion7();
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<IProductionBoardPush>().OrdersChangedAsync(Tenancy.TenantA.Id, [orderId]);
        }

        (await SeenAsync(received, r => r.Orders.Contains(orderId), PushWindow)).Should().BeTrue();
        await Task.Delay(300);
        lock (otherReceived)
        {
            otherReceived.Orders.Should().NotContain(orderId, "o grupo é {tenantId}:production, do tenant da claim");
        }

        var id = Id(row);
        var updated = await Client().SendAsync(Authorized(HttpMethod.Put, $"{Screens}/{id}", manager, new { name = row.GetProperty("name").GetString(), views = Array.Empty<string>(), rotationSeconds = 60, allowedNetworks = Array.Empty<string>(), version = Version(row) }));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SeenAsync(received, r => r.Control.Contains("config"), TimeSpan.FromSeconds(5))).Should().BeTrue("KioskConfigChanged em até 1 min, sem trocar o token (CA 4)");
        lock (otherReceived)
        {
            otherReceived.Control.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Revogar_com_a_conexao_aberta_envia_KioskRevoked_e_fecha_a_conexao_na_hora()
    {
        var (row, cookie, manager) = await PairedAsync();
        await using var hub = KioskHubFor(cookie);
        var received = Listen(hub);
        await hub.StartAsync();
        var id = Id(row);
        var watch = Stopwatch.StartNew();

        var revoked = await Client().SendAsync(Authorized(HttpMethod.Post, $"{Screens}/{id}/revoke", manager, new { version = Version(row) }));

        revoked.StatusCode.Should().Be(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync());
        (await SeenAsync(received, r => r.Closed, TimeSpan.FromSeconds(10))).Should().BeTrue("a conexão é fechada");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1));
        lock (received)
        {
            received.Control.Should().Contain("revoked", "a TV mostra \"Esta tela foi desligada por {empresa}.\"");
        }

        await using var again = KioskHubFor(cookie);
        var reconnect = () => again.StartAsync();
        await reconnect.Should().ThrowAsync<HttpRequestException>("a reconexão passa de novo pela sessão, que caiu");
    }

    [Fact]
    public async Task Suspender_o_tenant_ou_tirar_o_modulo_fecha_a_conexao_na_revalidacao()
    {
        var (tenant, host, _, cookie, _) = await PairedInOwnTenantAsync();
        await using var hub = KioskHubFor(cookie, host);
        var received = Listen(hub);
        await hub.StartAsync();

        await SetKioskReleasedAsync(tenant.Id, released: false);
        try
        {
            (await SeenAsync(received, r => r.Closed, TimeSpan.FromSeconds(20))).Should().BeTrue("a revalidação (a cada 30 s; 2 s neste host) fecha a conexão em menos de 1 min");
            lock (received)
            {
                received.Control.Should().Contain("unavailable", "a TV mostra \"Painel indisponível nesta versão do sistema.\"");
            }
        }
        finally
        {
            await SetKioskReleasedAsync(tenant.Id, released: true);
        }

        await using var back = KioskHubFor(cookie, host);
        var backReceived = Listen(back);
        await back.StartAsync();
        await Tenancy.SetStatusAsync(tenant.Id, suspended: true);
        try
        {
            (await SeenAsync(backReceived, r => r.Closed, TimeSpan.FromSeconds(20))).Should().BeTrue("tenant suspenso também fecha a conexão");
        }
        finally
        {
            await Tenancy.SetStatusAsync(tenant.Id, suspended: false);
        }
    }

    [Fact]
    public void Hub_do_quiosque_nao_tem_metodo_de_cliente_para_servidor() =>
        typeof(KioskHub).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetBaseDefinition().DeclaringType == typeof(KioskHub))
            .Should().BeEmpty("entrar em grupo é decisão do servidor, pelas claims");
}
