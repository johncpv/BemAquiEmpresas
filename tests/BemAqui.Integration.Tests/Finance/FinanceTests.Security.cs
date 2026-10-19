using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class FinanceTests
{
    [Fact]
    public async Task Troca_de_banco_agencia_ou_conta_exige_aprovar_e_avisa_o_Proprietario()
    {
        var (ownerId, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (_, financeiro) = await ActorAsync("finance");
        var id = await CreatedAsync(await financeiro.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Itaú " + Guid.NewGuid().ToString("N")[..4])));
        var name = (await JsonAsync(await financeiro.GetAsync($"/api/v1/finance/bank-accounts/{id}"))).GetProperty("name").GetString()!;

        var denied = await financeiro.PutAsJsonAsync($"/api/v1/finance/bank-accounts/{id}", Bank(name, account: "99999999-0"));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem finance.approve não troca os dados bancários (CA 9)");
        Code(await JsonAsync(denied)).Should().Be("forbidden");
        (await financeiro.PutAsJsonAsync($"/api/v1/finance/bank-accounts/{id}", Bank(name + " (renomeada)"))).StatusCode.Should().Be(HttpStatusCode.OK, "o nome não é dado bancário");

        var changed = await owner.PutAsJsonAsync($"/api/v1/finance/bank-accounts/{id}", Bank(name + " (renomeada)", account: "99999999-0"));
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var notes = await QueryAsync(db => db.Notifications.AsNoTracking().Where(n => n.Type == "finance.bank_data_changed" && n.Title.Contains(name)).ToListAsync());
        notes.Should().Contain(n => n.UserId == ownerId).And.OnlyContain(n => n.Title.StartsWith("Dados da conta " + name, StringComparison.Ordinal));
        var audit = await QueryAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "BankAccount" && a.EntityId == id.ToString()).OrderBy(a => a.At).ToListAsync());
        audit.Last().Changes.Should().Contain("12345678-9").And.Contain("99999999-0", "o histórico guarda antes e depois (CA 9 e 19)");
    }

    [Fact]
    public async Task Quem_so_ve_recebe_agencia_e_conta_mascaradas_do_servidor()
    {
        var (_, financeiro) = await ActorAsync("finance");
        var (_, viewer) = await ActorAsync("view");
        var id = await CreatedAsync(await financeiro.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Bradesco " + Guid.NewGuid().ToString("N")[..4])));

        var full = await JsonAsync(await financeiro.GetAsync($"/api/v1/finance/bank-accounts/{id}"));
        full.GetProperty("account").GetString().Should().Be("12345678-9");
        var masked = await JsonAsync(await viewer.GetAsync($"/api/v1/finance/bank-accounts/{id}"));
        masked.GetProperty("agency").GetString().Should().Be("****-5");
        masked.GetProperty("account").GetString().Should().Be("*****678-9");
        masked.GetProperty("masked").GetBoolean().Should().BeTrue();
        (await viewer.GetStringAsync("/api/v1/finance/bank-accounts")).Should().NotContain("12345678-9", "a lista também sai mascarada (CA 10)");
        (await viewer.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Não pode"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sem_grupo_com_finance_e_403_forbidden_e_todo_endpoint_tem_as_tres_camadas()
    {
        var (_, nobody) = await ActorAsync();
        var forbidden = await nobody.GetAsync("/api/v1/finance/bank-accounts");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");

        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        // A liberação por tenant é coberta pelo FND-06; aqui basta todo endpoint do módulo ter as três camadas (CA 18).
        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/finance", StringComparison.Ordinal) == true).ToList();
        endpoints.Should().HaveCountGreaterThan(14);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("finance", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "finance.", StringComparison.Ordinal), endpoint.DisplayName);
        }

        (await owner.GetAsync("/api/v1/finance/banks")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
