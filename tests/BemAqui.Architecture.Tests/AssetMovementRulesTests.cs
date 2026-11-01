using System.Reflection;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PAT-02: nada mudou (CA 2), destino do lote (CA 4), data do movimento, transições da baixa e da reativação (CA 6 e 8), descrição do título
/// da venda (CA 7) e o movimento imutável: sem setter público, sem construtor público, sem alteração no código e sem rota de edição ou
/// exclusão (CA 3).
/// </summary>
public class AssetMovementRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static string Modules => Path.Combine(Solution.RootDirectory, "src", "Modules");

    [Fact]
    public void Nada_mudou_quando_local_responsavel_e_centro_sao_os_mesmos()
    {
        var (site, employee, center) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var current = new AssetPlacement(site, employee, center);
        AssetMovementRules.Changes(current, new AssetPlacement(site, employee, center)).Should().BeFalse();
        AssetMovementRules.Changes(current, new AssetPlacement(site, null, center)).Should().BeTrue("tirar o responsável é mudança");
        AssetMovementRules.Changes(current, new AssetPlacement(Guid.CreateVersion7(), employee, center)).Should().BeTrue();
    }

    [Fact]
    public void Destino_do_lote_so_troca_o_que_foi_escolhido()
    {
        var current = new AssetPlacement(Guid.CreateVersion7(), Guid.CreateVersion7(), null);
        var newResponsible = Guid.CreateVersion7();
        AssetMovementRules.Merge(current, null, newResponsible, null).Should().Be(current with { ResponsibleEmployeeId = newResponsible });
        AssetMovementRules.Merge(current, null, null, null).Should().Be(current);
    }

    [Theory]
    [InlineData("2026-11-01", null, true)]
    [InlineData("2026-11-02", null, false)]
    [InlineData("2026-10-20", "2026-10-20", true)]
    [InlineData("2026-10-19", "2026-10-20", false)]
    public void Data_ate_hoje_e_nao_antes_do_ultimo_movimento(string date, string? last, bool valid) =>
        AssetMovementRules.IsValidDate(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), Today,
            last is null ? null : DateOnly.Parse(last, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(valid);

    [Fact]
    public void Transferencia_baixa_e_reativacao_guardam_antes_e_depois_e_mudam_a_situacao()
    {
        var (siteA, siteB, ana) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var asset = new Asset { Name = "Torno", SiteId = siteA, ResponsibleEmployeeId = ana, Status = AssetStatus.Maintenance };
        var user = Guid.CreateVersion7();

        var transfer = AssetMovement.Transfer(asset, new AssetPlacement(siteB, null, null), Today, "Reforma", user, At);
        transfer.FromSiteId.Should().Be(siteA);
        transfer.ToSiteId.Should().Be(siteB);
        transfer.FromEmployeeId.Should().Be(ana);
        transfer.ToEmployeeId.Should().BeNull();
        asset.SiteId.Should().Be(siteB);
        asset.ResponsibleEmployeeId.Should().BeNull();

        var disposal = AssetMovement.Disposal(asset, AssetDisposalReason.Scrap, Today, null, 100m, Guid.CreateVersion7(), user, At);
        asset.Status.Should().Be(AssetStatus.Disposed);
        disposal.DisposalValue.Should().BeNull("valor e comprador só na venda");
        disposal.BuyerPartyId.Should().BeNull();
        disposal.ToSiteId.Should().Be(siteB, "o bem fica onde estava");
        FluentActions.Invoking(() => AssetMovement.Transfer(asset, new AssetPlacement(siteA, null, null), Today, "x", user, At)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => AssetMovement.Disposal(asset, AssetDisposalReason.Sale, Today, null, 1m, null, user, At)).Should().Throw<InvalidOperationException>();

        var reactivation = AssetMovement.Reactivation(asset, Today, "Engano", user, At);
        reactivation.Kind.Should().Be(AssetMovementKind.Reactivation);
        asset.Status.Should().Be(AssetStatus.Active);
        FluentActions.Invoking(() => AssetMovement.Reactivation(asset, Today, "De novo", user, At)).Should().Throw<InvalidOperationException>();

        var sale = AssetMovement.Disposal(asset, AssetDisposalReason.Sale, Today, null, 48_500.75m, ana, user, At);
        sale.DisposalValue.Should().Be(48_500.75m);
        sale.BuyerPartyId.Should().Be(ana);
    }

    [Fact]
    public void Descricao_do_titulo_da_venda_leva_etiqueta_e_nome() =>
        AssetMovementRules.SaleDescription("PAT-00007", "Torno CNC").Should().Be("Venda do bem PAT-00007 — Torno CNC");

    [Fact]
    public void Movimento_do_bem_e_imutavel_sem_setter_sem_construtor_e_sem_rota_de_edicao()
    {
        typeof(AssetMovement).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod is { IsPublic: true }).Select(p => p.Name)
            .Should().BeEmpty("nenhum setter público no movimento");
        typeof(AssetMovement).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty("só as fábricas criam o movimento");
        typeof(AssetMovement).GetProperty(nameof(AssetMovement.Reason))!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o motivo é texto livre");

        var sep = Path.DirectorySeparatorChar;
        Directory.GetFiles(Modules, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f) is var text && new[] { "AssetMovements.Remove", "AssetMovements.Update", "AssetMovements.Attach", "AssetMovements.ExecuteUpdate", "AssetMovements.ExecuteDelete", "UPDATE assets.asset_movements", "DELETE FROM assets.asset_movements" }
                .Any(t => text.Contains(t, StringComparison.Ordinal)))
            .Should().BeEmpty("movimento não é alterado nem apagado (CA 3)");

        var endpoints = File.ReadAllText(Path.Combine(Modules, "Assets", "BemAqui.Assets.Endpoints", "AssetsEndpoints.cs"));
        endpoints.Split(';').Where(s => s.Contains("/movements", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(s => !s.Contains("MapPut", StringComparison.Ordinal) && !s.Contains("MapDelete", StringComparison.Ordinal) && !s.Contains("MapPatch", StringComparison.Ordinal));
    }
}
