using System.Net;
using System.Net.Http.Json;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Platform;

/// <summary>FND-13 CA 2, 6, 7, 8 e 9: allowlist, Hangfire, bloqueio, auditoria, anti-CSRF e inatividade.</summary>
public partial class PlatformAuthTests
{
    private async Task<T> CatalogAsync<T>(Func<CatalogDbContext, Task<T>> query)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
    }

    [Fact]
    public async Task Hangfire_so_abre_com_sessao_da_plataforma()
    {
        var anonymous = await Client().GetAsync("/hangfire");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Redirect);
        anonymous.Headers.Location!.ToString().Should().Be("/entrar?volta=%2Fhangfire");

        var admin = await AdminWithSessionAsync();
        (await WithCookie(Client(), admin.Cookie).GetAsync("/hangfire")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IP_fora_da_allowlist_recebe_404_antes_do_login()
    {
        await using var restricted = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AllowedNetworks:0", "10.9.9.0/24"));

        (await Client(factory: restricted).PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "x@y.com", password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client(factory: restricted).GetAsync("/hangfire")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cinco_senhas_erradas_bloqueiam_e_tudo_vai_para_a_auditoria_do_catalogo()
    {
        var admin = await AdminWithSessionAsync();
        for (var i = 0; i < 5; i++)
        {
            (await Client().PostAsJsonAsync("/api/v1/platform/auth/login", new { email = admin.Email, password = "senha errada numero " + i })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        (await Client().PostAsJsonAsync("/api/v1/platform/auth/login", new { email = admin.Email, password = Password })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await WithCookie(Client(), admin.Cookie).PostAsync("/api/v1/platform/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithCookie(Client(), admin.Cookie).GetAsync("/api/v1/platform/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var adminId = await CatalogAsync(db => db.PlatformAdmins.Where(a => a.NormalizedEmail == admin.Email).Select(a => a.Id).SingleAsync());
        var actions = await CatalogAsync(db => db.AuditEntries.Where(a => a.Entity == "PlatformAuth" && a.EntityId == adminId.ToString()).Select(a => a.Action).ToListAsync());
        actions.Should().Contain(["AdminInvited", "PasswordSet", "TwoFactorEnabled", "LoginSucceeded", "LoginFailed", "LockedOut", "LoginBlocked", "Logout"]);
    }

    [Fact]
    public async Task POST_sem_cabecalho_anti_CSRF_e_recusado()
    {
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = PlatformHost;

        (await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "x@y.com", password = Password })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sessao_parada_alem_do_tempo_de_inatividade_expira()
    {
        var admin = await AdminWithSessionAsync();
        var adminId = await CatalogAsync(db => db.PlatformAdmins.Where(a => a.NormalizedEmail == admin.Email).Select(a => a.Id).SingleAsync());

        await CatalogAsync(db => db.PlatformSessions.Where(s => s.AdminId == adminId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, DateTimeOffset.UtcNow.AddMinutes(-31))));

        (await WithCookie(Client(), admin.Cookie).GetAsync("/api/v1/platform/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
