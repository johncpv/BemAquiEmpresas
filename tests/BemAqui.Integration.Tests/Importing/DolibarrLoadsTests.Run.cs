using BemAqui.Finance.Contracts;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

public partial class DolibarrLoadsTests
{
    [Fact]
    public async Task Carga_completa_traz_terceiros_contatos_condicoes_e_contas_e_a_reexecucao_nao_muda_nada()
    {
        await using (var scope = await ScopeAsync())
        {
            var manual = await scope.ServiceProvider.GetRequiredService<PartyService>().CreateAsync(new CreatePartyRequest(PartyKind.PJ, CnpjD, "Delta cadastrado à mão", null, [PartyRoleKind.Customer]));
            manual.IsSuccess.Should().BeTrue();
        }

        var first = await RunAsync(Fixture());

        first.ExitCode.Should().Be(0, first.Message);
        var totals = first.Report!.Entities.ToDictionary(e => e.Entity);
        totals["payment-terms"].Should().BeEquivalentTo(new { Read = 3, Imported = 2, Linked = 1, Warnings = 1 }, "30 dias liga ao seed; fim do mês avisa (CA 13)");
        totals["bank-accounts"].Should().BeEquivalentTo(new { Read = 3, Imported = 2, Errors = 1 }, "conta de banco sem código vai para erro (CA 15)");
        totals["parties"].Should().BeEquivalentTo(new { Read = 8, Imported = 4, Merged = 1, Linked = 1, Skipped = 1, Errors = 1 });
        totals["contacts"].Should().BeEquivalentTo(new { Read = 3, Imported = 1, Skipped = 2 });
        first.Report.Entities.Should().OnlyContain(e => e.Total == e.Read, "a soma fecha com o total lido (CA 19)");
        first.Items!.Single(i => i.SourceTable == "llx_bank_account" && i.SourceId == "1").Message.Should().Be("Conta BB sem código de banco: informe no de-para.");
        first.Items!.Single(i => i.SourceTable == "llx_societe" && i.SourceId == "4").Message.Should().Be("CNPJ inválido no Dolibarr (id 4, Documento errado).");

        await AssertPartiesAsync();
        await AssertFinanceAsync();

        var second = await RunAsync(Fixture());
        second.Report!.Entities.Should().OnlyContain(e => e.Imported == 0 && e.Updated == 0 && e.Conflicts == 0, "sem mudança no Dolibarr, nada muda (CA 18)");
        second.Report.Entities.Single(e => e.Entity == "parties").Unchanged.Should().Be(6);
        (await QueryAsync(db => db.Parties.CountAsync())).Should().Be(5, "Alfa, Beta, Maria, Gama e Delta (vinculado), sem duplicar");
    }

    private async Task AssertPartiesAsync()
    {
        await using var scope = await ScopeAsync();
        var service = scope.ServiceProvider.GetRequiredService<PartyService>();
        var alfa = (await service.GetAsync((await TargetAsync("llx_societe", "1"))!.Value)).Value;
        alfa.Kind.Should().Be(PartyKind.PJ);
        alfa.ImportSource.Should().Be("dolibarr:llx_societe:1", "CA 17");
        alfa.Roles.Should().Equal(PartyRoleKind.Customer);
        alfa.Phone.Should().Be("1236531000");
        alfa.Notes.Should().Be("Dolibarr — nota privada: Bom & pagador\n\nSem atraso", "HTML vira texto, sem bidi (CA 10)");
        alfa.Addresses.Single().Should().BeEquivalentTo(new { Street = "Rua das Flores", Number = "120", Complement = "Sala 3", ZipCode = "12345678", State = "SP", District = "Não informado", IsDefault = true });
        alfa.DefaultPaymentTerm!.Name.Should().Be("30 dias", "cond_reglement aponta para a condição ligada ao seed (CA 14)");
        alfa.Contacts.Single().Should().BeEquivalentTo(new { Name = "João Silva", Role = "Compras", Email = (string?)null, Phone = "12999998888" }, "e-mail inválido entra vazio (CA 11)");

        (await service.GetAsync((await TargetAsync("llx_societe", "2"))!.Value)).Value.IsActive.Should().BeFalse("encerrado entra inativo (CA 9)");
        var maria = (await service.GetAsync((await TargetAsync("llx_societe", "3"))!.Value)).Value;
        maria.Should().BeEquivalentTo(new { Kind = PartyKind.PF, Document = (string?)null, Notes = "Prospect no Dolibarr" }, "prospect PF sem documento (CA 3 e 5)");

        var gama5 = await TargetAsync("llx_societe", "5");
        (await TargetAsync("llx_societe", "6")).Should().Be(gama5, "mesmo CNPJ vira um terceiro só (CA 6)");
        var gama = (await service.GetAsync(gama5!.Value)).Value;
        gama.LegalName.Should().Be("Gama Elevadores", "os dados vêm do registro mais recente");
        gama.Roles.Should().BeEquivalentTo([PartyRoleKind.Customer, PartyRoleKind.Supplier]);

        var delta = (await service.GetAsync((await TargetAsync("llx_societe", "7"))!.Value)).Value;
        delta.LegalName.Should().Be("Delta cadastrado à mão", "vínculo não sobrescreve (CA 7)");
        delta.ImportSource.Should().Be("dolibarr:llx_societe:7");
    }

    private async Task AssertFinanceAsync()
    {
        var accounts = await QueryAsync(db => db.BankAccounts.AsNoTracking().ToListAsync());
        accounts.Should().HaveCount(2).And.OnlyContain(a => a.OpeningBalance == null && a.OpeningDate == null, "contas entram sem saldo (CA 16)");
        accounts.Single(a => a.Kind == BankAccountKind.Cash).Name.Should().Be("Caixa da fábrica");
        accounts.Single(a => a.Kind == BankAccountKind.Bank).Should().BeEquivalentTo(new { BankCode = "077", Agency = "0001", Account = "12345678-9", ImportSource = "dolibarr:llx_bank_account:3" });
        accounts.Count(a => a.IsDefault).Should().Be(1, "a primeira conta ativa vira padrão");

        var terms = await QueryAsync(db => db.PaymentTerms.AsNoTracking().ToListAsync());
        terms.Where(t => t.SeedKey != null).Should().HaveCount(4, "o seed não é duplicado");
        terms.Single(t => t.SeedKey == "30-dias").ImportSource.Should().Be("dolibarr:llx_c_payment_term:2");
        terms.Single(t => t.Name == "Na entrega").Installments.Should().Equal(new Installment(1, 100m));
    }
}
