using System.Net;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using BemAqui.Identity.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>PNL-01a CA 1 a 4 e 20: catálogo, seed patch, lista, criar com o link uma vez, editar e as três camadas da gestão.</summary>
public partial class KioskTests
{
    [Fact]
    public async Task Catalogo_tem_kiosk_delete_e_o_seed_patch_da_a_acao_a_Supervisao_uma_vez()
    {
        var (catalog, patches, supervision) = await WithDbAsync(async db => (
            await db.CatalogModuleActions.Where(a => a.ModuleCode == "kiosk").Select(a => a.Action).ToListAsync(),
            await db.SeedPatches.CountAsync(p => p.Key == "kiosk-delete"),
            await db.GroupPermissions.Where(p => p.ModuleCode == "kiosk" && db.Groups.Any(g => g.Id == p.GroupId && g.SeedKey == "production_supervision")).Select(p => p.Action).ToListAsync()));

        catalog.Should().BeEquivalentTo(["view", "create", "delete"]);
        patches.Should().Be(1);
        supervision.Should().BeEquivalentTo(["view", "create", "delete"]);

        // Rodar os seeders de novo (migrate seguinte) não duplica nem reaplica.
        await using (var scope = await ScopeAsync())
        {
            await new SeedPatchSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await WithDbAsync(db => db.SeedPatches.CountAsync(p => p.Key == "kiosk-delete"))).Should().Be(1);
        (await WithDbAsync(db => db.GroupPermissions.CountAsync(p => p.ModuleCode == "kiosk" && p.Action == "delete" && db.Groups.Any(g => g.Id == p.GroupId && g.SeedKey == "production_supervision"))))
            .Should().Be(1);

        var (ownerId, _) = await OwnerOnDutyAsync();
        await using var permissionScope = await ScopeAsync();
        (await permissionScope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(ownerId)).Has("kiosk.delete").Should().BeTrue("o Proprietário tem tudo");
    }

