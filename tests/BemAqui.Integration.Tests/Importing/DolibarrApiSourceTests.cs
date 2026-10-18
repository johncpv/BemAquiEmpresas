using System.Net;
using System.Text;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>ADR 0017 adendo 2: origem pela API REST do Dolibarr, só GET, projetando só as colunas da lista fixa.</summary>
public class DolibarrApiSourceTests
{
    private sealed class FakeDolibarr(Dictionary<string, string> responses) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Get, "a importação só lê");
            request.Headers.GetValues("DOLAPIKEY").Should().Equal("chave-teste");
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add(path);
            var key = responses.Keys.FirstOrDefault(k => path.EndsWith(k, StringComparison.Ordinal));
            return Task.FromResult(key is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : responses[key] == "403" ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses[key], Encoding.UTF8, "application/json") });
        }
    }

    private static DolibarrApiSnapshotSource Source(FakeDolibarr handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://dolibarr.teste/api/index.php/") }, "chave-teste", "47.225.005/0001-86", TimeProvider.System);

    private static async Task<List<SourceRow>> ReadAsync(DolibarrApiSnapshotSource source, SnapshotTable table)
    {
        var rows = new List<SourceRow>();
        await foreach (var row in source.ReadAsync(table, TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public async Task Terceiros_vem_com_as_colunas_da_lista_tipadas_e_sem_o_que_nao_migra()
    {
        var handler = new FakeDolibarr(new()
        {
            ["/thirdparties?limit=500&page=0"] = """
                [{"id":"10","name":"Elevadores Ação","idprof1":"47.225.005/0001-86","client":"1","fournisseur":"0","entity":"1",
                  "typent_id":"2","typent_code":"TE_SMALL","date_modification":1760000000,"email":"a@b.com","bank_account":"BR00 IBAN","capital":"1000"},
                 {"id":"9","name":"Beta","idprof1":"","client":"0","fournisseur":"1","entity":"1","typent_id":"2","typent_code":"TE_SMALL","date_modification":""}]
                """,
        });
        await using var source = Source(handler);

        var rows = await ReadAsync(source, SnapshotTables.Societe);

        rows.Select(r => r.Id).Should().Equal("9", "10");
        var a = rows[1];
        a.Text("nom").Should().Be("Elevadores Ação");
        a.Text("siren").Should().Be("47.225.005/0001-86");
        a.WholeNumber("client").Should().Be(1);
        a["tms"].Should().Be(DateTimeOffset.FromUnixTimeSeconds(1760000000).UtcDateTime);
        a.Values.Keys.Should().BeEquivalentTo(SnapshotTables.Societe.Columns.Select(c => c.Name), "só as colunas da lista fixa");
        a.Values.Values.Should().NotContain("BR00 IBAN");
        rows[0].Text("siren").Should().BeNull();
        rows[0]["tms"].Should().BeNull();

        var types = await ReadAsync(source, SnapshotTables.TypeEnt);
        types.Should().ContainSingle().Which.Text("code").Should().Be("TE_SMALL");
        handler.Requests.Count(r => r.Contains("/thirdparties", StringComparison.Ordinal)).Should().Be(1, "o endpoint é lido uma vez por execução");
    }

    [Fact]
    public async Task Pagina_ate_o_fim_lista_vazia_404_e_recusa_403_sem_mostrar_a_chave()
    {
        var page0 = "[" + string.Join(',', Enumerable.Range(1, DolibarrApiSnapshotSource.PageSize).Select(i => $$"""{"id":"{{i}}","ref":"E{{i}}","label":"Depósito {{i}}","statut":"1","entity":"1"}""")) + "]";
        var handler = new FakeDolibarr(new()
        {
            ["/warehouses?limit=500&page=0"] = page0,
            ["/warehouses?limit=500&page=1"] = """[{"id":"501","ref":"E501","label":"Último","statut":"0","entity":"1"}]""",
            ["/bankaccounts?limit=500&page=0"] = "403",
        });
        await using var source = Source(handler);

        (await ReadAsync(source, SnapshotTables.Entrepot)).Should().HaveCount(501);
        (await ReadAsync(source, SnapshotTables.SocPeople)).Should().BeEmpty("404 é lista vazia no Dolibarr");
        (await ReadAsync(source, SnapshotTables.Const)).Should().BeEmpty();
        (await source.CompanyDocumentAsync(TestContext.Current.CancellationToken)).Should().Be("47.225.005/0001-86");

        var refused = async () => await ReadAsync(source, SnapshotTables.BankAccount);
        (await refused.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("/bankaccounts").And.Contain("403").And.NotContain("chave-teste");
    }

    [Fact]
    public async Task Garantia_do_produto_vem_do_extrafield()
    {
        var handler = new FakeDolibarr(new()
        {
            ["/products?limit=500&page=0"] = """[{"id":"7","ref":"ELV-1","label":"Elevador","type":"0","status":"1","status_buy":"0","status_batch":"0","seuil_stock_alerte":"2.5","array_options":{"options_elev_garantia":"12 meses","options_elev_modelo":"X"},"price":"99000","pmp":"50000"}]""",
        });
        await using var source = Source(handler);

        var product = (await ReadAsync(source, SnapshotTables.Product)).Single();
        product["seuil_stock_alerte"].Should().Be(2.5);
        product.Values.Values.Should().NotContain("99000").And.NotContain("50000", "preço e custo não migram");
        (await ReadAsync(source, SnapshotTables.ProductExtrafields)).Single().Text("elev_garantia").Should().Be("12 meses");
    }

    [Fact]
    public async Task Arquivo_de_secret_da_API_incompleto_e_recusado()
    {
        var factory = new ServiceCollection().AddMigrationInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider().GetRequiredService<ISnapshotSourceFactory>();
        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "url=http://inseguro/api/index.php\nkey=x\ncnpj=47225005000186\n", TestContext.Current.CancellationToken);

        var open = () => factory.OpenApiAsync(file, TestContext.Current.CancellationToken);

        (await open.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("https").And.NotContain("key=x");
        await File.WriteAllTextAsync(file, "url=https://dolibarr.teste/api/index.php\nkey=x\ncnpj=47225005000186\n", TestContext.Current.CancellationToken);
        await using var source = await factory.OpenApiAsync(file, TestContext.Current.CancellationToken);
        (await source.CompanyDocumentAsync(TestContext.Current.CancellationToken)).Should().Be("47225005000186");
    }
}
