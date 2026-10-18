using BemAqui.Migrations.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BemAqui.Integration.Tests.Importing;

public partial class ImportTests
{
    private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + shift;
    }

    [Fact]
    public async Task Relatorio_em_arquivo_e_staging_expurgada_mantendo_chaves_e_totais()
    {
        var row = NextRow();
        var source = new FakeSnapshot();
        source.Societe.Add(FakeSnapshot.Company(row, "Kappa Ltda", NewCnpj(), "contato@kappa.com.br"));
        var result = await RunAsync(source);
        var directory = Path.Combine(Path.GetTempPath(), "bqe-relatorio-" + Guid.NewGuid().ToString("N"));
        var files = await ImportReportWriter.WriteFilesAsync(directory, result.Report!, result.Items!);

        var csv = await File.ReadAllTextAsync(files[1]);
        csv.Should().Contain("importado").And.NotContain("contato@kappa", "nenhum dado pessoal completo no relatório (CA 18)");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(files[0]).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(files[1]).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IMigrationDb>();
        var options = Options.Create(new MigrationOptions { ReportDirectory = directory });
        (await new StagingService(db, options, new ShiftedClock(TimeSpan.FromDays(29))).ExpiredAsync()).Should().NotContain(r => r.Id == result.Report!.RunId);
        (await new StagingService(db, options, new ShiftedClock(TimeSpan.FromDays(31))).ExpiredAsync()).Should().Contain(r => r.Id == result.Report!.RunId, "aos 30 dias o alerta dispara (CA 22)");

        var purged = await new StagingService(db, options, TimeProvider.System).PurgeAsync(accepted: true);

        purged.Files.Should().BeGreaterThanOrEqualTo(2);
        files.Should().OnlyContain(f => !File.Exists(f));
        (await db.ImportItems.CountAsync()).Should().Be(0, "a staging é apagada");
        (await db.ExternalRefs.CountAsync(r => r.SourceId == row.ToString(System.Globalization.CultureInfo.InvariantCulture))).Should().Be(1, "as chaves externas ficam para a reexecução");
        var run = await db.ImportRuns.AsNoTracking().SingleAsync(r => r.Id == result.Report!.RunId);
        run.PurgedAt.Should().NotBeNull();
        run.AcceptedAt.Should().NotBeNull();
        run.Summary.Should().Contain("\"imported\": 1", "os totais ficam");
        (await new StagingService(db, options, new ShiftedClock(TimeSpan.FromDays(31))).ExpiredAsync()).Should().BeEmpty();
    }
}
