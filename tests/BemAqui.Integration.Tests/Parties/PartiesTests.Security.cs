using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Parties;

public partial class PartiesTests
{
    [Fact]
    public async Task Tres_camadas_modulo_nao_liberado_sem_grupo_e_so_leitura()
    {
        var (_, nobody) = await ActorAsync();
        var (_, engineering) = await ActorAsync("engineering");

        var noGroup = await nobody.GetAsync("/api/v1/parties");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden");
        (await engineering.GetAsync("/api/v1/parties")).StatusCode.Should().Be(HttpStatusCode.OK, "Engenharia ganha parties.view pelo seed patch");
        var write = await engineering.PostAsJsonAsync("/api/v1/parties", Company(null, "Sem permissão"));
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await SetReleasedAsync(false);
        try
        {
            var notReleased = await engineering.GetAsync("/api/v1/parties");
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }
    }

    private async Task SetReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "parties").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    [Fact]
    public async Task Seed_patch_da_parties_aos_grupos_padrao_uma_vez_e_respeita_a_mudanca_do_tenant()
    {
        var (_, commercial) = await ActorAsync("commercial");
        await CreatedAsync(await commercial.PostAsJsonAsync("/api/v1/parties", Company(NewCnpj(), "Criado pelo Comercial")));

        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.SeedPatches.CountAsync(p => p.Key == "parties-default-permissions")).Should().Be(1);
        var warehouse = await db.Groups.SingleAsync(g => g.SeedKey == "warehouse");
        (await db.GroupPermissions.Where(p => p.GroupId == warehouse.Id && p.ModuleCode == "parties").Select(p => p.Action).ToListAsync()).Should().BeEquivalentTo(["view", "create", "edit"]);
        var engineeringId = await db.Groups.Where(g => g.SeedKey == "engineering").Select(g => g.Id).SingleAsync();
        (await db.GroupPermissions.Where(p => p.GroupId == engineeringId && p.ModuleCode == "parties").Select(p => p.Action).ToListAsync()).Should().Equal("view");

        // O tenant tira "edit" do Almoxarifado; rodar os seeds de novo (novo deploy) não devolve.
        await db.GroupPermissions.Where(p => p.GroupId == warehouse.Id && p.ModuleCode == "parties" && p.Action == "edit").ExecuteDeleteAsync();
        foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await db.GroupPermissions.CountAsync(p => p.GroupId == warehouse.Id && p.ModuleCode == "parties" && p.Action == "edit")).Should().Be(0);
        (await db.SeedPatches.CountAsync(p => p.Key == "parties-default-permissions")).Should().Be(1);
    }

    [Fact]
    public async Task Respostas_saem_com_no_store()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var list = await owner.GetAsync("/api/v1/parties");
        list.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await owner.GetAsync("/api/v1/parties/search?q=abc")).Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Dado_pessoal_nao_aparece_em_claro_na_auditoria_nem_no_log()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var cpf = NewCpf();
        const string email = "canario.lgpd@exemplo.com.br";
        const string phone = "11955554444";

        var id = await CreatedAsync(await client.PostAsJsonAsync("/api/v1/parties", new
        {
            kind = "PF", document = cpf, legalName = "Pessoa Canário", roles = new[] { "Customer" }, email, phone,
            mainContact = new { name = "Contato Canário Secreto", email, phone, receivesBilling = true },
        }));
        await client.GetAsync("/api/v1/parties?q=" + cpf);
        await client.PutAsJsonAsync("/api/v1/parties/" + id, new { kind = "PF", document = cpf, legalName = "Pessoa Canário", tradeName = (string?)null, roles = new[] { "Customer", "Supplier" }, stateRegistration = (string?)null, municipalRegistration = (string?)null, taxRegime = "NotInformed", email = "outro." + email, phone, notes = "Observação canário" });

        await using var scope = await ScopeAsync();
        var audit = string.Join("\n", await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.Where(a => a.Entity == "Party" || a.Entity == "PartyContact").Select(a => a.Changes).ToListAsync());
        audit.Should().Contain("Pessoa Canário", "o que não é sensível aparece no histórico");
        foreach (var canary in new[] { cpf, email, phone, "Contato Canário Secreto", "Observação canário" })
        {
            audit.Should().NotContain(canary);
        }

        string events;
        lock (sink.Events)
        {
            events = string.Join("\n", sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        events.Should().NotContain(email).And.NotContain(phone).And.NotContain("Contato Canário Secreto");
    }
}
