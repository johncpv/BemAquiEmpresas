using BemAqui.BuildingBlocks.Export;
using BemAqui.Finance.Application;
using BemAqui.Finance.Application.Exports;
using BemAqui.Finance.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-05 CA 18: plano de contas com a hierarquia (recuo por nível, inativas só com o filtro) e descontos e cancelamentos com os totais.</summary>
public class RegistryExportsUnitTests
{
    private static AccountDetail Account(string code, string name, bool active = true, bool entries = true) =>
        new(Guid.NewGuid(), code, name, AccountType.Revenue, DreGroup.GrossRevenue, null, entries, active, null);

    [Fact]
    public void Plano_de_contas_sai_com_o_codigo_e_o_recuo_por_nivel_e_sem_as_inativas_por_padrao()
    {
        AccountDetail[] accounts = [Account("1", "Receitas", entries: false), Account("1.1", "Venda de elevadores"), Account("1.1.2", "Peças avulsas"), Account("1.9", "A classificar", active: false)];

        var rows = RegistryExports.AccountRows(accounts, inactive: false);

        rows.Select(r => r.Code).Should().Equal("1", "1.1", "1.1.2");
        RegistryExports.Accounts.Styles(rows).Select(s => s?.Indent ?? 0).Should().Equal(0, 1, 2);
        RegistryExports.Accounts.Styles(rows)[0]!.Bold.Should().BeTrue("o primeiro nível é o grupo");
        rows[0].Entries.Should().Be("Sintética");
        rows[1].Type.Should().Be("Receita");
        rows[1].DreGroup.Should().Be("Receita bruta");
        RegistryExports.AccountRows(accounts, inactive: true).Should().HaveCount(4).And.Contain(r => r.Situation == "Inativa");
        RegistryExports.Accounts.OutputColumns(ExportFormat.Xlsx, _ => true).Single(c => c.Header == "Conta").Indented.Should().BeTrue();
    }

    [Fact]
    public void Descontos_e_cancelamentos_saem_por_data_com_quem_fez_e_os_dois_totais()
    {
        var user = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 10);
        var report = new DiscountsAndCancellations(day.AddDays(-9), day.AddDays(20),
            [new DiscountLine(Guid.NewGuid(), "CR-2026-00002", "Alfa", day, 30m, 300m, 10m, user, DateTimeOffset.UtcNow, true)],
            [new CancellationLine(Guid.NewGuid(), "CR-2026-00001", "Beta", 500m, "Cliente desistiu", user, new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero))],
            30m, 500m);

        var rows = RegistryExports.DiscountRows(report, new Dictionary<Guid, string> { [user] = "Maria" });

        rows.Select(r => r.Title).Should().Equal("CR-2026-00001", "CR-2026-00002", "Total de descontos", "Total cancelado");
        rows[0].Kind.Should().Be("Cancelamento");
        rows[0].Reason.Should().Be("Cliente desistiu");
        rows[0].Date.Should().Be(new DateOnly(2026, 10, 5));
        rows[1].Reason.Should().Be("Acima do limite");
        rows[1].By.Should().Be("Maria");
        rows[^2].Amount.Should().Be(30m);
        rows[^1].Amount.Should().Be(500m);
        rows[^1].IsTotal.Should().BeTrue();
    }
}
