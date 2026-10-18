using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-15 CA 9 a 13: configuração do provedor, chave write-only, teste de conexão e troca sem reinício.</summary>
public partial class AuthTests
{
    [Fact]
    public async Task Trocar_de_provedor_vale_na_proxima_pergunta_e_exige_a_chave_do_novo()
    {
        await using var ai = AiFactory();
        var owner = await ConfiguredAsync(ai.As(await ActorAsync(Group.OwnerSeedKey)));

        var missingKey = await owner.PutAsJsonAsync("/api/v1/settings/ia", new { provider = "openai", model = "gpt-5-mini", apiKey = (string?)null });
        missingKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await missingKey.Content.ReadAsStringAsync()).Should().Contain("Informe a chave de API do novo provedor.");

        (await owner.PutAsJsonAsync("/api/v1/settings/ia", new { provider = "openai", model = "gpt-5-mini", apiKey = "sk-openai-teste-0002" })).StatusCode.Should().Be(HttpStatusCode.OK);
        await AskAsync(owner, GroupQuestion);
        (await owner.PutAsJsonAsync("/api/v1/settings/ia", new { provider = "gemini", model = "gemini-2.5-flash", apiKey = "AIza-teste-gemini-0003" })).StatusCode.Should().Be(HttpStatusCode.OK);
        await AskAsync(owner, GroupQuestion);

        ai.Server.Calls.Select(c => c.Host).Should().Equal("api.openai.com", "generativelanguage.googleapis.com");
        ai.Server.Calls[0].Headers["authorization"].Should().Be("Bearer sk-openai-teste-0002");
        ai.Server.Calls[1].Path.Should().Be("/v1beta/models/gemini-2.5-flash:generateContent");
        ai.Server.Calls[1].Headers["x-goog-api-key"].Should().Be("AIza-teste-gemini-0003");
        (await UsageAsync()).TakeLast(2).Select(u => (u.Provider, u.InputTokens)).Should().Equal(("openai", 300), ("gemini", 310));
    }

    [Fact]
    public async Task Chave_nunca_volta_na_API_nem_na_auditoria()
    {
        await using var ai = AiFactory();
        var owner = await ConfiguredAsync(ai.As(await ActorAsync(Group.OwnerSeedKey)));

        var raw = await (await owner.GetAsync("/api/v1/settings/ia")).Content.ReadAsStringAsync();
        raw.Should().NotContain(AnthropicKey).And.NotContain("apiKey").And.NotContain("ipher", "CA 10");
        var settings = JsonDocument.Parse(raw).RootElement;
        settings.GetProperty("configured").GetBoolean().Should().BeTrue();
        settings.GetProperty("provider").GetString().Should().Be("anthropic");
        settings.GetProperty("keySetAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        await using var scope = await TenantAScopeAsync();
        var cipher = await scope.ServiceProvider.GetRequiredService<IIdentityDb>().AiSettings.Select(s => s.ApiKeyCipher).SingleAsync();
        cipher.Should().NotContain(AnthropicKey);
        var audit = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.Where(a => a.Entity == nameof(AiSettings)).Select(a => a.Changes).ToListAsync();
        audit.Should().NotBeEmpty("alterar a configuração é auditado");
        audit.Should().AllSatisfy(c => c.Should().NotContain(AnthropicKey).And.NotContain(cipher));
    }

    [Fact]
    public async Task Testar_conexao_traduz_o_erro_e_nao_salva()
    {
        await using var ai = AiFactory();
        var owner = ai.As(await ActorAsync(Group.OwnerSeedKey));
        await owner.DeleteAsync("/api/v1/settings/ia");

        ai.Server.Status = HttpStatusCode.Unauthorized;
        var refused = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/settings/ia/testar", new { provider = "anthropic", model = "claude-haiku-5-5", apiKey = "sk-ant-errada-000" }));
        refused.GetProperty("ok").GetBoolean().Should().BeFalse();
        refused.GetProperty("message").GetString().Should().Contain("chave de API foi recusada");

        ai.Server.Status = HttpStatusCode.NotFound;
        (await JsonAsync(await owner.PostAsJsonAsync("/api/v1/settings/ia/testar", new { provider = "anthropic", model = "claude-nao-existe", apiKey = AnthropicKey })))
            .GetProperty("message").GetString().Should().Contain("não encontrou esse modelo");

        ai.Server.Status = HttpStatusCode.OK;
        var ok = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/settings/ia/testar", new { provider = "anthropic", model = "claude-haiku-5-5", apiKey = AnthropicKey }));
        ok.GetProperty("message").GetString().Should().Be("Conexão OK");

        (await JsonAsync(await owner.GetAsync("/api/v1/settings/ia"))).GetProperty("configured").GetBoolean().Should().BeFalse("testar não salva (CA 11)");
        (await UsageAsync()).TakeLast(3).Select(u => u.Outcome).Should().Equal(AiUsageLog.TestFailed, AiUsageLog.TestFailed, AiUsageLog.TestOk);
    }

    [Fact]
    public async Task Sem_settings_edit_nao_salva_nem_testa()
    {
        await using var ai = AiFactory();
        var nobody = ai.As(await ActorAsync());
        var body = new { provider = "anthropic", model = "claude-haiku-5-5", apiKey = AnthropicKey };

        foreach (var response in new[] { await nobody.PutAsJsonAsync("/api/v1/settings/ia", body), await nobody.PostAsJsonAsync("/api/v1/settings/ia/testar", body) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("forbidden");
        }

        ai.Server.Calls.Should().BeEmpty();
    }
}
