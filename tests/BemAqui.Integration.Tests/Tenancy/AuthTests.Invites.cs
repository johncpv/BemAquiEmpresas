using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-04b CA 11 e 17 a 19: convite (inclusive o do proprietário no provisionamento) e rate limit.</summary>
public partial class AuthTests
{
    private async Task<string> InviteAsync(string email)
    {
        await using (var scope = await TenantAScopeAsync())
        {
            var invites = scope.ServiceProvider.GetRequiredService<Invites>();
            (await invites.EnsureInviteAsync(email, isOwner: false, invitedBy: null)).Should().NotBeNull();
            (await invites.SendAsync(email, "Maria")).Should().BeTrue();
        }

        return TokenOf((await LinksSentAsync(email, "invite"))[0]);
    }

    private async Task<JsonElement> InspectAsync(string token) =>
        await (await PostAsync("/api/v1/auth/invites/inspect", new { token })).Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Provisionamento_cria_o_proprietario_como_convite_e_envia_o_email()
    {
        var owner = "dono@" + Host;
        await using (var scope = await TenantAScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == owner);
            user.Status.Should().Be(UserStatus.Invited);
            (await db.UserInvites.Where(i => i.UserId == user.Id).ToListAsync()).Should().ContainSingle().Which.IsOwner.Should().BeTrue();
        }

        (await LinksSentAsync(owner, "invite")).Should().ContainSingle().Which.Should().StartWith("https://" + Host + "/convite?token=");
    }

    [Fact]
    public async Task Convite_valido_ativa_o_usuario_entra_e_oferece_o_2FA()
    {
        var email = NewEmail();
        var token = await InviteAsync(email);

        var info = await InspectAsync(token);
        info.GetProperty("state").GetString().Should().Be("Valid");
        info.GetProperty("email").GetString().Should().Be(email);

        var weak = await PostAsync("/api/v1/auth/invites/accept", new { token, name = "Ana", password = "curta" });
        weak.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await weak.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("password", out _).Should().BeTrue();

        var accepted = await PostAsync("/api/v1/auth/invites/accept", new { token, name = "Ana Souza", password = TenancyFixture.DefaultPassword });

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        RefreshCookieOf(accepted).Should().NotBeNullOrEmpty();
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("offerTwoFactor").GetBoolean().Should().BeTrue();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        await using (var scope = await TenantAScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.Status.Should().Be(UserStatus.Active);
            user.Name.Should().Be("Ana Souza");
            (await db.UserInvites.SingleAsync(i => i.UserId == user.Id)).AcceptedAt.Should().NotBeNull();
            (await AuditActionsAsync(user.Id)).Should().Contain("InviteAccepted");
        }

        (await InspectAsync(token)).GetProperty("message").GetString().Should().Be(Invites.AcceptedMessage);
        (await PostAsync("/api/v1/auth/invites/accept", new { token, password = TenancyFixture.DefaultPassword })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Convite_expirado_ou_reenviado_explica_e_orienta_pedir_novo()
    {
        var email = NewEmail();
        var first = await InviteAsync(email);
        var second = await InviteAsync(email);

        (await InspectAsync(first)).GetProperty("state").GetString().Should().Be("NotFound");
        await using (var scope = await TenantAScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<IIdentityDb>().UserInvites.Where(i => i.Email == email)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var expired = await InspectAsync(second);
        expired.GetProperty("state").GetString().Should().Be("Expired");
        expired.GetProperty("message").GetString().Should().Be(Invites.ExpiredMessage);
        (await PostAsync("/api/v1/auth/invites/accept", new { token = second, password = TenancyFixture.DefaultPassword })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Rate_limit_de_autenticacao_responde_429_com_Retry_After()
    {
        await using var limited = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:Auth:PerIpPerMinute", "3"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Host = Host;

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            responses.Add(await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = NewEmail() }));
        }

        responses.Take(3).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        var rejected = responses[3];
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("rate_limited");
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
