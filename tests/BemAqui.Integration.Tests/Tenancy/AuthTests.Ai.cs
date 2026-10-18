using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-15 CA 1 a 8: Pergunte ao BemAqui com provedor falso, citação validada e prompt sem dado do tenant.</summary>
public partial class AuthTests
{
    private const string AnthropicKey = "sk-ant-teste-chave-0001";
    private const string GroupQuestion = "Como criar um grupo de acesso?";

    private sealed record AiHost(WebApplicationFactory<Program> Factory, FakeAiServer Server) : IAsyncDisposable
    {
        public HttpClient As(Actor actor)
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Host = Host;
            client.DefaultRequestHeaders.Authorization = actor.Client.DefaultRequestHeaders.Authorization;
            return client;
        }

        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private AiHost AiFactory(string timeout = "00:00:05")
    {
        var server = new FakeAiServer();
        var factory = tenancy.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Ai:Timeout", timeout);
            b.ConfigureTestServices(s => s.ConfigureHttpClientDefaults(c => c.ConfigurePrimaryHttpMessageHandler(() => server)));
        });
        return new AiHost(factory, server);
    }

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, string question, string? currentKey = null, bool confirmed = false) =>
        client.PostAsJsonAsync("/api/v1/ajuda/perguntar", new { question, currentKey, confirmed });

    private static async Task<HttpClient> ConfiguredAsync(HttpClient owner, string provider = "anthropic", string model = "claude-haiku-5-5", string? apiKey = AnthropicKey)
    {
        (await owner.DeleteAsync("/api/v1/settings/ia")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var saved = await owner.PutAsJsonAsync("/api/v1/settings/ia", new { provider, model, apiKey });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return owner;
    }

    private async Task<List<AiUsageLog>> UsageAsync()
    {
        await using var scope = await TenantAScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IIdentityDb>().AiUsageLogs.AsNoTracking().OrderBy(u => u.At).ToListAsync();
    }

    [Fact]
    public async Task Sem_provedor_devolve_trechos_sem_chamar_a_IA_e_sem_secao_diz_que_nao_encontrou()
    {
        await using var ai = AiFactory();
        var owner = ai.As(await ActorAsync(Group.OwnerSeedKey));
        await owner.DeleteAsync("/api/v1/settings/ia");

        var answer = await JsonAsync(await AskAsync(owner, GroupQuestion, "acessos-grupos"));
        answer.GetProperty("kind").GetString().Should().Be("snippets");
        answer.GetProperty("text").GetString().Should().Be("Encontrei estes trechos na ajuda:");
        answer.GetProperty("citations")[0].GetProperty("key").GetString().Should().Be("acessos-grupos", "a tela atual tem prioridade (CA 1)");

        var none = await JsonAsync(await AskAsync(owner, "xilofone zebrado quântico"));
        none.GetProperty("kind").GetString().Should().Be("not_found");
        none.GetProperty("text").GetString().Should().Be("Não encontrei isso na ajuda.");
        ai.Server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Resposta_da_IA_cita_a_secao_e_o_prompt_nao_leva_dado_do_tenant()
    {
        await using var ai = AiFactory();
        var ownerActor = await ActorAsync(Group.OwnerSeedKey);
        var owner = await ConfiguredAsync(ai.As(ownerActor));
        var marker = "Grupo Sigiloso " + Guid.NewGuid().ToString("N")[..8];
        var group = await owner.PostAsJsonAsync("/api/v1/identity/groups", new { name = marker, description = "Clientes VIP com faturamento de R$ 98.765,43" });
        group.IsSuccessStatusCode.Should().BeTrue(await group.Content.ReadAsStringAsync());
        ai.Server.Reply = """{"resposta":"Abra Acessos e use Novo grupo [a1].","fontes":["a1"],"naoEncontrou":false}""";

        var answer = await JsonAsync(await AskAsync(owner, GroupQuestion, "acessos-grupos"));

        answer.GetProperty("kind").GetString().Should().Be("answer");
        answer.GetProperty("text").GetString().Should().Be("Abra Acessos e use Novo grupo.");
        answer.GetProperty("citations").GetArrayLength().Should().Be(1);
        var call = ai.Server.Calls.Should().ContainSingle().Subject;
        call.Host.Should().Be("api.anthropic.com");
        call.Headers["x-api-key"].Should().Be(AnthropicKey);
        call.Body.Should().Contain(GroupQuestion).And.Contain("Grupos de acesso");
        string[] tenantData = ["Empresa A Ltda", tenancy.TenantA.Key, Host, marker, "98.765,43", "Usuário de teste", ownerActor.Id.ToString(), "konebras.com.br"];
        foreach (var data in tenantData)
        {
            call.Body.Should().NotContain(data, "CA 7: nenhum dado do banco do tenant vai para a IA");
        }

        var usage = (await UsageAsync()).Last();
        usage.Outcome.Should().Be(AiUsageLog.Answered);
        usage.UserId.Should().Be(ownerActor.Id);
        usage.Provider.Should().Be("anthropic");
        usage.InputTokens.Should().Be(321);
        usage.OutputTokens.Should().Be(45);
    }

    [Theory]
    [InlineData("""{"resposta":"Use Novo grupo.","fontes":[],"naoEncontrou":false}""")]
    [InlineData("""{"resposta":"Use Novo grupo.","fontes":["a9"],"naoEncontrou":false}""")]
    [InlineData("""{"resposta":"Use Novo grupo [a7].","fontes":["a1"],"naoEncontrou":false}""")]
    [InlineData("isto não é JSON")]
    public async Task Resposta_sem_citacao_valida_e_descartada_e_volta_os_trechos(string reply)
    {
        await using var ai = AiFactory();
        var owner = await ConfiguredAsync(ai.As(await ActorAsync(Group.OwnerSeedKey)));
        ai.Server.Reply = reply;

        var answer = await JsonAsync(await AskAsync(owner, GroupQuestion));

        answer.GetProperty("kind").GetString().Should().Be("snippets");
        (await UsageAsync()).Last().Outcome.Should().Be(AiUsageLog.NoCitation);
    }

    [Fact]
    public async Task Provedor_com_erro_ou_lento_devolve_os_trechos_e_registra_a_falha()
    {
        await using var ai = AiFactory(timeout: "00:00:00.300");
        var owner = await ConfiguredAsync(ai.As(await ActorAsync(Group.OwnerSeedKey)));

        ai.Server.Status = HttpStatusCode.InternalServerError;
        (await JsonAsync(await AskAsync(owner, GroupQuestion))).GetProperty("kind").GetString().Should().Be("snippets");

        ai.Server.Status = HttpStatusCode.OK;
        ai.Server.Delay = TimeSpan.FromSeconds(3);
        (await JsonAsync(await AskAsync(owner, GroupQuestion))).GetProperty("kind").GetString().Should().Be("snippets");

        (await UsageAsync()).TakeLast(2).Select(u => u.Outcome).Should().Equal(AiUsageLog.Failed, AiUsageLog.Failed);
    }

    [Fact]
    public async Task Ajuda_de_modulo_sem_permissao_nao_vai_para_a_IA_nem_volta()
    {
        await using var ai = AiFactory();
        await ConfiguredAsync(ai.As(await ActorAsync(Group.OwnerSeedKey)));
        var nobody = ai.As(await ActorAsync());

        var answer = await JsonAsync(await AskAsync(nobody, "Como criar um grupo de acesso e mudar a segurança?", "acessos-grupos"));

        if (answer.TryGetProperty("citations", out var citations))
        {
            citations.EnumerateArray().Select(c => c.GetProperty("key").GetString()).Should().NotContain(["acessos-grupos", "acessos-usuarios", "configuracoes-seguranca"]);
        }

        ai.Server.Calls.Should().AllSatisfy(c => c.Body.Should().NotContain("Grupos de acesso"));
    }

    [Fact]
    public async Task Pergunta_com_dado_da_empresa_pede_confirmacao_antes_de_enviar()
    {
        await using var ai = AiFactory();
        var owner = await ConfiguredAsync(ai.As(await ActorAsync(Group.OwnerSeedKey)));
        const string question = "Como dar acesso ao grupo para joao@cliente.com.br?";

        (await JsonAsync(await AskAsync(owner, question))).GetProperty("kind").GetString().Should().Be("confirm");
        ai.Server.Calls.Should().BeEmpty();

        await AskAsync(owner, question, confirmed: true);
        ai.Server.Calls.Should().ContainSingle();
    }
}
