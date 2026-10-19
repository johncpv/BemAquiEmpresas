using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BemAqui.BuildingBlocks.Lookups;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Parties;

/// <summary>CAD-01b: consulta de CNPJ e de CEP com a BrasilAPI trocada por um servidor falso.</summary>
public partial class LookupTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string Host = "a.localhost";

    /// <summary>BrasilAPI falsa: respostas por caminho, com contagem de chamadas; "slow" demora 6 s.</summary>
    internal sealed class FakeBrasilApi : HttpMessageHandler
    {
        public ConcurrentDictionary<string, (HttpStatusCode Status, string Body)> Responses { get; } = new(StringComparer.Ordinal);

        public ConcurrentBag<string> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add(path);
            if (!Responses.TryGetValue(path, out var r))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (r.Body == "slow")
            {
                await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            }

            var response = new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") };
            if (r.Status == HttpStatusCode.Found)
            {
                response.Headers.Location = new Uri("https://outro.exemplo/");
            }

            return response;
        }
    }

    private readonly FakeBrasilApi _api = new();
    private WebApplicationFactory<Program>? _factory;

    public void Dispose()
    {
        _factory?.Dispose();
        _api.Dispose();
        GC.SuppressFinalize(this);
    }

    private WebApplicationFactory<Program> Factory => _factory ??= Build(_api, circuitFailures: 1000);

    private WebApplicationFactory<Program> Build(FakeBrasilApi api, int circuitFailures) => tenancy.Factory.WithWebHostBuilder(b => b
        .UseSetting("Lookups:CircuitFailures", circuitFailures.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .ConfigureTestServices(s => s.AddHttpClient(PublicDataHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => api)));

    private async Task<HttpClient> ClientAsync(string? seedKey = Group.OwnerSeedKey, WebApplicationFactory<Program>? factory = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "l" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = (factory ?? Factory).CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Resposta da BrasilAPI com o QSA, que não pode chegar ao cache (CA 14).</summary>
    private static string Cnpj(string document, string situation = "ATIVA") => $$"""
        {"cnpj":"{{document}}","razao_social":"ELEVADORES TESTE LTDA","nome_fantasia":"TESTE ELEVADORES","descricao_situacao_cadastral":"{{situation}}",
         "descricao_tipo_de_logradouro":"RUA","logradouro":"DAS FLORES","numero":"120","complemento":"SALA 3","bairro":"CENTRO","cep":"12280000",
         "municipio":"CACAPAVA","uf":"SP","codigo_municipio_ibge":3508504,"ddd_telefone_1":"1236531000","email":"CONTATO@TESTE.COM.BR",
         "qsa":[{"nome_socio":"FULANO DE TAL","cnpj_cpf_do_socio":"***123456**"}],"capital_social":1000}
        """;

    /// <summary>CNPJ válido e único por teste.</summary>
    private static string NewCnpj()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("12345678").Take(12).Select(c => c - '0').ToList();
        int Dv(int[] w) { var r = w.Select((x, i) => x * digits[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        digits.Add(Dv([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digits.Add(Dv([6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digits);
    }
}
