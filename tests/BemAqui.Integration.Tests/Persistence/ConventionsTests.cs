using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Persistence;

/// <summary>FND-09e CA 13, 15 e 16 (ADR 0019): concorrência pelo xmin, idempotência por cabeçalho e notificação sem duplicar.</summary>
[Collection(PostgresTestGroup.Name)]
public class ConventionsTests(PostgresFixture postgres)
{
    public sealed record DocInput(string Name);

    [Fact]
    public async Task Duas_gravacoes_sobre_a_mesma_versao_dao_409_concurrency_conflict()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("conc"));
        Guid id;
        uint version;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            var doc = new SampleDocument { Name = "v0" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
            (id, version) = (doc.Id, doc.Version);
        }

        version.Should().NotBe(0u, "a versão é o xmin da linha");
        await using (var first = provider.CreateAsyncScope())
        {
            var db = first.ServiceProvider.GetRequiredService<SampleDbContext>();
            var doc = await db.Documents.SingleAsync(d => d.Id == id);
            doc.Name = "primeira";
            await db.SaveChangesAsync();
        }

        await using var second = provider.CreateAsyncScope();
        var stale = second.ServiceProvider.GetRequiredService<SampleDbContext>();
        var again = await stale.Documents.SingleAsync(d => d.Id == id);
        stale.Entry(again).Property(d => d.Version).OriginalValue = version;
        again.Name = "segunda, com a versão que o cliente abriu";
        var save = () => stale.SaveChangesAsync();
        var exception = (await save.Should().ThrowAsync<DbUpdateConcurrencyException>()).Which;

        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();
        (await new ConcurrencyExceptionHandler().TryHandleAsync(http, exception, default)).Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        body.Should().Contain("concurrency_conflict").And.Contain("O documento de teste mudou desde que você abriu. Recarregue.");
    }

    [Fact]
    public async Task Notificacao_com_a_mesma_chave_grava_uma_vez_mesmo_em_paralelo()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("dedup"));
        var user = Guid.CreateVersion7();
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyOnceAsync(user, "finance.overdue", "3 títulos vencidos", "/app/financeiro/a-receber", "overdue:2026-10-20");
            result.IsSuccess.Should().BeTrue();
        }));
        await using var check = provider.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<SampleDbContext>().Notifications.CountAsync(n => n.UserId == user)).Should().Be(1);
    }
}
