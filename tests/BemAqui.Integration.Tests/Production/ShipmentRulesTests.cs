using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Commercial.Application;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-06b, regras sem banco: a série que entra na linha do romaneio (CA 2), o estoque geral que pede outra pessoa e o Proprietário único
/// (CA 6), o que cancela e o que estorna (CA 10 e 11), o produto acabado disponível por projeto (CA 1 e 13), o endereço, a placa, o texto
/// da linha do tempo e o PDF sem valores nem custo (CA 3, extração de texto).
/// </summary>
public class ShipmentRulesTests
{
    private static readonly Guid Project = Guid.CreateVersion7();
    private static readonly Guid Item = Guid.CreateVersion7();
    private static readonly Guid Place = Guid.CreateVersion7();

    private static SerialInfo Serial(string number, Guid? project = null, Guid? item = null, Guid? location = null, SerialState status = SerialState.InStock) =>
        new(Guid.CreateVersion7(), item ?? Item, number, Guid.CreateVersion7(), project, location ?? Place, status, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    private static string? Problem(SerialInfo serial, string? openShipment = null, bool general = false) =>
        ShipmentRules.SerialProblem(new ShipmentSerialCandidate(serial, openShipment), Project, Item, Place, general);

    [Fact]
    public void Serie_do_projeto_no_estoque_e_no_local_entra()
    {
        Problem(Serial("ELV-2026-00001", Project)).Should().BeNull("CA 1: série InStock do projeto, no local da linha");
    }

    [Fact]
    public void Serie_em_outro_romaneio_de_outro_projeto_ou_fora_do_estoque_e_recusada()
    {
        Problem(Serial("ELV-2026-00001", Project), "ROM-2026-0003").Should().Be("A série ELV-2026-00001 já está no romaneio ROM-2026-0003.", "CA 2");
        Problem(Serial("ELV-2026-00002", Guid.CreateVersion7())).Should().Be("A série ELV-2026-00002 é de outro projeto.", "CA 2: outro projeto nunca");
        Problem(Serial("ELV-2026-00002", Guid.CreateVersion7()), general: true).Should().Be("A série ELV-2026-00002 é de outro projeto.", "nem pelo estoque geral");
        Problem(Serial("ELV-2026-00003", Project, status: SerialState.Shipped)).Should().Be("A série ELV-2026-00003 não está no estoque.");
        Problem(Serial("ELV-2026-00004", Project, location: Guid.CreateVersion7())).Should().Be("A série ELV-2026-00004 não está neste local.");
        Problem(Serial("ELV-2026-00005", Project, item: Guid.CreateVersion7())).Should().Be("A série ELV-2026-00005 não é deste item.");
    }

    [Fact]
    public void Serie_sem_projeto_so_entra_pelo_estoque_geral()
    {
        Problem(Serial("PA-2026-00001")).Should().Contain("estoque geral", "CA 2: produto acabado sem projeto pede a opção");
        Problem(Serial("PA-2026-00001"), general: true).Should().BeNull("CA 2: marcada a linha 'Estoque geral'");
        Problem(Serial("ELV-2026-00001", Project), general: true).Should().Contain("é do projeto");
    }

    [Fact]
    public void Estoque_geral_pede_outra_pessoa_salvo_o_Proprietario_unico()
    {
        ShipmentRules.RequiresStockApproval([false, false]).Should().BeFalse("CA 4: só produto do projeto expede direto");
        ShipmentRules.RequiresStockApproval([false, true]).Should().BeTrue("CA 6: uma linha do estoque geral basta");
        var author = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        SoleOwnerRule.NoOtherEligible([author, other], author, [author]).Should().BeFalse("CA 6: há outra pessoa com inventory.approve: o autor recebe 403");
        SoleOwnerRule.NoOtherEligible([author], author, [author]).Should().BeTrue("CA 6: Proprietário único autoaprova com a marca");
        ShipmentRules.OwnBlocked.Should().Be("Quem montou o romaneio não confirma produto do estoque geral.");
        ShipmentRules.AwaitingTitle("ROM-2026-0001").Should().Be("Romaneio ROM-2026-0001 aguardando confirmação: produto do estoque geral.");
    }

    [Fact]
    public void Cancela_rascunho_e_aguardando_e_estorna_so_o_expedido()
    {
        ShipmentRules.CanCancel(ShipmentState.Draft).Should().BeTrue();
        ShipmentRules.CanCancel(ShipmentState.AwaitingStockApproval).Should().BeTrue("CA 10");
        ShipmentRules.CanCancel(ShipmentState.Shipped).Should().BeFalse("o expedido se estorna");
        ShipmentRules.CanReverse(ShipmentState.Shipped).Should().BeTrue("CA 11");
        ShipmentRules.CanReverse(ShipmentState.Draft).Should().BeFalse();
        ShipmentRules.HoldsSerials(ShipmentState.Cancelled).Should().BeFalse("CA 10: séries livres");
        ShipmentRules.HoldsSerials(ShipmentState.Reversed).Should().BeFalse("CA 11: séries de volta ao estoque");
        ShipmentRules.HoldsSerials(ShipmentState.AwaitingStockApproval).Should().BeTrue();
        ShipmentRules.Reason("  ").Should().BeNull();
        ShipmentRules.Reason(new string('x', 501)).Should().BeNull();
        ShipmentRules.Reason(" Cliente adiou ").Should().Be("Cliente adiou");
    }

    [Fact]
    public void Disponivel_por_projeto_e_o_menor_entre_o_do_projeto_o_disponivel_e_o_lote()
    {
        var lot = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var serials = new[] { Serial("ELV-2026-00002", Project), Serial("ELV-2026-00001", Project) };
        var lines = FinishedGoodsStockService.Combine(
            [
                new AttributedStock(Project, Item, Place, null, true, 2m, serials),
                new AttributedStock(Project, other, Place, lot, false, 10m, []),
                new AttributedStock(Project, other, Place, null, false, 0m, []),
            ],
            new Dictionary<(Guid Item, Guid Location), PositionStock> { [(Item, Place)] = new(2m, 0m), [(other, Place)] = new(12m, 4m) },
            new Dictionary<(Guid Lot, Guid Location), decimal> { [(lot, Place)] = 6m },
            new Dictionary<Guid, string> { [lot] = "OS-2026-00007" });
        lines.Should().HaveCount(2, "linha sem nada do projeto fica fora");
        var withSerial = lines.Single(l => l.ItemId == Item);
        withSerial.Shippable.Should().Be(2m);
        withSerial.Serials.Select(s => s.SerialNumber).Should().Equal("ELV-2026-00001", "ELV-2026-00002");
        var byLot = lines.Single(l => l.ItemId == other);
        (byLot.OnHand, byLot.Reserved, byLot.Available).Should().Be((12m, 4m, 8m), "CA 13: físico, reservado e disponível");
        byLot.Shippable.Should().Be(6m, "o lote tem só 6 no local");
        byLot.LotNumber.Should().Be("OS-2026-00007");
    }

    [Fact]
    public void Endereco_placa_e_linha_do_tempo()
    {
        var address = new PartyAddressInfo(Guid.CreateVersion7(), AddressKind.Site, "30100000", "Rua da Obra", "100", "Bloco B", "Centro", "Belo Horizonte", null, "MG");
        ShipmentRules.FormatAddress(address).Should().Be("Rua da Obra, 100 — Bloco B — Centro — Belo Horizonte/MG — CEP 30100-000");
        ShipmentRules.Plate(" abc 1d23 ").Should().Be("ABC1D23");
        ShipmentRules.Plate("   ").Should().BeNull();
        ProjectService.ShipmentText("ROM-2026-0001", 2m, ["ELV-2026-00001", "ELV-2026-00002"]).Should().Be("Romaneio ROM-2026-0001 expedido: 2 unidades (ELV-2026-00001, ELV-2026-00002)");
        ProjectService.ShipmentText("ROM-2026-0002", 1m, []).Should().Be("Romaneio ROM-2026-0002 expedido: 1 unidade");
        ProjectService.ShipmentText("ROM-2026-0003", 7m, Enumerable.Range(1, 7).Select(i => $"S-{i}").ToList()).Should().EndWith("(S-1, S-2, S-3, S-4, S-5 e mais 2)");
    }

    [Fact]
    public async Task PDF_do_romaneio_traz_entrega_itens_series_e_assinaturas_sem_valor_nem_custo()
    {
        var model = new ShipmentPdfModel(
            new ShipmentPdfCompany("Konebras", "Konebras Elevadores Ltda", "12.345.678/0001-90", "Rua A, 1", "31 3333-0000", null), "ROM-2026-0001", new DateOnly(2026, 10, 24),
            "PRJ-2026-0001", "Condomínio Solar", "11.222.333/0001-44", "Rua da Obra, 100 — Centro — Belo Horizonte/MG", "Transportes Rápidos", "ABC1D23", "José da Silva", 3, 850.5m,
            [new ShipmentPdfItem(1, "ELV-0001", "Elevador 6 paradas", 2m, "UN", ["ELV-0001-2026-00001", "ELV-0001-2026-00002"], null)]);
        var bytes = await new QuestPdfRenderer().RenderAsync(new ShipmentPdfDocument(model));
        using var document = PdfDocument.Open(bytes);
        var text = string.Join(" ", document.GetPages().Select(p => p.Text));
        text.Should().Contain("ROM-2026-0001").And.Contain("Condomínio Solar").And.Contain("Rua da Obra, 100").And.Contain("Transportes Rápidos").And.Contain("ELV-0001-2026-00002")
            .And.Contain("Elevador 6 paradas").And.Contain("Recebido por").And.Contain("850,5 kg").And.Contain("não é documento fiscal");
        text.Should().NotContainAny(["R$", "Custo", "Valor", "Preço", "Total"], "CA 3: o romaneio não tem valor nem custo");
        typeof(ShipmentPdfModel).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Price", StringComparison.Ordinal) || n.Contains("Amount", StringComparison.Ordinal));
        document.GetPages().SelectMany(p => p.Letters).Select(l => l.FontName).Distinct().Should().OnlyContain(f => f.Contains("Archivo"), "ADR 0022: só a Archivo embutida");
    }
}
