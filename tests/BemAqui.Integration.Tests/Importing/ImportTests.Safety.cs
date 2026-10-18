using BemAqui.Migrations.Application;
using BemAqui.Migrations.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BemAqui.Integration.Tests.Importing;

public partial class ImportTests
{
    [Fact]
    public async Task Snapshot_de_outra_empresa_e_recusado_sem_gravar_nada()
    {
        var source = new FakeSnapshot(siren: "11.222.333/0001-81");
        source.Societe.Add(FakeSnapshot.Company(NextRow(), "Não pode entrar", NewCnpj()));
        var runsBefore = await QueryAsync(db => db.ImportRuns.CountAsync());

        var result = await RunAsync(source);

        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("MAIN_INFO_SIREN").And.Contain("nada foi gravado");
        (await QueryAsync(db => db.ImportRuns.CountAsync())).Should().Be(runsBefore, "a recusa vem antes de qualquer gravação (CA 6)");
        (await RunAsync(new FakeSnapshot(siren: null))).ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Simulacao_desfaz_os_destinos_e_entrega_o_mesmo_relatorio()
    {
        var source = new FakeSnapshot();
        source.Societe.Add(FakeSnapshot.Company(NextRow(), "Delta Simulada Ltda", NewCnpj()));
        source.Societe.Add(FakeSnapshot.Company(NextRow(), "Épsilon Simulada Ltda", NewCnpj()));
        var parties = await QueryAsync(db => db.Parties.CountAsync());
        var refs = await QueryAsync(db => db.ExternalRefs.CountAsync());

        var dry = await RunAsync(source, ImportMode.DryRun);

        dry.ExitCode.Should().Be(0, dry.Message);
        dry.Report!.Entities.Single().Imported.Should().Be(2, "o relatório é o de uma execução real (CA 14)");
        (await QueryAsync(db => db.Parties.CountAsync())).Should().Be(parties);
        (await QueryAsync(db => db.ExternalRefs.CountAsync())).Should().Be(refs);
        (await QueryAsync(db => db.ImportRuns.AsNoTracking().SingleAsync(r => r.Id == dry.Report.RunId))).Should().BeEquivalentTo(new { Mode = ImportMode.DryRun, Status = ImportRunStatus.Succeeded });
        (await QueryAsync(db => db.ImportItems.CountAsync(i => i.RunId == dry.Report.RunId))).Should().Be(2);

        var real = await RunAsync(source);
        real.Report!.Entities.Single().Imported.Should().Be(2, "a simulação não deixou rastro que mude a carga real");
    }

    [Fact]
    public async Task Erro_de_registro_segue_a_carga_e_falha_de_infraestrutura_desfaz_a_entidade()
    {
        var bad = NextRow();
        var source = new FakeSnapshot();
        source.Societe.Add(FakeSnapshot.Company(bad, "CNPJ errado Ltda", "11111111111111"));
        source.Societe.Add(FakeSnapshot.Company(bad + 1, "Zeta Ltda", NewCnpj()));

        var result = await RunAsync(source);

        result.ExitCode.Should().Be(0, "erro de registro não derruba a carga (CA 15)");
        result.Report!.Entities.Single().Should().BeEquivalentTo(new { Errors = 1, Imported = 1 });
        var error = result.Items!.Single(i => i.Outcome == ImportOutcome.Error);
        error.SourceId.Should().Be(bad.ToString(System.Globalization.CultureInfo.InvariantCulture));
        error.Message.Should().NotBeNullOrWhiteSpace();

        var infra = new FakeSnapshot();
        var first = NextRow();
        infra.Societe.Add(FakeSnapshot.Company(first, "Eta Ltda", NewCnpj()));
        infra.Societe.Add(FakeSnapshot.Company(first + 1, "Teta Ltda", NewCnpj()));
        var parties = await QueryAsync(db => db.Parties.CountAsync());

        var failed = await RunAsync(infra, failAfter: 1);

        failed.ExitCode.Should().Be(2);
        failed.Report!.Status.Should().Be(ImportRunStatus.Failed);
        failed.Message.Should().Contain("entidade desfeita");
        (await QueryAsync(db => db.Parties.CountAsync())).Should().Be(parties, "o primeiro terceiro foi desfeito junto com a entidade");
        (await RefAsync(first)).Should().BeNull();
    }

    [Fact]
    public async Task Segunda_importacao_no_mesmo_tenant_e_recusada_pelo_lock()
    {
        // Sem pool: devolvida ao pool, a sessão continuaria segurando o lock para os testes seguintes.
        await using var holder = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(tenancy.Decrypt(tenancy.TenantA)) { Pooling = false }.ConnectionString);
        await holder.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(" + ImportRunner.LockKey + ")", holder))
        {
            await command.ExecuteNonQueryAsync();
        }

        var result = await RunAsync(new FakeSnapshot());

        result.ExitCode.Should().Be(1);
        result.Message.Should().Be("Já existe uma importação em andamento neste tenant.");

        await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(" + ImportRunner.LockKey + ")", holder);
        await unlock.ExecuteNonQueryAsync();
        (await RunAsync(new FakeSnapshot())).Message.Should().NotBe(ImportRunner.Busy, "sem o lock, a importação segue");
    }

    [Fact]
    public async Task Auditoria_mostra_a_importacao_o_operador_e_a_execucao()
    {
        var row = NextRow();
        var source = new FakeSnapshot();
        source.Societe.Add(FakeSnapshot.Company(row, "Iota Auditada Ltda", NewCnpj(), "fiscal@iota.com.br"));

        var result = await RunAsync(source);

        var target = (await RefAsync(row))!.TargetId.ToString();
        var entry = await QueryAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(a => a.Entity == "Party" && a.EntityId == target));
        entry.UserId.Should().BeNull("usuário de sistema");
        entry.Origin.Should().Be("Importação Dolibarr · operador " + Operator + " · execução " + result.Report!.RunId);
        entry.Changes.Should().NotContain("fiscal@iota", "campo [PersonalData] segue mascarado (CA 21)");
    }
}
