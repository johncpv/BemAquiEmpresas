using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Persistence;
using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Email;

/// <summary>FND-08 CA 1, 2, 3 e 9: fila transacional, job na fila email com tenantId, envio real, 3 tentativas, Skipped.</summary>
[Collection(PostgresTestGroup.Name)]
public class EmailTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private static readonly Dictionary<string, object?> InviteModel = new()
    {
        ["tenant_name"] = "Konebras Elevadores",
        ["link"] = "https://app.konebras.com.br/convite/abc",
        ["expires_at"] = DateTimeOffset.UtcNow.AddDays(3),
    };

    private static async Task<Guid> EnqueueAsync(IServiceProvider provider, string to)
    {
        await using var scope = provider.CreateAsyncScope();
        var id = scope.ServiceProvider.GetRequiredService<IEmailSender>().Enqueue("invite", to, InviteModel, "User", Guid.CreateVersion7());
        await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().SaveChangesAsync();
        return id;
    }

    private static async Task<EmailLog> LogAsync(IServiceProvider provider, Guid id)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().EmailLogs.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private static SendEmailJob Job(IServiceProvider provider) =>
        new(provider.GetRequiredService<ITenantDirectory>(), provider.GetRequiredService<ITenantScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SendEmailJob>.Instance);

    [Fact]
    public async Task Enfileirar_grava_log_e_evento_na_mesma_transacao_e_o_consumer_cria_o_job_com_o_tenant()
    {
        var (provider, jobs, _) = await EmailHarness.CreateAsync(await postgres.CreateDatabaseAsync("mail_q"), mailpit.Container.Hostname, mailpit.SmtpPort);
        await using var disposeProvider = provider;

        var id = await EnqueueAsync(provider, "ana@konebras.com.br");

        (await LogAsync(provider, id)).Status.Should().Be(EmailStatus.Queued);
        await using (var scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().OutboxMessages.SingleAsync()).Type.Should().EndWith(nameof(EmailQueued));
            var holder = scope.ServiceProvider.GetRequiredService<TenantContextHolder>();
            holder.Set(new ResolvedTenant(EmailHarness.Tenant, "WSP", "Konebras", "app.konebras.com.br", "x"));
            await new EmailQueuedConsumer(jobs, holder).HandleAsync(new EmailQueued(id), CancellationToken.None);
        }

        var created = jobs.Created.Single();
        ((EnqueuedState)created.State).Queue.Should().Be(JobQueues.Email);
        created.Job.Args[0].Should().Be(EmailHarness.Tenant.Id);
    }

    [Fact]
    public async Task Job_envia_pelo_SMTP_e_marca_Sent()
    {
        var (provider, _, _) = await EmailHarness.CreateAsync(await postgres.CreateDatabaseAsync("mail_ok"), mailpit.Container.Hostname, mailpit.SmtpPort);
        await using var disposeProvider = provider;
        var to = "envio-" + Guid.CreateVersion7().ToString("N")[..8] + "@konebras.com.br";
        var id = await EnqueueAsync(provider, to);

        await Job(provider).ExecuteAsync(EmailHarness.Tenant.Id, new SendEmailArgs(id), CancellationToken.None);

        var log = await LogAsync(provider, id);
        log.Status.Should().Be(EmailStatus.Sent);
        log.SentAt.Should().NotBeNull();
        using var http = new HttpClient { BaseAddress = mailpit.Api };
        var search = await http.GetFromJsonAsync<JsonElement>("search?query=to:" + to);
        search.GetProperty("messages")[0].GetProperty("Subject").GetString().Should().StartWith("Convite para acessar Konebras Elevadores");
    }

    [Fact]
    public async Task SMTP_fora_do_ar_tenta_3_vezes_e_marca_Failed_com_o_erro()
    {
        var (provider, _, _) = await EmailHarness.CreateAsync(await postgres.CreateDatabaseAsync("mail_fail"), "127.0.0.1", 1);
        await using var disposeProvider = provider;
        var id = await EnqueueAsync(provider, "falha@konebras.com.br");
        var job = Job(provider);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var tentar = () => job.ExecuteAsync(EmailHarness.Tenant.Id, new SendEmailArgs(id), CancellationToken.None);
            await tentar.Should().ThrowAsync<Exception>("o Hangfire tenta de novo (tentativa {0})", attempt);
            (await LogAsync(provider, id)).Status.Should().Be(EmailStatus.Queued);
        }

        await job.ExecuteAsync(EmailHarness.Tenant.Id, new SendEmailArgs(id), CancellationToken.None);

        var log = await LogAsync(provider, id);
        log.Status.Should().Be(EmailStatus.Failed);
        log.Attempts.Should().Be(3);
        log.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Politica_de_retry_e_de_3_tentativas_com_intervalo_crescente()
    {
        var retry = (AutomaticRetryAttribute)Attribute.GetCustomAttribute(typeof(SendEmailJob), typeof(AutomaticRetryAttribute))!;

        retry.Attempts.Should().Be(2, "1ª execução + 2 novas tentativas = 3");
        retry.DelaysInSeconds.Should().Equal(30, 120);
    }

    [Fact]
    public async Task Tenant_suspenso_nao_envia_e_registra_Skipped()
    {
        var (provider, _, directory) = await EmailHarness.CreateAsync(await postgres.CreateDatabaseAsync("mail_susp"), mailpit.Container.Hostname, mailpit.SmtpPort);
        await using var disposeProvider = provider;
        directory.Tenants[0] = EmailHarness.Tenant with { Status = TenantStatus.Suspended };
        var id = await EnqueueAsync(provider, "suspenso@konebras.com.br");

        await Job(provider).ExecuteAsync(EmailHarness.Tenant.Id, new SendEmailArgs(id), CancellationToken.None);

        var log = await LogAsync(provider, id);
        log.Status.Should().Be(EmailStatus.Skipped);
        log.Error.Should().Contain("Suspended");
    }
}
