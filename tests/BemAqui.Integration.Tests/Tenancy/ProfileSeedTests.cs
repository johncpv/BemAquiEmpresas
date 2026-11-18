using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.Platform.Application.Modules;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>
/// PERF-04: pacotes de dados iniciais por perfil e segmento (Comércio completo, idempotência sem sobrescrever, fábrica intocada, perfil
/// adicionado depois) e o checklist "Primeiros passos" calculado dos dados.
/// </summary>
public class ProfileSeedTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private static readonly string[] RetailMethodKeys = ["dinheiro", "pix", "debito", "credito", "credito-parcelado", "vale"];

    private async Task<AsyncServiceScope> ScopeAsync(Guid tenantId)
    {
        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenantId))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> QueryAsync<T>(Guid tenantId, Func<TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync(tenantId);
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Todos os seeders, como o <c>migrate</c> de cada deploy (o executor dos pacotes entre eles).</summary>
    private async Task SeedAllAsync(Guid tenantId)
    {
        await using var scope = await ScopeAsync(tenantId);
        foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }
    }

    private async Task<Tenant> ProvisionRetailAsync(string key, string segment)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ProvisioningService>().ProvisionAsync(new ProvisionRequest(
            key, "Loja Teste Ltda", "47.225.005/0001-86", key + ".localhost", "dono@" + key + ".localhost", null, Profiles: [new ProfileChoice(BusinessProfileCatalog.Retail, segment)]));
        result.Success.Should().BeTrue(result.Error);
        return await tenancy.FindAsync(key);
    }

    private async Task<HttpClient> OwnerAsync(Tenant tenant, string? host = null)
    {
        host ??= tenant.Key + ".localhost";
        var (id, stamp) = await tenancy.SeedUserAsync(tenant, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync(tenant.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var owner = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = owner });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return client;
    }

    /// <summary>
    /// Retrato de todas as tabelas do banco do tenant (contagem e hash do conteúdo), menos as de rastro (auditoria e outbox): o que a
    /// regressão da Konebras compara antes e depois do seed (CA 5).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> SnapshotAsync(Guid tenantId) => await QueryAsync(tenantId, async db =>
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = """
                SELECT quote_ident(table_schema) || '.' || quote_ident(table_name) FROM information_schema.tables
                WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND table_name NOT IN ('audit_log', 'outbox_messages', 'inbox_messages', '__EFMigrationsHistory') ORDER BY 1
                """;
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table} t";
            snapshot[table] = (string)(await command.ExecuteScalarAsync())!;
        }

        return (IReadOnlyDictionary<string, string>)snapshot;
    });

    [Fact]
    public async Task Fabrica_nao_ganha_nem_muda_nada_com_os_seeds_e_nao_tem_primeiros_passos()
    {
        // CA 5: o tenant B nasce como a Konebras (perfil factory, migration da PERF-01). Retrato antes e depois de um migrate inteiro.
        await SeedAllAsync(tenancy.TenantB.Id); // estabiliza o que outros seeders já fariam no deploy
        var before = await SnapshotAsync(tenancy.TenantB.Id);
        await SeedAllAsync(tenancy.TenantB.Id);
        await using (var scope = await ScopeAsync(tenancy.TenantB.Id))
        {
            scope.ServiceProvider.GetRequiredService<TenantContextHolder>().Resolved!.Profiles.Should().Equal(BusinessProfileCatalog.Factory);
            await scope.ServiceProvider.GetRequiredService<ProfileSeedRunner>().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var after = await SnapshotAsync(tenancy.TenantB.Id);
        after.Should().Equal(before, "a fábrica não roda pacote nenhum");
        (await QueryAsync(tenancy.TenantB.Id, db => db.PaymentMethods.IgnoreQueryFilters().CountAsync())).Should().Be(0);
        (await QueryAsync(tenancy.TenantB.Id, db => db.Locations.CountAsync(l => l.SeedKey == "loja" || l.SeedKey == "deposito"))).Should().Be(0);
        (await QueryAsync(tenancy.TenantB.Id, db => db.Stores.CountAsync())).Should().Be(0, "PDV-01: a fábrica não ganha loja nem caixa");

        var owner = await OwnerAsync(tenancy.TenantB, "b.localhost");
        var steps = await owner.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/first-steps");
        steps.GetProperty("applies").GetBoolean().Should().BeFalse("a fábrica não vê o cartão");
        steps.GetProperty("steps").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Provisionar_Comercio_padaria_cria_o_pacote_completo_e_a_conferencia_e_auditada()
    {
        var tenant = await ProvisionRetailAsync("loja-padaria", "bakery");

        // CA 1: locais, contas, adquirente, formas e grupos.
        (await QueryAsync(tenant.Id, db => db.Locations.Where(l => l.SeedKey == "loja" || l.SeedKey == "deposito").Select(l => l.Name).ToListAsync()))
            .Should().BeEquivalentTo(["Loja", "Depósito"]);
        var accounts = await QueryAsync(tenant.Id, db => db.BankAccounts.Where(a => a.SeedKey != null).ToDictionaryAsync(a => a.SeedKey!, a => (a.Id, a.Name, a.Kind)));
        accounts.Values.Where(a => a.Kind == BankAccountKind.Cash).Select(a => a.Name).Should().BeEquivalentTo(["Caixa PDV 01", "Cofre", "Quebra de caixa"]);
        accounts.Should().ContainKey("banco-principal", "tenant novo não tem conta bancária: Pix e cartões precisam de uma");
        var acquirer = await QueryAsync(tenant.Id, db => db.Parties.Include(p => p.Roles).SingleAsync(p => p.SeedKey == "maquininha"));
        acquirer.LegalName.Should().Be("Maquininha");
        acquirer.Roles.Select(r => r.Role).Should().Equal(PartyRoleKind.Acquirer);
        var methods = await QueryAsync(tenant.Id, db => db.PaymentMethods.Where(m => m.SeedKey != null).ToListAsync());
        methods.Select(m => m.SeedKey).Should().BeEquivalentTo(RetailMethodKeys);
        methods.Should().OnlyContain(m => m.ConfirmedAt == null, "CA 2: nascem \"A confirmar\"");
        methods.Single(m => m.SeedKey == "dinheiro").DestinationAccountId.Should().Be(accounts["caixa-pdv-01"].Id);
        methods.Where(m => m.SeedKey is "debito" or "credito" or "credito-parcelado" or "vale").Should().OnlyContain(m => m.AcquirerPartyId == acquirer.Id);
        methods.Single(m => m.SeedKey == "pix").AcquirerPartyId.Should().BeNull();
        (await QueryAsync(tenant.Id, db => db.Groups.Where(g => g.SeedKey == "store_manager" || g.SeedKey == "cashier" || g.SeedKey == "stock_clerk").Select(g => g.Name).ToListAsync()))
            .Should().BeEquivalentTo(["Gerente de loja", "Operador de caixa", "Estoquista"]);
        var modules = await QueryAsync(tenant.Id, db => db.GroupPermissions.Select(p => p.ModuleCode).Distinct().ToListAsync());
        modules.Should().OnlyContain(m => ModuleCatalog.All.Any(c => c.Code == m), "só permissões de módulos que já existem");

        (await QueryAsync(tenant.Id, db => db.ProductCategories.Where(c => c.SeedKey == "geral").Select(c => c.Name).ToListAsync())).Should().Equal("Geral");
        (await QueryAsync(tenant.Id, db => db.PriceLists.Where(l => l.SeedKey == "balcao").Select(l => l.Name).ToListAsync())).Should().Equal("Balcão");

        // PDV-01 CA 2: a loja "Loja" ligada ao local Loja e à tabela Balcão, com o caixa 01 na conta Caixa PDV 01; e o grupo Fiscal de caixa.
        var store = await QueryAsync(tenant.Id, db => db.Stores.SingleAsync(s => s.SeedKey == "loja"));
        store.Code.Should().Be("LOJA");
        store.StockLocationId.Should().Be(await QueryAsync(tenant.Id, db => db.Locations.Where(l => l.SeedKey == "loja").Select(l => l.Id).SingleAsync()));
        store.DefaultPriceListId.Should().Be(await QueryAsync(tenant.Id, db => db.PriceLists.Where(l => l.SeedKey == "balcao").Select(l => l.Id).SingleAsync()));
        var register = await QueryAsync(tenant.Id, db => db.Registers.SingleAsync(r => r.StoreId == store.Id));
        register.Number.Should().Be(1);
        register.CashAccountId.Should().Be(accounts["caixa-pdv-01"].Id);
        (await QueryAsync(tenant.Id, db => db.Groups.CountAsync(g => g.SeedKey == "cash_supervisor"))).Should().Be(1);
        (await QueryAsync(tenant.Id, db => db.GroupPermissions.CountAsync(p => p.ModuleCode == "pos" && p.Action == "manage_devices"))).Should().BeGreaterThan(0, "o Gerente de loja registra aparelhos");

        // CA 4: o segmento de alimentação acrescenta as unidades.
        (await QueryAsync(tenant.Id, db => db.Units.Where(u => u.SeedKey != null).Select(u => u.Code).ToListAsync())).Should().Contain(["G", "ML", "DZ", "PCT", "POR"]);

        // CA 2: taxa e prazo zerados (sem taxa: a venda entra pelo valor cheio) e "Marcar como conferido" auditado.
        var owner = await OwnerAsync(tenant);
        var debit = methods.Single(m => m.SeedKey == "debito").Id;
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/payment-methods/{debit}");
        detail.GetProperty("confirmed").GetBoolean().Should().BeFalse();
        detail.GetProperty("acquirerName").GetString().Should().Be("Maquininha");
        (await owner.PostAsync($"/api/v1/finance/payment-methods/{debit}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await QueryAsync(tenant.Id, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "PaymentMethod" && a.EntityId == debit.ToString()).Select(a => a.Changes).ToListAsync()))
            .Should().Contain(c => c.Contains("confirmedAt", StringComparison.Ordinal), "a conferência é auditada");
    }

    [Fact]
    public async Task Perfil_adicionado_depois_semeia_e_rodar_de_novo_nao_duplica_nem_sobrescreve()
    {
        // CA 3: o tenant A nasce fábrica; a aba Perfis acrescenta Comércio (o endpoint chama estes dois serviços).
        var tenantId = tenancy.TenantA.Id;

        // O Catálogo já foi usado antes do perfil (a CAT-01 cria a "Geral" e a "Balcão" no primeiro uso): o pacote reusa, não duplica.
        await using (var scope = await ScopeAsync(tenantId))
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDefaults>().EnsureAsync();
        }

        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<TenantModuleService>().AddProfileAsync(tenantId, BusinessProfileCatalog.Retail, "counter", null)).IsSuccess.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<TenantProfileSeeding>().RunAsync(tenantId)).Should().BeTrue();
        }

        (await QueryAsync(tenantId, db => db.ProductCategories.IgnoreQueryFilters().CountAsync(c => c.SeedKey == "geral"))).Should().Be(1);
        (await QueryAsync(tenantId, db => db.PriceLists.CountAsync(l => l.SeedKey == "balcao"))).Should().Be(1);
        (await QueryAsync(tenantId, db => db.PaymentMethods.CountAsync(m => m.SeedKey != null))).Should().Be(RetailMethodKeys.Length);
        (await QueryAsync(tenantId, db => db.Units.CountAsync(u => u.Code == "DZ"))).Should().Be(0, "Balcão não é segmento de alimentação");

        // O tenant mexe: renomeia o Cofre e o Dinheiro, exclui o Depósito, troca o nome do grupo Estoquista e inativa a Maquininha.
        await using (var scope = await ScopeAsync(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            (await db.BankAccounts.SingleAsync(a => a.SeedKey == "cofre")).Name = "Cofre da gerência";
            (await db.PaymentMethods.SingleAsync(m => m.SeedKey == "dinheiro")).Name = "Dinheiro (espécie)";
            (await db.Locations.SingleAsync(l => l.SeedKey == "deposito")).MarkDeleted(DateTimeOffset.UtcNow);
            (await db.Groups.SingleAsync(g => g.SeedKey == "stock_clerk")).Name = "Almoxarife da loja";
            (await db.Parties.SingleAsync(p => p.SeedKey == "maquininha")).IsActive = false;
            await db.SaveChangesAsync();
        }

        var counts = await CountsAsync(tenantId);
        await SeedAllAsync(tenantId);
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<TenantModuleService>().AddProfileAsync(tenantId, BusinessProfileCatalog.Retail, "counter", null)).Value.Changed.Should().BeFalse();
            (await scope.ServiceProvider.GetRequiredService<TenantProfileSeeding>().RunAsync(tenantId)).Should().BeTrue();
        }

        (await CountsAsync(tenantId)).Should().Be(counts, "nada é duplicado nem recriado");
        (await QueryAsync(tenantId, db => db.BankAccounts.SingleAsync(a => a.SeedKey == "cofre"))).Name.Should().Be("Cofre da gerência");
        (await QueryAsync(tenantId, db => db.PaymentMethods.SingleAsync(m => m.SeedKey == "dinheiro"))).Name.Should().Be("Dinheiro (espécie)");
        (await QueryAsync(tenantId, db => db.Groups.SingleAsync(g => g.SeedKey == "stock_clerk"))).Name.Should().Be("Almoxarife da loja");
        (await QueryAsync(tenantId, db => db.Parties.SingleAsync(p => p.SeedKey == "maquininha"))).IsActive.Should().BeFalse();
        (await QueryAsync(tenantId, db => db.Locations.AnyAsync(l => l.SeedKey == "deposito"))).Should().BeFalse("o Depósito excluído não volta");

        // Um segmento de alimentação adicionado depois acrescenta só as unidades dele.
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<TenantModuleService>().AddProfileAsync(tenantId, BusinessProfileCatalog.Retail, "snack_bar", null)).IsSuccess.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<TenantProfileSeeding>().RunAsync(tenantId)).Should().BeTrue();
        }

        (await QueryAsync(tenantId, db => db.Units.CountAsync(u => u.SeedKey == "dz"))).Should().Be(1);
        (await QueryAsync(tenantId, db => db.PaymentMethods.CountAsync(m => m.SeedKey != null))).Should().Be(RetailMethodKeys.Length);
    }

    private Task<string> CountsAsync(Guid tenantId) => QueryAsync(tenantId, async db =>
        string.Join(",", await db.BankAccounts.IgnoreQueryFilters().CountAsync(), await db.PaymentMethods.IgnoreQueryFilters().CountAsync(),
            await db.Locations.IgnoreQueryFilters().CountAsync(), await db.Groups.CountAsync(), await db.GroupPermissions.CountAsync(),
            await db.Parties.IgnoreQueryFilters().CountAsync(), await db.Units.IgnoreQueryFilters().CountAsync(),
            await db.ProductCategories.IgnoreQueryFilters().CountAsync(), await db.PriceLists.CountAsync()));

    [Fact]
    public async Task Primeiros_passos_marcam_sozinhos_pelo_dado()
    {
        // CA 6 e 7: cinco passos; empresa e formas se concluem pelo dado; os que dependem de stories futuras ficam "Em breve".
        var tenant = await ProvisionRetailAsync("loja-passos", "counter");
        var owner = await OwnerAsync(tenant);
        var card = await owner.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/first-steps");
        card.GetProperty("applies").GetBoolean().Should().BeTrue();
        Steps(card).Select(s => s.Key).Should().Equal("company", "products", "payments", "printer", "first_sale");
        Steps(card).Where(s => !s.Available).Select(s => s.Key).Should().Equal("printer", "first_sale");
        Steps(card).Single(s => s.Key == "products").Url.Should().Be("/app/catalogo/produtos/importar", "o passo leva à importação da CAT-02");
        Steps(card).Should().OnlyContain(s => !s.Done);
        card.GetProperty("done").GetInt32().Should().Be(0);
        card.GetProperty("total").GetInt32().Should().Be(5);
        Steps(card).Single(s => s.Key == "payments").Url.Should().Be("/app/financeiro/formas-de-pagamento");

        // Formas: conferir todas conclui o passo 3.
        var ids = await QueryAsync(tenant.Id, db => db.PaymentMethods.Select(m => m.Id).ToListAsync());
        foreach (var id in ids)
        {
            (await owner.PostAsync($"/api/v1/finance/payment-methods/{id}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        card = await owner.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/first-steps");
        Steps(card).Single(s => s.Key == "payments").Done.Should().BeTrue();

        // Empresa: endereço e contato completos levam ao regime; conferido, o passo 1 se conclui.
        await using (var scope = await ScopeAsync(tenant.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var profile = await db.CompanyProfiles.FirstOrDefaultAsync();
            if (profile is null)
            {
                profile = new CompanyProfile { LegalName = "Loja Teste Ltda" };
                db.CompanyProfiles.Add(profile);
            }

            profile.Street = "Rua das Flores";
            profile.Number = "10";
            profile.City = "Curitiba";
            profile.State = "PR";
            profile.Phone = "4133330000";
            await db.SaveChangesAsync();
        }

        card = await owner.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/first-steps");
        var company = Steps(card).Single(s => s.Key == "company");
        company.Done.Should().BeFalse("falta conferir o regime");
        company.Url.Should().Be("/app/configuracoes/fiscal");
        (await owner.PostAsync("/api/v1/fiscal/settings/confirmations", new StringContent("""{"keys":["regime"]}""", Encoding.UTF8, "application/json")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        card = await owner.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/first-steps");
        Steps(card).Single(s => s.Key == "company").Done.Should().BeTrue();
        card.GetProperty("done").GetInt32().Should().Be(2);
        card.GetProperty("allDone").GetBoolean().Should().BeFalse("os passos \"Em breve\" ainda não se concluem");
    }

    private static List<(string Key, bool Done, bool Available, string? Url)> Steps(JsonElement card) =>
        card.GetProperty("steps").EnumerateArray().Select(s => (s.GetProperty("key").GetString()!, s.GetProperty("done").GetBoolean(), s.GetProperty("available").GetBoolean(),
            s.GetProperty("url").ValueKind == JsonValueKind.Null ? null : s.GetProperty("url").GetString())).ToList();
}
