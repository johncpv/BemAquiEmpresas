using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Assets.Application;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Application;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Assets;

public partial class AssetTests
{
    /// <summary>CA 1: o seed patch assets-permissions roda uma vez; Supervisão com view/create/edit, Almoxarifado com view, delete só com o Proprietário.</summary>
    [Fact]
    public async Task Seed_patch_assets_permissions_uma_vez_e_delete_so_com_o_Proprietario()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.SeedPatches.CountAsync(p => p.Key == "assets-permissions")).Should().Be(1);
        async Task<List<string>> ActionsAsync(string seedKey)
        {
            var group = await db.Groups.SingleAsync(g => g.SeedKey == seedKey);
            (await db.GroupModules.AnyAsync(m => m.GroupId == group.Id && m.ModuleCode == "assets" && m.Enabled)).Should().BeTrue(seedKey);
            return await db.GroupPermissions.Where(p => p.GroupId == group.Id && p.ModuleCode == "assets").Select(p => p.Action).ToListAsync();
        }

        (await ActionsAsync("production_supervision")).Should().BeEquivalentTo(["view", "create", "edit"]);
        (await ActionsAsync("warehouse")).Should().Equal("view");
        (await db.GroupPermissions.AnyAsync(p => p.ModuleCode == "assets" && p.Action == "delete" && db.Groups.Any(g => g.Id == p.GroupId && !g.IsSystem && g.SeedKey != null)))
            .Should().BeFalse("assets.delete fica só com o Proprietário (D10)");

        // Um novo deploy (seeds de novo) não reaplica o patch nem duplica as concessões; os locais do seed também não se repetem.
        foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await db.SeedPatches.CountAsync(p => p.Key == "assets-permissions")).Should().Be(1);
        (await ActionsAsync("production_supervision")).Should().HaveCount(3);
        var sites = scope.ServiceProvider.GetRequiredService<IAssetsDb>().AssetSites.IgnoreQueryFilters();
        (await sites.CountAsync(s => s.SeedKey == AssetSiteService.FactorySeedKey)).Should().Be(1);
        (await sites.CountAsync(s => s.SeedKey == AssetSiteService.OfficeSeedKey)).Should().Be(1);
    }

    /// <summary>CA 9 e 2: lista com busca e filtros; o valor de aquisição só existe no JSON para quem tem assets.edit e só ele informa o valor.</summary>
    [Fact]
    public async Task Lista_filtra_e_o_valor_de_aquisicao_so_existe_para_quem_edita()
    {
        var supervisor = await SupervisorAsync();
        var center = await CenterAsync("Pintura");
        var site = await SiteAsync(supervisor);
        var responsible = await EmployeeAsync("Elisa");
        var marker = Guid.NewGuid().ToString("N")[..8];
        var machine = await CreateAsync(supervisor, Input("Cabine de pintura " + marker, serial: "CP-" + marker, value: 42_000m, site: site, responsible: responsible, center: center));
        await CreateAsync(supervisor, Input("Mesa " + marker, type: "Furniture", value: 900m));

        var bySerial = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}?q=cp-{marker}");
        bySerial.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tag").GetString()).Should().Equal(machine.GetProperty("tag").GetString());
        var filtered = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}?q={marker}&type=Machine&siteId={site}&workCenterId={center}&responsibleEmployeeId={responsible}&status=Active");
        var row = filtered.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("siteId").GetGuid().Should().Be(site);
        row.GetProperty("workCenterName").GetString().Should().StartWith("Pintura");
        row.GetProperty("responsibleName").GetString().Should().StartWith("Elisa");
        row.GetProperty("acquisitionValue").GetDecimal().Should().Be(42_000m);
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}?q={marker}&status=Disposed")).GetProperty("total").GetInt32().Should().Be(0);

        var warehouse = await ActorAsync("warehouse");
        var hidden = await warehouse.GetFromJsonAsync<JsonElement>($"{Base}?q={marker}");
        hidden.GetProperty("items").GetArrayLength().Should().Be(2);
        hidden.GetProperty("items").EnumerateArray().Should().OnlyContain(i => WithoutValue(i), "sem assets.edit o campo não existe no JSON");
        var detail = await warehouse.GetFromJsonAsync<JsonElement>($"{Base}/{machine.GetProperty("id").GetGuid()}");
        detail.TryGetProperty("acquisitionValue", out _).Should().BeFalse();
        (await warehouse.PostAsJsonAsync(Base, Input(Unique("Sem permissão")))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var creator = await ActorAsync("assets:view,create");
        var withValue = await creator.PostAsJsonAsync(Base, Input(Unique("Notebook"), type: "It", value: 5_000m));
        withValue.StatusCode.Should().Be(HttpStatusCode.Forbidden, "o valor de aquisição só com assets.edit");
        (await creator.PostAsJsonAsync(Base, Input(Unique("Notebook"), type: "It"))).StatusCode.Should().Be(HttpStatusCode.Created);
        var invalid = await supervisor.PostAsJsonAsync(Base, Input(Unique("Notebook"), type: "It", value: 10.555m));
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>CA 12: exclusão lógica só sem histórico; com uso ou fora de Active, 409 "Use Baixar bem."; a etiqueta nunca volta.</summary>
    [Fact]
    public async Task Exclusao_e_logica_so_sem_historico_e_a_etiqueta_nao_volta()
    {
        var supervisor = await SupervisorAsync();
        var owner = await ActorAsync("assets:view,create,edit,delete");
        (await supervisor.DeleteAsync($"{Base}/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "assets.delete só com o Proprietário");

        var used = (await CreateAsync(supervisor, Input(Unique("Ponte rolante")))).GetProperty("id").GetGuid();
        InUse[used] = "movimentações";
        var conflict = await owner.DeleteAsync($"{Base}/{used}");
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(conflict);
        Code(problem).Should().Be("asset_has_history");
        problem.GetProperty("detail").GetString().Should().Be("Bem com histórico não pode ser excluído. Use Baixar bem.");

        var maintenance = (await CreateAsync(supervisor, Input(Unique("Calandra")))).GetProperty("id").GetGuid();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAssetsDb>();
            (await db.Assets.SingleAsync(a => a.Id == maintenance)).Status = AssetStatus.Maintenance;
            await db.SaveChangesAsync();
        }

        (await owner.DeleteAsync($"{Base}/{maintenance}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var free = await CreateAsync(supervisor, Input(Unique("Bancada")));
        var freeId = free.GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"{Base}/{freeId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await supervisor.GetAsync($"{Base}/{freeId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAssetsDb>();
            (await db.Assets.IgnoreQueryFilters().SingleAsync(a => a.Id == freeId)).IsDeleted.Should().BeTrue("exclusão lógica");
        }

        var next = await CreateAsync(supervisor, Input(Unique("Bancada nova")));
        string.CompareOrdinal(next.GetProperty("tag").GetString(), free.GetProperty("tag").GetString()).Should().BePositive("a etiqueta do excluído não é reaproveitada");
    }

    /// <summary>CA 14 e 15: os donos só inativam o que o bem usa; o IAssets entrega só id, etiqueta, nome, tipo, centro e situação.</summary>
    [Fact]
    public async Task Provedores_de_uso_seguram_o_dono_e_o_contrato_nao_tem_valor_nem_responsavel()
    {
        var supervisor = await SupervisorAsync();
        var (center, employee, supplier) = (await CenterAsync("Dobra"), await EmployeeAsync("Fábio"), await SupplierAsync("Dobradeiras SA"));
        var asset = await CreateAsync(supervisor, Input(Unique("Dobradeira"), value: 70_000m, responsible: employee, center: center, supplier: supplier));
        var id = asset.GetProperty("id").GetGuid();

        await using var scope = await ScopeAsync();
        var sp = scope.ServiceProvider;
        foreach (var usage in new[]
                 {
                     await UsageAsync(sp.GetServices<IPartyUsageProvider>(), p => p.UsageAsync(supplier)),
                     await UsageAsync(sp.GetServices<IWorkCenterUsageProvider>(), p => p.UsageAsync(center)),
                     await UsageAsync(sp.GetServices<IEmployeeUsageProvider>(), p => p.UsageAsync(employee)),
                 })
        {
            usage.Should().Contain("bens do patrimônio");
        }

        var delete = await sp.GetRequiredService<EmployeeService>().DeleteAsync(employee);
        delete.Error!.Code.Should().Be("employee_in_use");

        var info = (await sp.GetRequiredService<IAssets>().FindAsync([id])).Single();
        info.Should().Be(new AssetInfo(id, asset.GetProperty("tag").GetString()!, asset.GetProperty("name").GetString()!, AssetType.Machine, center, AssetStatus.Active));
        typeof(AssetInfo).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["Id", "Tag", "Name", "Type", "WorkCenterId", "Status"]);
        (await sp.GetRequiredService<IAssets>().ByWorkCenterAsync(center)).Select(a => a.Id).Should().Equal(id);
    }

    private static bool WithoutValue(JsonElement element) => !element.TryGetProperty("acquisitionValue", out _);

    private static async Task<List<string?>> UsageAsync<T>(IEnumerable<T> providers, Func<T, Task<string?>> usage)
    {
        var all = new List<string?>();
        foreach (var provider in providers)
        {
            all.Add(await usage(provider));
        }

        return all;
    }

    /// <summary>CA 16: o histórico mostra antes/depois, usuário e data; o valor de aquisição só para quem tem assets.edit.</summary>
    [Fact]
    public async Task Historico_mostra_as_alteracoes_e_o_valor_so_para_quem_edita()
    {
        var supervisor = await SupervisorAsync();
        var asset = await CreateAsync(supervisor, Input(Unique("Gerador"), value: 12_000m));
        var id = asset.GetProperty("id").GetGuid();
        (await supervisor.PutAsJsonAsync($"{Base}/{id}", Input("Gerador diesel", value: 13_500m, version: asset.GetProperty("version").GetUInt32()))).StatusCode.Should().Be(HttpStatusCode.OK);

        var full = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/audit/Asset/{id}");
        var update = full.GetProperty("items").EnumerateArray().First(e => e.GetProperty("action").GetString() == "Updated");
        update.GetProperty("userId").ValueKind.Should().Be(JsonValueKind.String);
        var changes = JsonDocument.Parse(update.GetProperty("changes").GetString()!).RootElement;
        changes.GetProperty("name").GetProperty("new").GetString().Should().Be("Gerador diesel");
        changes.GetProperty("acquisitionValue").GetProperty("new").GetDecimal().Should().Be(13_500m);

        var limited = await (await ActorAsync("warehouse")).GetFromJsonAsync<JsonElement>($"/api/v1/audit/Asset/{id}");
        limited.GetProperty("items").EnumerateArray().Should().NotBeEmpty()
            .And.OnlyContain(e => WithoutValue(JsonDocument.Parse(e.GetProperty("changes").GetString()!).RootElement), "sem assets.edit o valor sai do histórico");
    }

    /// <summary>CA 11: com 5 mil bens, a lista com filtro responde em menos de 1 s.</summary>
    [Fact]
    [Trait(LoadTest.Trait, LoadTest.Value)]
    public async Task Lista_com_5_mil_bens_e_filtro_responde_em_menos_de_1_segundo()
    {
        var supervisor = await SupervisorAsync();
        var center = await CenterAsync("Carga");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAssetsDb>();
            var numbers = scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Persistence.Numbering.INumberSequence>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            for (var i = 0; i < 5000; i++)
            {
                var asset = new Asset { Name = "Ferramenta de carga " + i, Type = i % 5 == 0 ? AssetType.Machine : AssetType.Tool, WorkCenterId = i % 2 == 0 ? center : null, SerialNumber = "LD-" + i, CreatedAt = DateTimeOffset.UtcNow };
                asset.AssignTag(await numbers.NextAsync("PAT"));
                db.Assets.Add(asset);
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await supervisor.GetAsync($"{Base}?workCenterId={center}&type=Machine");
        var watch = Stopwatch.StartNew();
        var response = await supervisor.GetAsync($"{Base}?q=carga&workCenterId={center}&type=Machine&status=Active&pageSize=50");
        watch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("total").GetInt32().Should().Be(500);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }
}
