using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-09a CA 9: o tema fica no perfil e vale em outro dispositivo.</summary>
public partial class AuthTests
{
    [Fact]
    public async Task Tema_salvo_no_perfil_volta_em_outra_sessao()
    {
        var user = await ActorAsync();
        (await JsonAsync(await user.Client.GetAsync("/api/v1/me/preferences"))).GetProperty("theme").GetString().Should().Be("system", "Automático é o padrão");

        (await user.Client.PutAsJsonAsync("/api/v1/me/preferences", new { theme = "dark" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.Client.PutAsJsonAsync("/api/v1/me/preferences", new { theme = "roxo" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await JsonAsync(await user.Client.GetAsync("/api/v1/me/preferences"))).GetProperty("theme").GetString().Should().Be("dark");
    }
}
