using BemAqui.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BemAqui.Integration.Tests;

/// <summary>Monta o Data Protection do BemAqui fora do Host, como um ambiente de produção.</summary>
public static class SecretsHarness
{
    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "BemAqui.Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public static ServiceProvider Build(string connectionString, string? certificatePath, bool allowCreation)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Catalog"] = connectionString,
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:CertificatePassword"] = TestCertificates.Password,
                ["DataProtection:AllowKeyRingCreation"] = allowCreation.ToString(),
                ["DataProtection:AutoMigrate"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBemAquiSecrets(configuration, new ProductionEnvironment());
        return services.BuildServiceProvider();
    }

    /// <summary>Roda a verificação de subida (o que o Host faz antes de aceitar requisições).</summary>
    public static async Task StartAsync(ServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(CancellationToken.None);
        }
    }

    public static async Task<ServiceProvider> StartedAsync(string connectionString, string? certificatePath = null, bool allowCreation = true)
    {
        var provider = Build(connectionString, certificatePath ?? TestCertificates.Primary, allowCreation);
        await StartAsync(provider);
        return provider;
    }
}
