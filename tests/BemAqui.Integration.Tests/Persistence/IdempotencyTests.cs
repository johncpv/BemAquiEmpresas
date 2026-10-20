using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Persistence;

/// <summary>FND-09e CA 15 (ADR 0019): comando de teste com Idempotency-Key, reenvio e envios paralelos.</summary>
[Collection(PostgresTestGroup.Name)]
public class IdempotencyTests(PostgresFixture postgres)
{
    public sealed record DocInput(string Name);

    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var connection = await postgres.CreateDatabaseAsync("idem");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<BemAqui.BuildingBlocks.Identity.ICurrentUser>(_ => new FixedUser(Guid.CreateVersion7(), "203.0.113.10"));
        builder.Services.AddBemAquiPersistenceCore();
        builder.Services.AddDbContext<SampleDbContext>((sp, o) => o.UseBemAquiDefaults(connection, sp));
        builder.Services.AddScoped<BemAquiDbContext>(sp => sp.GetRequiredService<SampleDbContext>());
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

    private static HttpRequestMessage Post(Guid? key, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/docs") { Content = JsonContent.Create(new DocInput(name)) };
        if (key is { } k)
        {
            request.Headers.Add(IdempotencyFilter.Header, k.ToString());
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
}
