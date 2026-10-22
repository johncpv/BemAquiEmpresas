using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host;
using BemAqui.Host.Cli;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// O <c>migrate</c> roda os seeders nos tenants existentes com os módulos do CLI, que é uma lista à parte do
/// <c>ModuleRegistry</c> da API. Módulo com seeder fora dela não semeia no deploy (o roteiro de exemplo da ENG-01 faltou assim).
/// </summary>
public class CliModulesTests
{
    [Fact]
    public void Todo_modulo_com_seeder_esta_no_CLI()
    {
        // Só registra serviços; nada conecta. O Platform exige a string do catálogo na composição.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = "Host=localhost;Database=catalog" })
            .Build();
        var withSeeder = ModuleRegistry.All.Where(m =>
        {
            var services = new ServiceCollection();
            m.AddServices(services, configuration);
            return services.Any(d => d.ServiceType == typeof(ITenantSeeder));
        }).Select(m => m.GetType().Name).ToList();

        withSeeder.Should().NotBeEmpty();
        CliCommand.Modules.Select(m => m.GetType().Name).Should().Contain(withSeeder);
    }
}
