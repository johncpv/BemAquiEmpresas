using BemAqui.Migrations.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

public partial class ImportTests
{
    [Fact]
    public async Task Carga_grava_staging_e_chave_externa_e_a_reexecucao_nao_duplica()
    {
        var row = NextRow();
        var source = new FakeSnapshot();
        source.Societe.Add(FakeSnapshot.Company(row, "Elevadores Alfa Ltda", NewCnpj(), "compras@alfa.com.br"));
        source.Societe.Add(FakeSnapshot.Company(row + 1, "Beta Peças Ltda", NewCnpj()));
        source.Societe.Add(FakeSnapshot.Company(row + 2, "Outra empresa do Dolibarr", NewCnpj(), entity: 2));

        var first = await RunAsync(source);

        first.ExitCode.Should().Be(0, first.Message);
        var summary = first.Report!.Entities.Single();
        summary.Should().BeEquivalentTo(new { Read = 3, Imported = 2, Skipped = 1, Unchanged = 0, Errors = 0 });
        summary.Total.Should().Be(summary.Read, "a soma fecha com o total lido (CA 17)");

        var reference = (await RefAsync(row))!;
        reference.Should().BeEquivalentTo(new { Source = "dolibarr", TargetType = "party", LastRunId = first.Report.RunId });
        reference.TargetXmin.Should().BePositive();
        ExternalRef.ImportSource("llx_societe", row.ToString(System.Globalization.CultureInfo.InvariantCulture)).Should().Be("dolibarr:llx_societe:" + row);
        var staged = await QueryAsync(db => db.ImportItems.AsNoTracking().Where(i => i.RunId == first.Report.RunId).ToListAsync());
        staged.Should().HaveCount(3).And.OnlyContain(i => i.Hash.Length == 64 && i.SourceTable == "llx_societe");
        staged.Single(i => i.SourceId == row.ToString(System.Globalization.CultureInfo.InvariantCulture)).Payload
            .Should().Contain("c***@alfa.com.br").And.NotContain("compras@", "e-mail mascarado na staging (CA 18)");

        var partiesBefore = await QueryAsync(db => db.Parties.CountAsync());
        var second = await RunAsync(source);

        second.Report!.Entities.Single().Should().BeEquivalentTo(new { Read = 3, Unchanged = 2, Skipped = 1, Imported = 0 });
        (await QueryAsync(db => db.Parties.CountAsync())).Should().Be(partiesBefore, "nada é duplicado (CA 11)");
        (await RefAsync(row))!.LastRunId.Should().Be(second.Report.RunId);
    }

    [Fact]
    public async Task Origem_alterada_atualiza_e_editado_no_BemAqui_vira_conflito_sem_sobrescrever()
    {
        var row = NextRow();
        var cnpj = NewCnpj();
        var source = new FakeSnapshot();
        source.Societe.Add(FakeSnapshot.Company(row, "Gama Ltda", cnpj));
        (await RunAsync(source)).ExitCode.Should().Be(0);

        source.Societe[0]["nom"] = "Gama Elevadores Ltda";
        var updated = await RunAsync(source);

        updated.Report!.Entities.Single().Updated.Should().Be(1, "mudou no Dolibarr e ninguém mexeu no BemAqui (CA 12)");
        var reference = (await RefAsync(row))!;
        (await QueryAsync(db => db.Parties.AsNoTracking().Where(p => p.Id == reference.TargetId).Select(p => p.LegalName).SingleAsync())).Should().Be("Gama Elevadores Ltda");

        await using (var scope = await ScopeAsync())
        {
            var edited = await scope.ServiceProvider.GetRequiredService<PartyService>().UpdateAsync(reference.TargetId,
                new UpdatePartyRequest(PartyKind.PJ, cnpj, "Gama Editada no BemAqui", null, [PartyRoleKind.Customer], null, null, TaxRegime.NotInformed, "gama@bemaqui.com", null, null));
            edited.IsSuccess.Should().BeTrue();
        }

        source.Societe[0]["nom"] = "Gama Nome Novo no Dolibarr";
        source.Societe[0]["email"] = "novo@gama.com.br";
        var conflict = await RunAsync(source);

        conflict.Report!.Entities.Single().Conflicts.Should().Be(1, "editado no BemAqui depois da carga (CA 13)");
        (await QueryAsync(db => db.Parties.AsNoTracking().Where(p => p.Id == reference.TargetId).Select(p => p.LegalName).SingleAsync())).Should().Be("Gama Editada no BemAqui");
        var item = conflict.Items!.Single();
        item.Outcome.Should().Be(ImportOutcome.Conflict);
        item.Message.Should().Contain("não foi sobrescrito");
        item.Differences.Should().Contain("Gama Nome Novo no Dolibarr").And.Contain("Gama Editada no BemAqui", "o relatório mostra os dois valores")
            .And.Contain("n***@gama.com.br").And.NotContain("novo@gama", "e-mail mascarado nas diferenças");
    }
}
