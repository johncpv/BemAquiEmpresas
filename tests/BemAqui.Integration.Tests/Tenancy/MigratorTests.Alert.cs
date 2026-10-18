using BemAqui.BuildingBlocks.Observability;
using BemAqui.Host.Security;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-10: alerta do backup pela CLI (CA 14) e segredos lidos de arquivos (Docker secrets).</summary>
public partial class MigratorTests
{
    [Fact]
    public async Task Alert_raise_registra_o_alerta_do_backup_e_recusa_tipo_desconhecido()
    {
        var output = new StringWriter();
        var summary = "Backup falhou " + Guid.NewGuid().ToString("N")[..6];

        (await RunCliAsync(output, "alert", "raise", "--kind", "backup_failed", "--summary", summary, "--details", "pg_dump: conexão recusada")).Should().Be(0, output.ToString());
        (await RunCliAsync(new StringWriter(), "alert", "raise", "--kind", "qualquer", "--summary", "x")).Should().Be(1);
        (await RunCliAsync(new StringWriter(), "alert", "raise", "--kind", "backup_failed")).Should().Be(1, "sem resumo");

        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().PlatformAlerts.CountAsync(a => a.Key.StartsWith(AlertKinds.BackupFailed))).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Segredo_em_arquivo_vira_configuracao_e_arquivo_sem_separador_e_ignorado()
    {
        var dir = Directory.CreateTempSubdirectory("bq-secrets-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "ConnectionStrings__Catalog"), "Host=db;Password=segredo\n");
            File.WriteAllText(Path.Combine(dir.FullName, "dp-cert.pfx"), "binario");

            var config = new ConfigurationBuilder().AddSecretFiles(dir.FullName).Build();

            config["ConnectionStrings:Catalog"].Should().Be("Host=db;Password=segredo");
            config.AsEnumerable().Select(kv => kv.Key).Should().NotContain(k => k.Contains("pfx", StringComparison.Ordinal));
            new ConfigurationBuilder().AddSecretFiles(Path.Combine(dir.FullName, "nao-existe")).Build().AsEnumerable().Should().BeEmpty();
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