    [Fact]
    public async Task Criar_mostra_o_link_uma_vez_grava_so_o_hash_audita_sem_o_token_e_avisa_o_Proprietario()
    {
        await OwnerOnDutyAsync();
        var (_, manager) = await UserAsync(Manager, "Marta Lopes");
        var name = "TV Montagem " + Guid.NewGuid().ToString("N")[..6];

        var response = await Client().SendAsync(Authorized(HttpMethod.Post, Screens, manager, new { name, line = (string?)null, views = Array.Empty<string>(), rotationSeconds = (int?)null, allowedNetworks = new[] { "192.168.10.0/24" } }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await JsonAsync(response);
        var link = body.GetProperty("link").GetString()!;
        link.Should().StartWith("https://a.localhost/tv#");
        var token = link[(link.IndexOf('#', StringComparison.Ordinal) + 1)..];
        var row = body.GetProperty("screen");
        row.GetProperty("status").GetString().Should().Be("AwaitingConnection");
        row.GetProperty("rotationSeconds").GetInt32().Should().Be(20, "padrão de 20 s");
        row.GetProperty("views").EnumerateArray().Select(v => v.GetString()).Should().Equal(KioskViews.All);
        row.GetProperty("tokenPrefix").GetString().Should().Be(token[..6]);
        row.ToString().Should().NotContain(token, "a linha nunca leva o token");

        var id = Id(row);
        var stored = await WithDbAsync(db => db.KioskScreens.AsNoTracking().SingleAsync(s => s.Id == id));
        stored.TokenHash.Should().Be(BemAqui.BuildingBlocks.Security.OpaqueToken.Hash(token)).And.NotContain(token);
        var audit = await WithDbAsync(db => db.AuditEntries.Where(a => a.EntityId == id.ToString()).Select(a => a.Changes).ToListAsync());
        audit.Should().NotBeEmpty("a criação é auditada pelo interceptor");
        audit.Should().AllSatisfy(c => c.Should().NotContain(token).And.NotContain(stored.TokenHash));
        (await NotificationsAsync($"Nova tela de TV {name} criada por Marta Lopes.")).Should().NotBeEmpty();

        var list = await JsonAsync(await Client().SendAsync(Authorized(HttpMethod.Get, Screens, manager)));
        var listed = list.GetProperty("screens").EnumerateArray().Single(s => Id(s) == id);
        listed.GetProperty("createdByName").GetString().Should().Be("Marta Lopes");
        listed.GetProperty("allowedNetworks").EnumerateArray().Select(n => n.GetString()).Should().Equal("192.168.10.0/24");
        listed.GetProperty("activeSessions").GetInt32().Should().Be(0);
        listed.GetProperty("lastSeenAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "Nunca conectada");
        list.ToString().Should().NotContain(token, "o link não aparece de novo");
    }

    [Fact]
    public async Task Nome_unico_entre_as_ativas_e_validacao_em_portugues()
    {
        var (_, manager) = await UserAsync(Manager);
        var name = "TV Pintura " + Guid.NewGuid().ToString("N")[..6];
        await CreateScreenAsync(manager, name);

        var repeated = await Client().SendAsync(Authorized(HttpMethod.Post, Screens, manager, new { name = name.ToUpperInvariant(), views = Array.Empty<string>(), allowedNetworks = Array.Empty<string>() }));
        repeated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(repeated)).Should().Be("kiosk_name_taken");

        var invalid = await Client().SendAsync(Authorized(HttpMethod.Post, Screens, manager, new { name = "TV", rotationSeconds = 5, views = new[] { "financeiro" }, allowedNetworks = new[] { "fabrica" }, line = "Linha que não existe" }));
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await JsonAsync(invalid)).GetProperty("errors");
        errors.GetProperty("rotationSeconds")[0].GetString().Should().Be("A rotação vai de 10 a 120 segundos.");
        errors.TryGetProperty("views", out _).Should().BeTrue();
        errors.TryGetProperty("allowedNetworks", out _).Should().BeTrue();
        errors.TryGetProperty("line", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Editar_grava_com_auditoria_avisa_o_Proprietario_e_nao_troca_o_token()
    {
        await OwnerOnDutyAsync();
        var (_, manager) = await UserAsync(Manager, "Caio Reis");
        var (row, token) = await CreateScreenAsync(manager);
        var id = Id(row);
        var hashBefore = await WithDbAsync(db => db.KioskScreens.Where(s => s.Id == id).Select(s => s.TokenHash).SingleAsync());
        var name = "TV Solda " + Guid.NewGuid().ToString("N")[..6];

        var updated = await Client().SendAsync(Authorized(HttpMethod.Put, $"{Screens}/{id}", manager, new { name, views = new[] { KioskViews.Board }, rotationSeconds = 45, allowedNetworks = Array.Empty<string>(), version = Version(row) }));

        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var after = await JsonAsync(updated);
        after.GetProperty("name").GetString().Should().Be(name);
        after.GetProperty("rotationSeconds").GetInt32().Should().Be(45);
        (await WithDbAsync(db => db.KioskScreens.Where(s => s.Id == id).Select(s => s.TokenHash).SingleAsync())).Should().Be(hashBefore, "editar não troca o token");
        (await NotificationsAsync($"Tela de TV {name} alterada por Caio Reis.")).Should().NotBeEmpty();
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == id.ToString() && a.Action == AuditActions.Updated))).Should().BeGreaterThan(0);

        var stale = await Client().SendAsync(Authorized(HttpMethod.Put, $"{Screens}/{id}", manager, new { name, views = Array.Empty<string>(), allowedNetworks = Array.Empty<string>(), version = Version(row) }));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "versão antiga (xmin): 409, não 500");
        (await Client().SendAsync(OpenRequest(token))).StatusCode.Should().Be(HttpStatusCode.OK, "o link continua o mesmo depois de editar");
    }

    [Fact]
    public async Task Tres_camadas_da_gestao_das_telas()
    {
        var (_, viewer) = await UserAsync("kiosk:view");
        var (_, creatorWithoutProduction) = await UserAsync("kiosk:view,create");
        var (_, manager) = await UserAsync(Manager);
        var (_, noGroup) = await UserAsync(null);
        var (row, _) = await CreateScreenAsync(manager);
        var id = Id(row);

        (await Client().SendAsync(Authorized(HttpMethod.Get, Screens, viewer))).StatusCode.Should().Be(HttpStatusCode.OK);
        var none = await Client().SendAsync(Authorized(HttpMethod.Get, Screens, noGroup));
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden");

        var create = await Client().SendAsync(Authorized(HttpMethod.Post, Screens, creatorWithoutProduction, new { name = "Sem produção", views = Array.Empty<string>(), allowedNetworks = Array.Empty<string>() }));
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden, "criar exige também production.view (CA 3)");
        Code(await JsonAsync(create)).Should().Be("forbidden");
        (await Client().SendAsync(Authorized(HttpMethod.Post, $"{Screens}/{id}/new-link", creatorWithoutProduction, new { version = Version(row) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client().SendAsync(Authorized(HttpMethod.Put, $"{Screens}/{id}", viewer, new { name = "x", version = Version(row) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client().SendAsync(Authorized(HttpMethod.Post, $"{Screens}/{id}/revoke", viewer, new { version = Version(row) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "revogar exige kiosk.delete");

        var (tenant, host) = await OwnTenantAsync();
        var (_, other) = await UserAsync(Manager, owner: tenant, host: host);
        await SetKioskReleasedAsync(tenant.Id, released: false);
        var notReleased = await Client(host).SendAsync(Authorized(HttpMethod.Get, Screens, other));
        notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        Code(await JsonAsync(await Client(host).SendAsync(Authorized(HttpMethod.Post, Screens, other, new { name = "x" })))).Should().Be("module_not_released");
    }

    [Fact]
    public async Task Troca_da_rota_revoga_as_telas_antigas_audita_e_avisa_o_Proprietario()
    {
        var (tenant, host) = await OwnTenantAsync();
        await OwnerOnDutyAsync(tenant, host);
        var legacyId = Guid.Empty;
        await WithDbAsync(async db =>
        {
            var screen = KioskScreen.Create("TV do link antigo", null, KioskViews.All, 20, [], new string('a', 64), "aaaaaa", Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-3));
            db.KioskScreens.Add(screen);
            legacyId = screen.Id;
            await db.SeedPatches.Where(p => p.Key == KioskLegacyRouteSeeder.Key).ExecuteDeleteAsync(); // como o banco antes do deploy desta story
            await db.SaveChangesAsync();
        }, tenant.Id);

        await using (var scope = await ScopeAsync(tenant.Id))
        {
            await new KioskLegacyRouteSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var revoked = await WithDbAsync(db => db.KioskScreens.AsNoTracking().SingleAsync(s => s.Id == legacyId), tenant.Id);
        revoked.RevokedAt.Should().NotBeNull();
        (await WithDbAsync(db => db.AuditEntries.Where(a => a.EntityId == legacyId.ToString() && a.Action == "KioskRevoked").Select(a => a.Changes).ToListAsync(), tenant.Id))
            .Should().ContainSingle(c => c.Contains(KioskLegacyRouteSeeder.AuditReason));
        (await NotificationsAsync("A rota da TV mudou e 1 tela foi desligada.", tenant.Id)).Should().NotBeEmpty();

        // Uma vez só: a tela criada depois (com o link novo) não é revogada no próximo migrate.
        var (_, manager) = await UserAsync(Manager, owner: tenant, host: host);
        var (row, _) = await CreateScreenAsync(manager, host: host);
        await using (var scope = await ScopeAsync(tenant.Id))
        {
            await new KioskLegacyRouteSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var createdId = Id(row);
        (await WithDbAsync(db => db.KioskScreens.Where(s => s.Id == createdId).Select(s => s.RevokedAt).SingleAsync(), tenant.Id)).Should().BeNull();
    }
}
