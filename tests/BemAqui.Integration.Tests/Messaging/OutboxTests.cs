using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Messaging;

/// <summary>FND-07 CA 11 a 14 e 16: outbox transacional, SKIP LOCKED, backoff/dead-letter, inbox idempotente, tenant suspenso.</summary>
[Collection(PostgresTestGroup.Name)]
public class OutboxTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Evento_fica_no_outbox_so_se_a_transacao_confirma()
    {
        await using var provider = await MessagingHarness.CreateAsync(await postgres.CreateDatabaseAsync("ob_tx"));

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(new CustomerRegistered(Guid.CreateVersion7(), "desfeito"));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        (await MessagingHarness.OutboxAsync(provider)).Should().BeEmpty();

        await MessagingHarness.PublishAsync(provider, new CustomerRegistered(Guid.CreateVersion7(), "confirmado"));
        var outbox = await MessagingHarness.OutboxAsync(provider);
        outbox.Should().ContainSingle();
        outbox[0].Type.Should().Be(typeof(CustomerRegistered).FullName);
        outbox[0].Payload.Should().Contain("confirmado");
    }

    [Fact]
    public async Task Dispatcher_entrega_e_marca_processado()
    {
        await using var provider = await MessagingHarness.CreateAsync(await postgres.CreateDatabaseAsync("ob_ok"));
        var id = Guid.CreateVersion7();
        await MessagingHarness.PublishAsync(provider, new CustomerRegistered(id, "Konebras"));

        await provider.GetRequiredService<OutboxDispatcher>().DispatchOnceAsync(CancellationToken.None);

        (await MessagingHarness.EffectsAsync(provider, id)).Should().Be(1);
        (await MessagingHarness.OutboxAsync(provider)).Single().ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Dois_processadores_em_paralelo_nao_entregam_a_mesma_mensagem()
    {
        await using var provider = await MessagingHarness.CreateAsync(await postgres.CreateDatabaseAsync("ob_skip"));
        await MessagingHarness.PublishAsync(provider, Enumerable.Range(0, 20).Select(_ => new CustomerRegistered(Guid.CreateVersion7(), "x")).ToArray());

        RecordEffectConsumer.DelayMs = 30;
        try
        {
            var counts = await Task.WhenAll(
                Task.Run(() => MessagingHarness.ProcessAsync(provider)),
                Task.Run(() => MessagingHarness.ProcessAsync(provider)));
            while (await MessagingHarness.ProcessAsync(provider) > 0)
            {
            }

            counts.Sum().Should().BeLessThanOrEqualTo(20);
        }
        finally
        {
            RecordEffectConsumer.DelayMs = 0;
        }

        (await MessagingHarness.EffectsAsync(provider)).Should().Be(20, "cada mensagem entregue uma única vez");
        (await MessagingHarness.OutboxAsync(provider)).Should().OnlyContain(m => m.ProcessedAt != null);
    }

    [Fact]
    public async Task Falha_aplica_backoff_e_vai_para_dead_letter_sem_bloquear_as_seguintes()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var provider = await MessagingHarness.CreateAsync(await postgres.CreateDatabaseAsync("ob_dlq"), clock, maxAttempts: 3);
        var okId = Guid.CreateVersion7();
        await MessagingHarness.PublishAsync(provider, new AlwaysFails("teste"), new CustomerRegistered(okId, "segue"));

        await MessagingHarness.ProcessAsync(provider);
        var afterFirst = (await MessagingHarness.OutboxAsync(provider)).Single(m => m.Type.EndsWith(nameof(AlwaysFails), StringComparison.Ordinal));
        afterFirst.Attempts.Should().Be(1);
        afterFirst.NextAttemptAt.Should().BeCloseTo(clock.Now + TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        afterFirst.Error.Should().Contain("falha proposital");
        (await MessagingHarness.EffectsAsync(provider, okId)).Should().Be(1, "a mensagem seguinte não fica bloqueada");

        (await MessagingHarness.ProcessAsync(provider)).Should().Be(0, "antes do backoff não tenta de novo");

        clock.Now += TimeSpan.FromSeconds(6);
        await MessagingHarness.ProcessAsync(provider);
        var afterSecond = (await MessagingHarness.OutboxAsync(provider)).Single(m => m.Id == afterFirst.Id);
        afterSecond.Attempts.Should().Be(2);
        (afterSecond.NextAttemptAt - clock.Now).Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), "backoff crescente");

        clock.Now += TimeSpan.FromSeconds(11);
        await MessagingHarness.ProcessAsync(provider);
        var dead = (await MessagingHarness.OutboxAsync(provider)).Single(m => m.Id == afterFirst.Id);
        dead.Attempts.Should().Be(3);
        dead.DeadLetteredAt.Should().NotBeNull();

        clock.Now += TimeSpan.FromHours(2);
        (await MessagingHarness.ProcessAsync(provider)).Should().Be(0, "dead-letter não volta para a fila");
    }

    [Fact]
    public async Task Reentrega_da_mesma_mensagem_nao_repete_o_efeito()
    {
        await using var provider = await MessagingHarness.CreateAsync(await postgres.CreateDatabaseAsync("ob_inbox"));
        var id = Guid.CreateVersion7();
        await MessagingHarness.PublishAsync(provider, new CustomerRegistered(id, "uma vez"));
        await MessagingHarness.ProcessAsync(provider);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE shared.outbox_messages SET processed_at = NULL");
        }

        await MessagingHarness.ProcessAsync(provider);

        (await MessagingHarness.EffectsAsync(provider, id)).Should().Be(1);
        (await MessagingHarness.OutboxAsync(provider)).Single().ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Tenant_suspenso_nao_tem_outbox_processado()
    {
        await using var provider = await MessagingHarness.CreateAsync(await postgres.CreateDatabaseAsync("ob_susp"));
        var directory = provider.GetRequiredService<FakeTenantDirectory>();
        directory.Tenants[0] = MessagingHarness.Tenant with { Status = TenantStatus.Suspended };
        var id = Guid.CreateVersion7();
        await MessagingHarness.PublishAsync(provider, new CustomerRegistered(id, "suspenso"));

        await provider.GetRequiredService<OutboxDispatcher>().DispatchOnceAsync(CancellationToken.None);
        (await MessagingHarness.EffectsAsync(provider, id)).Should().Be(0);

        directory.Tenants[0] = MessagingHarness.Tenant;
        await provider.GetRequiredService<OutboxDispatcher>().DispatchOnceAsync(CancellationToken.None);
        (await MessagingHarness.EffectsAsync(provider, id)).Should().Be(1);
    }
}
