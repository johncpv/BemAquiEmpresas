using System.Buffers.Binary;
using System.Text;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Attachments;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>PAT-01a: sugestões do centro (CA 3), regra de exclusão (CA 12), referências do módulo e o passo sem metadados da imagem (CA 8).</summary>
public class AssetRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Assets_so_referencia_os_Contracts_do_RH_da_Producao_dos_Terceiros_do_Fiscal_e_do_Financeiro()
    {
        // PAT-03b: o título a pagar do prestador da manutenção sai pelo contrato do Financeiro.
        var others = Solution.Projects.Where(p => p.Module == "Assets")
            .SelectMany(p => p.ProjectReferences.Select(Solution.Classify))
            .Where(t => t.Module is not null && t.Module != "Assets")
            .Select(t => $"BemAqui.{t.Module}.{t.Layer}")
            .Distinct()
            .ToList();
        others.Should().BeEquivalentTo(["BemAqui.HR.Contracts", "BemAqui.Production.Contracts", "BemAqui.Parties.Contracts", "BemAqui.Fiscal.Contracts", "BemAqui.Finance.Contracts"]);
    }

    [Fact]
    public void Sugestao_traz_o_local_do_bem_mais_recente_e_o_responsavel_mais_comum_entre_os_ativos()
    {
        var (siteA, siteB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (ana, bruno, gone) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var assets = new[]
        {
            new CenterAsset(T0, siteA, ana),
            new CenterAsset(T0.AddDays(1), siteA, ana),
            new CenterAsset(T0.AddDays(2), siteB, bruno),
            new CenterAsset(T0.AddDays(3), null, gone),
            new CenterAsset(T0.AddDays(4), null, gone),
            new CenterAsset(T0.AddDays(5), null, gone),
        };

        var suggestion = AssetRules.Suggest(assets, new HashSet<Guid> { ana, bruno });

        suggestion.SiteId.Should().Be(siteB, "o bem mais recente com local");
        suggestion.ResponsibleEmployeeId.Should().Be(ana, "o mais comum entre os ativos (o desligado não conta)");
    }

    [Fact]
    public void Sugestao_no_empate_fica_com_o_responsavel_do_bem_mais_recente_e_sem_bens_nao_sugere()
    {
        var (ana, bruno) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        AssetRules.Suggest([new CenterAsset(T0, null, ana), new CenterAsset(T0.AddHours(1), null, bruno)], new HashSet<Guid> { ana, bruno })
            .ResponsibleEmployeeId.Should().Be(bruno);
        AssetRules.Suggest([], new HashSet<Guid>()).Should().Be(new AssetSuggestion(null, null));
    }

    [Theory]
    [InlineData(AssetStatus.Active, null, true)]
    [InlineData(AssetStatus.Active, "movimentações", false)]
    [InlineData(AssetStatus.Maintenance, null, false)]
    [InlineData(AssetStatus.Disposed, null, false)]
    public void So_exclui_o_bem_ativo_sem_historico(AssetStatus status, string? usage, bool allowed) =>
        AssetRules.CanDelete(status, [null, usage]).Should().Be(allowed);

    [Fact]
    public void Serie_compara_marca_modelo_e_serie_sem_caixa_e_o_like_nao_aceita_curinga()
    {
        AssetRules.SerialKey(" Romi ", "gl 240", " ab-1 ").Should().Be(AssetRules.SerialKey("ROMI", "GL 240", "AB-1"));
        AssetRules.SerialKey(null, "GL", "1").Should().NotBe(AssetRules.SerialKey("Romi", "GL", "1"));
        AssetRules.LikeExact(@"50%_a\b").Should().Be(@"50\%\_a\\b");
        AssetRules.IsValidValue(0m).Should().BeTrue();
        AssetRules.IsValidValue(1.555m).Should().BeFalse();
        AssetRules.IsValidValue(-1m).Should().BeFalse();
        AssetRules.HasMaintenancePlan(AssetType.Vehicle).Should().BeTrue();
        AssetRules.HasMaintenancePlan(AssetType.Furniture).Should().BeFalse();
    }

    [Fact]
    public void Asset_PNG_sai_sem_eXIf_e_textos_e_mantem_a_imagem()
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        png.AddRange(Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]));
        png.AddRange(Chunk("eXIf", Encoding.ASCII.GetBytes("MM GPS -23.55")));
        png.AddRange(Chunk("tEXt", Encoding.ASCII.GetBytes("Location\0Sao Paulo")));
        png.AddRange(Chunk("IDAT", [1, 2, 3]));
        png.AddRange(Chunk("IEND", []));

        var clean = ImageMetadata.Strip(png.ToArray(), "image/png")!;

        var text = Encoding.Latin1.GetString(clean);
        text.Should().NotContain("eXIf").And.NotContain("GPS").And.NotContain("Location");
        text.Should().Contain("IHDR").And.Contain("IDAT").And.Contain("IEND");
        clean.Length.Should().Be(8 + 25 + 15 + 12);
    }

    [Fact]
    public void Asset_WebP_sai_sem_EXIF_e_XMP_com_as_marcas_do_VP8X_apagadas()
    {
        var vp8x = new byte[10];
        vp8x[0] = 0x08 | 0x04 | 0x10;
        var body = new List<byte>();
        body.AddRange(RiffChunk("VP8X", vp8x));
        body.AddRange(RiffChunk("VP8 ", [1, 2, 3, 4, 5]));
        body.AddRange(RiffChunk("EXIF", Encoding.ASCII.GetBytes("GPS -23.55")));
        body.AddRange(RiffChunk("XMP ", Encoding.ASCII.GetBytes("<x:xmpmeta/>")));
        var webp = new List<byte>();
        webp.AddRange("RIFF"u8.ToArray());
        webp.AddRange(BitConverter.GetBytes((uint)(4 + body.Count)));
        webp.AddRange("WEBP"u8.ToArray());
        webp.AddRange(body);

        var clean = ImageMetadata.Strip(webp.ToArray(), "image/webp")!;

        var text = Encoding.Latin1.GetString(clean);
        text.Should().NotContain("EXIF").And.NotContain("GPS").And.NotContain("xmpmeta").And.Contain("VP8 ");
        BinaryPrimitives.ReadUInt32LittleEndian(clean.AsSpan(4)).Should().Be((uint)(clean.Length - 8));
        clean[20].Should().Be(0x10, "só a marca de alfa fica");
    }

    [Fact]
    public void Asset_imagem_quebrada_e_recusada_em_vez_de_guardada_com_metadados()
    {
        ImageMetadata.Strip([0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF], "image/jpeg").Should().BeNull();
        ImageMetadata.Strip([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0], "image/png").Should().BeNull();
        ImageMetadata.Strip("%PDF-1.4"u8, "application/pdf").Should().BeNull();
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] RiffChunk(string fourcc, byte[] data)
    {
        var padded = data.Length + (data.Length % 2);
        var chunk = new byte[8 + padded];
        Encoding.ASCII.GetBytes(fourcc).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }
}
