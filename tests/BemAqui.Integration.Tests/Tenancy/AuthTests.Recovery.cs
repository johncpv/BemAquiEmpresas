using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-04b CA 12 a 15: esqueci minha senha, redefinição de uso único e e-mail de conta bloqueada.</summary>
public partial class AuthTests
{
    private async Task<AsyncServiceScope> TenantAScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Links enfileirados para o destinatário, do mais novo para o mais antigo.</summary>
    private async Task<List<string>> LinksSentAsync(string to, string template)
    {
        await using var scope = await TenantAScopeAsync();
        var models = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().EmailLogs
            .Where(e => e.To == to && e.Template == template).OrderByDescending(e => e.CreatedAt).Select(e => e.Model).ToListAsync();
        return models.Select(m => JsonDocument.Parse(m).RootElement.GetProperty("link").GetString()!).ToList();
    }

    private static string TokenOf(string link) => Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);

    private Task<HttpResponseMessage> PostAsync(string path, object body) => tenancy.ClientFor(Host).PostAsJsonAsync(path, body);

    [Fact]
    public async Task Esqueci_a_senha_responde_igual_e_so_o_cadastrado_recebe_link_no_host_primario()
    {
        var email = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantA, email);
        var unknown = NewEmail();

        var known = await PostAsync("/api/v1/auth/forgot-password", new { email = email.ToUpperInvariant() });
        var other = await PostAsync("/api/v1/auth/forgot-password", new { email = unknown });

        known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        other.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await known.Content.ReadAsStringAsync()).Should().Be(await other.Content.ReadAsStringAsync()).And.Contain(AccountRecovery.ForgotMessage);
        (await LinksSentAsync(unknown, "password-reset")).Should().BeEmpty();
        (await LinksSentAsync(email, "password-reset")).Should().ContainSingle()
            .Which.Should().StartWith("https://" + tenancy.TenantA.Domains.Single(d => d.IsPrimary).Host + "/redefinir-senha?token=");
    }

    [Fact]
    public async Task Redefinicao_e_de_uso_unico_revoga_sessoes_e_mantem_o_2FA()
    {
        var email = NewEmail();
        var (userId, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        // Sessão antes de ligar o 2FA (com ele ligado, o login pediria o código).
        var cookie = CookieValue(RefreshCookieOf(await LoginAsync(email, TenancyFixture.DefaultPassword)));
        await using (var scope = await TenantAScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            (await db.Users.SingleAsync(u => u.Id == userId)).TotpEnabled = true;
            await db.SaveChangesAsync();
        }

        await PostAsync("/api/v1/auth/forgot-password", new { email });
        var token = TokenOf((await LinksSentAsync(email, "password-reset"))[0]);
        const string newPassword = "outra frase longa para entrar";

        (await PostAsync("/api/v1/auth/reset-password", new { token, password = "curta" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PostAsync("/api/v1/auth/reset-password", new { token, password = newPassword })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await PostAsync("/api/v1/auth/reset-password", new { token, password = newPassword });
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Be(AccountRecovery.LinkExpired);
        (await tenancy.ClientFor(Host).SendAsync(Refresh(cookie))).IsSuccessStatusCode.Should().BeFalse();
        (await LoginAsync(email, TenancyFixture.DefaultPassword)).IsSuccessStatusCode.Should().BeFalse();
        (await LoginAsync(email, newPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuditActionsAsync(userId)).Should().Contain("PasswordReset");
        await using (var scope = await TenantAScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IIdentityDb>().Users.SingleAsync(u => u.Id == userId)).TotpEnabled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Link_de_redefinicao_expira_em_uma_hora()
    {
        var email = NewEmail();
        var (userId, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        await PostAsync("/api/v1/auth/forgot-password", new { email });
        var token = TokenOf((await LinksSentAsync(email, "password-reset"))[0]);
        await using (var scope = await TenantAScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var reset = await db.PasswordResets.SingleAsync(r => r.UserId == userId);
            (reset.ExpiresAt - reset.CreatedAt).Should().Be(TimeSpan.FromHours(1));
            await db.PasswordResets.Where(r => r.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var expired = await PostAsync("/api/v1/auth/reset-password", new { token, password = "outra frase longa para entrar" });

        expired.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("auth.link_expired");
    }

    [Fact]
    public async Task Bloqueio_avisa_o_dono_da_conta_com_link_para_redefinir()
    {
        var email = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantA, email);

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "senha errada numero " + i);
        }

        (await LinksSentAsync(email, "account-locked")).Should().ContainSingle().Which.Should().EndWith("/esqueci-senha");
    }
}
