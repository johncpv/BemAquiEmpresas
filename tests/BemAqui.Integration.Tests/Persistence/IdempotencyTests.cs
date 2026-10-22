using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BemAqui.Integration.Tests.Persistence;

/// <summary>FND-09e CA 15 (ADR 0019): comando de teste com Idempotency-Key, reenvio e envios paralelos; chave por usuário e expurgo (D19).</summary>
[Collection(PostgresTestGroup.Name)]
public class IdempotencyTests(PostgresFixture postgres)
{
    /// <summary>Cabeçalho do teste que troca o usuário da requisição; sem ele, a requisição é do <see cref="DefaultUser"/>.</summary>
    private const string UserHeader = "X-Test-User";

    private static readonly Guid DefaultUser = Guid.CreateVersion7();

    public sealed record DocInput(string Name);

    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var connection = await postgres.CreateDatabaseAsync("idem");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<BemAqui.BuildingBlocks.Identity.ICurrentUser>(sp =>
        {
            var header = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Headers[UserHeader].ToString();
            return new FixedUser(header == "-" ? null : Guid.TryParse(header, out var id) ? id : DefaultUser, "203.0.113.10");
        });
        builder.Services.AddBemAquiPersistenceCore();
        builder.Services.AddDbContext<SampleDbContext>((sp, o) => o.UseBemAquiDefaults(connection, sp));
        builder.Services.AddScoped<BemAquiDbContext>(sp => sp.GetRequiredService<SampleDbContext>());
        var directory = new FakeTenantDirectory();
        directory.Tenants.Add(MessagingHarness.Tenant);
        builder.Services.AddSingleton<ITenantDirectory>(directory);
        builder.Services.AddSingleton<ITenantScopeFactory>(sp => new SameDatabaseScopeFactory(sp));
        var app = builder.Build();
        app.MapPost("/docs", async (DocInput input, SampleDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                return Error.Validation("validation", "Informe o nome.").ToProblem();
            }

            await Task.Delay(150); // segura o primeiro envio para os paralelos chegarem antes
            var doc = new SampleDocument { Name = input.Name };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
            return Results.Created($"/docs/{doc.Id}", new { doc.Id, doc.Name });
        }).RequireIdempotencyKey();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Database.EnsureCreatedAsync();
        }

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static HttpRequestMessage Post(Guid? key, string name, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/docs") { Content = JsonContent.Create(new DocInput(name)) };
        if (key is { } k)
        {
            request.Headers.Add(IdempotencyFilter.Header, k.ToString());
        }

        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        return request;
    }

    private static async Task<int> CountAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Documents.CountAsync();
    }

    [Fact]
    public async Task Sem_cabecalho_recusa_e_o_reenvio_devolve_o_resultado_original_sem_gravar_de_novo()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        var missing = await client.SendAsync(Post(null, "A"));
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("Envie o cabeçalho Idempotency-Key.");

        var key = Guid.NewGuid();
        var first = await client.SendAsync(Post(key, "A"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadAsStringAsync();
        var again = await client.SendAsync(Post(key, "A"));
        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await again.Content.ReadAsStringAsync()).Should().Be(firstBody, "o reenvio devolve a resposta guardada");
        (await CountAsync(app)).Should().Be(1);

        var reused = await client.SendAsync(Post(key, "B"));
        reused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await reused.Content.ReadAsStringAsync()).Should().Contain("idempotency_key_reused");

        var invalidKey = Guid.NewGuid();
        (await client.SendAsync(Post(invalidKey, " "))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(Post(invalidKey, " "))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "recusa não fica guardada: o reenvio passa pela validação de novo");
    }

    [Fact]
    public async Task Envios_paralelos_com_a_mesma_chave_gravam_um_documento()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.SendAsync(Post(key, "Paralelo"))));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.Created);
        (await CountAsync(app)).Should().Be(1, "clique duplo não duplica o documento");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-")]
    public async Task Chave_de_outro_usuario_com_o_mesmo_corpo_nao_devolve_a_resposta_guardada(string? otherIsAnonymous)
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        var key = Guid.NewGuid();
        (await client.SendAsync(Post(key, "Pedido da Ana"))).StatusCode.Should().Be(HttpStatusCode.Created);

        var other = await client.SendAsync(Post(key, "Pedido da Ana", otherIsAnonymous ?? Guid.CreateVersion7().ToString()));

        other.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await other.Content.ReadAsStringAsync();
        body.Should().Contain("idempotency_key_reused").And.NotContain("Pedido da Ana", "a resposta guardada é de outra pessoa");
        (await CountAsync(app)).Should().Be(1);
    }

    [Fact]
    public async Task Expurgo_diario_apaga_registros_com_mais_de_30_dias_e_mantem_os_recentes()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        var recent = Guid.NewGuid();
        (await client.SendAsync(Post(recent, "Recente"))).StatusCode.Should().Be(HttpStatusCode.Created);
        var old = Guid.NewGuid();
        var limit = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO shared.idempotency_records (key, scope, body_hash, status_code, created_at) VALUES ({old}, {"POST /docs -"}, {"x"}, {201}, {now.AddDays(-31)}), ({limit}, {"POST /docs -"}, {"x"}, {201}, {now.AddDays(-29)})");
        }

        var job = new IdempotencyPurgeJob(app.Services.GetRequiredService<ITenantDirectory>(), app.Services.GetRequiredService<ITenantScopeFactory>(), NullLogger<IdempotencyPurgeJob>.Instance);
        await job.ExecuteAsync(MessagingHarness.Tenant.Id, new IdempotencyPurgeArgs(), CancellationToken.None);
        await job.ExecuteAsync(MessagingHarness.Tenant.Id, new IdempotencyPurgeArgs(), CancellationToken.None); // idempotente

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var keys = await scope.ServiceProvider.GetRequiredService<SampleDbContext>().IdempotencyRecords.Select(r => r.Key).ToListAsync();
            keys.Should().BeEquivalentTo([recent, limit]);
        }
    }
}
