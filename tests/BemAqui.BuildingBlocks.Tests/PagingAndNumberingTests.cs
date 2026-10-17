using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

public class PagingAndNumberingTests
{
    [Theory]
    [InlineData(0, 0, 1, 20)]
    [InlineData(3, 50, 3, 50)]
    [InlineData(1, 5000, 1, 100)]
    [InlineData(-2, -1, 1, 20)]
    public void PageRequest_normaliza_pagina_e_limita_tamanho(int page, int size, int expectedPage, int expectedSize)
    {
        var normalized = new PageRequest(page, size).Normalize();

        normalized.Page.Should().Be(expectedPage);
        normalized.PageSize.Should().Be(expectedSize);
    }

    [Fact]
    public void Sort_com_menos_e_descendente()
    {
        new PageRequest(Sort: "-createdAt").ParseSort().Should().Be(("createdAt", true));
        new PageRequest(Sort: "name").ParseSort().Should().Be(("name", false));
        new PageRequest().ParseSort().Should().BeNull();
    }

    [Theory]
    [InlineData("ORC", 2026, 1, "ORC-2026-0001")]
    [InlineData("OS", 2026, 42, "OS-2026-00042")]
    [InlineData("CP", 2027, 12345, "CP-2027-12345")]
    [InlineData("PAT", 0, 7, "PAT-00007")]
    public void Formatos_padrao_de_04_fluxos(string docType, int year, long value, string expected) =>
        NumberFormats.Defaults[docType].Format(year, value).Should().Be(expected);
}
