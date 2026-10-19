using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Loaders;
using BemAqui.Migrations.Application.Snapshot;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01c CA 1 e 4 a 7 e 12, sem banco: de-para de tipo e unidade, lote e série, garantia, NCM e nome de sub-depósito.</summary>
public class CatalogMapperTests
{
    private static SourceRow Row(SnapshotTable table, Dictionary<string, object?> values) =>
        new(table, Convert.ToString(values["rowid"], System.Globalization.CultureInfo.InvariantCulture)!, table.Columns.ToDictionary(c => c.Name, c => values.GetValueOrDefault(c.Name)));

    private static SourceRow Product(int type = 0, int batch = 0, string? ncm = null, string reference = "P-1") =>
        Row(SnapshotTables.Product, new() { ["rowid"] = 7, ["ref"] = reference, ["label"] = "Peça", ["fk_product_type"] = type, ["tobatch"] = batch, ["customcode"] = ncm, ["tobuy"] = 1 });

    private static readonly ImportMapping Types = ImportMapping.Parse("""{ "maps": { "item-types": { "P-1": "materia-prima", "7": "Finished" } } }""").Value;

    [Theory]
    [InlineData(1, 1, ItemType.Service, false, false)]
    [InlineData(0, 1, ItemType.RawMaterial, true, false)]
    [InlineData(0, 2, ItemType.RawMaterial, false, true)]
    public void Tipo_e_lote_ou_serie(int type, int batch, ItemType expected, bool lot, bool serial)
    {
        var mapped = ItemRowMapper.Map(Product(type, batch), Types, null);
        mapped.Should().BeEquivalentTo(new { Type = expected, TracksLot = lot, TracksSerial = serial });
    }

    [Fact]
    public void De_para_pelo_ref_vem_antes_do_id_e_sem_de_para_vira_componente_com_aviso()
    {
        ItemRowMapper.Type(Product(reference: "OUTRO"), Types).Type.Should().Be(ItemType.Finished, "o id 7 está no mapa");
        var (type, warning) = ItemRowMapper.Type(Product(reference: "OUTRO"), ImportMapping.Empty);
        type.Should().Be(ItemType.Component);
        warning.Should().Be("tipo não informado no de-para: entrou como Componente");
    }

    [Fact]
    public void NCM_so_com_8_digitos()
    {
        ItemRowMapper.Map(Product(ncm: "8431.31.10"), Types, null).Ncm.Should().Be("84313110");
        var shortNcm = ItemRowMapper.Map(Product(ncm: "8431"), Types, null);
        shortNcm.Ncm.Should().BeNull();
        shortNcm.Warnings.Should().ContainSingle(w => w.Contains("não tem 8 dígitos", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, null, true, false)]
    [InlineData("24", 24, false, false)]
    [InlineData("um ano", null, true, true)]
    [InlineData("200", null, true, true)]
    public void Garantia_do_extrafield(string? text, int? months, bool useDefault, bool warns)
    {
        var warnings = new List<string>();
        ItemRowMapper.Warranty(text, warnings).Should().Be((months, useDefault));
        (warnings.Count > 0).Should().Be(warns);
    }

    [Fact]
    public void Unidade_pelo_de_para_ou_pelo_mesmo_codigo()
    {
        SourceRow Unit(string code) => Row(SnapshotTables.Units, new() { ["rowid"] = 3, ["code"] = code });
        UnitsLoader.TargetCode(Unit("U"), ImportMapping.Empty).Should().Be("UN");
        UnitsLoader.TargetCode(Unit("kg"), ImportMapping.Empty).Should().Be("KG");
        UnitsLoader.TargetCode(Unit("CX"), ImportMapping.Parse("""{ "maps": { "units": { "CX": "criar" } } }""").Value).Should().Be("criar");
    }

    [Fact]
    public void Codigo_e_nome_do_sub_deposito()
    {
        var parent = Row(SnapshotTables.Entrepot, new() { ["rowid"] = 1, ["ref"] = "ALMOX", ["label"] = "Almoxarifado" });
        var child = Row(SnapshotTables.Entrepot, new() { ["rowid"] = 4, ["ref"] = "Chapa Inox 1,2mm", ["label"] = "Chapa Inox 1,2mm", ["fk_parent"] = 1 });
        LocationsLoader.Code(child).Should().Be("CHAPA-INOX-1-2MM");
        LocationsLoader.Name(child, new Dictionary<string, SourceRow> { ["1"] = parent, ["4"] = child }).Should().Be("Almoxarifado / Chapa Inox 1,2mm");
        LocationsLoader.Code(Row(SnapshotTables.Entrepot, new() { ["rowid"] = 9, ["ref"] = "***" })).Should().Be("DEP-9");
    }
}
