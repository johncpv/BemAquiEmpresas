using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Persistence;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Email;

/// <summary>Mailpit: SMTP real para teste, com API HTTP para ler as mensagens recebidas.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public IContainer Container { get; } = new ContainerBuilder("axllent/mailpit:v1.21")
        .WithPortBinding(1025, assignRandomHostPort: true)
        .WithPortBinding(8025, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info")))
        .Build();

    public int SmtpPort => Container.GetMappedPublicPort(1025);

    public Uri Api => new($"http://{Container.Hostname}:{Container.GetMappedPublicPort(8025)}/api/v1/");

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}

public static class EmailHarness
{
    public static readonly TenantInfo Tenant = new(Guid.CreateVersion7(), "konebras", TenantStatus.Active);

    public static async Task<(ServiceProvider Provider, CapturingJobClient Jobs, FakeTenantDirectory Directory)> CreateAsync(string connection, string smtpHost, int smtpPort)
    {
        var jobs = new CapturingJobClient();
        var directory = new FakeTenantDirectory();
        directory.Tenants.Add(Tenant);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:SmtpHost"] = smtpHost,
            ["Email:SmtpPort"] = smtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Email:Security"] = "None",
            ["Email:Timeout"] = "00:00:05",
        }).Build();

        var provider = await TestPersistence.CreateAsync(connection, configure: s =>
        {
            s.AddBemAquiEmail(configuration);
            s.AddBemAquiMessaging(configuration, r => r.AddEmailEvents(), runDispatcher: false);
            s.AddSingleton<IBackgroundJobClient>(jobs);
            s.AddSingleton<ITenantDirectory>(directory);
            s.AddSingleton<ITenantScopeFactory>(sp => new SameDatabaseScopeFactory(sp));
            s.AddScoped<TenantContextHolder>();
            s.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextHolder>());
        });
        return (provider, jobs, directory);
    }
}
