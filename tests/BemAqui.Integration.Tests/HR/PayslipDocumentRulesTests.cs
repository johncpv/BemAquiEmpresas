using System.Diagnostics;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Domain;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-02b CA 1, 2, 3, 4 e 9 sem banco: CPF mascarado 3+2, nome do arquivo sem CPF nem nome, situação do aviso e o PDF lido de volta pelo
/// texto (valores, bases, marca d'água do rascunho e ausência de CPF completo), só com a Archivo; 50 holerites em menos de 30 s.
/// </summary>
public class PayslipDocumentRulesTests
{
    private const string Cpf = "52998224725";

    [Theory]
    [InlineData("52998224725", "529.***.***-25")]
    [InlineData("529.982.247-25", "529.***.***-25")]
    [InlineData("123", "***.***.***-**")]
    [InlineData(null, "***.***.***-**")]
    public void CPF_mascarado_com_os_3_primeiros_e_os_2_ultimos(string? cpf, string expected) =>
        PayslipDocumentRules.MaskCpf(cpf).Should().Be(expected);

    [Fact]
    public void Nome_do_arquivo_com_matricula_ou_id_curto_sem_CPF_nem_nome()
    {
        var id = Guid.Parse("01929a3c-0000-7000-8000-00000000abcd");
        PayslipDocumentRules.FileName(new DateOnly(2026, 10, 1), "123", id).Should().Be("holerite-2026-10-123.pdf");
        PayslipDocumentRules.FileName(new DateOnly(2026, 10, 1), " ../R 9/", id).Should().Be("holerite-2026-10-R9.pdf", "só letras, dígitos, - e _");
        PayslipDocumentRules.FileName(new DateOnly(2026, 10, 1), null, id).Should().Be("holerite-2026-10-0000abcd.pdf");
        PayslipDocumentRules.FileName(new DateOnly(2026, 10, 1), "  ", id).Should().Be("holerite-2026-10-0000abcd.pdf");
        PayslipDocumentRules.BatchFileName(new DateOnly(2026, 10, 1), PayrollRunType.Monthly).Should().Be("holerites-2026-10-mensal.zip");
        PayslipDocumentRules.MonthYear(new DateOnly(2026, 3, 1)).Should().Be("03/2026");
    }

    [Theory]
    [InlineData(false, true, false, null, false, PayslipNoticeStatus.NotNotified)]
    [InlineData(false, false, false, null, false, PayslipNoticeStatus.NoUser)]
    [InlineData(true, true, false, null, false, PayslipNoticeStatus.Terminated)]
    [InlineData(false, true, true, false, false, PayslipNoticeStatus.Queued)]
    [InlineData(false, true, true, true, false, PayslipNoticeStatus.Sent)]
    [InlineData(false, true, true, false, true, PayslipNoticeStatus.Failed)]
    [InlineData(true, true, true, true, false, PayslipNoticeStatus.Sent)]
    public void Situacao_do_aviso(bool terminated, bool hasUser, bool notified, bool? sent, bool failed, PayslipNoticeStatus expected) =>
        PayslipDocumentRules.Status(terminated, hasUser, notified, sent, failed).Should().Be(expected);

    private static PayslipPdfModel Model(bool draft, int extraLines = 0)
    {
        var lines = new List<PayslipPdfLine>
        {
            new("SAL", "Salário", PayrollItemKind.Earning, "30 dias", 7_654.32m),
            new("HE50", "Horas extras 50%", PayrollItemKind.Earning, "10:00 h", 250.5m),
            new("INSS", "INSS", PayrollItemKind.Deduction, "9%", 711.23m),
            new("FGTS", "FGTS", PayrollItemKind.Information, "8%", 632.39m),
        };
        lines.AddRange(Enumerable.Range(0, extraLines).Select(i => new PayslipPdfLine("AJ" + i, "Ajuste " + i, PayrollItemKind.Earning, null, 1m)));
        return new PayslipPdfModel(new PayslipPdfCompany("Konebras Elevadores Ltda", "12.345.678/0001-90", null),
            new PayslipPdfEmployee("Ana Souza", "123", PayslipDocumentRules.MaskCpf(Cpf), "Soldador", "7243-15", new DateOnly(2020, 1, 2)),
            new DateOnly(2026, 10, 1), PayrollRunType.Monthly, lines, 7_904.82m, 711.23m, 7_193.59m, 7_904.82m, 7_904.82m, 7_193.59m, 632.39m, draft);
    }

    [Fact]
    public async Task PDF_tem_empresa_funcionario_rubricas_bases_e_liquido_sem_CPF_completo_e_so_com_Archivo()
    {
        var bytes = await new QuestPdfRenderer().RenderAsync(new PayslipPdfDocument(Model(draft: false)));
        using var document = PdfDocument.Open(bytes);
        var text = string.Join(" ", document.GetPages().Select(p => p.Text));

        text.Should().Contain("Konebras Elevadores Ltda").And.Contain("12.345.678/0001-90").And.Contain("Ana Souza").And.Contain("123")
            .And.Contain("529.***.***-25").And.Contain("Soldador").And.Contain("7243-15").And.Contain("02/01/2020").And.Contain("10/2026").And.Contain("Mensal");
        text.Should().Contain("Salário").And.Contain("30 dias").And.Contain("7.654,32").And.Contain("250,50").And.Contain("711,23");
        text.Should().Contain("7.904,82").And.Contain("7.193,59").And.Contain("632,39").And.Contain("Base do INSS").And.Contain("Base do FGTS").And.Contain("Base do IRRF").And.Contain("FGTS do mês");
        text.Should().NotContain(Cpf).And.NotContain("529.982.247-25").And.NotContain("RASCUNHO");
        document.GetPages().SelectMany(p => p.Letters).Select(l => l.FontName).Distinct().Should().OnlyContain(f => f.Contains("Archivo"), "ADR 0022: só a Archivo embutida");
    }

    [Fact]
    public async Task Rascunho_leva_a_marca_dagua()
    {
        var bytes = await new QuestPdfRenderer().RenderAsync(new PayslipPdfDocument(Model(draft: true)));
        using var document = PdfDocument.Open(bytes);
        string.Join(" ", document.GetPages().Select(p => p.Text)).Should().Contain("RASCUNHO").And.Contain("folha não fechada");
    }

    [Fact]
    public async Task Gera_50_holerites_em_menos_de_30_segundos()
    {
        var renderer = new QuestPdfRenderer();
        await renderer.RenderAsync(new PayslipPdfDocument(Model(false)));
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            await renderer.RenderAsync(new PayslipPdfDocument(Model(false, extraLines: 20)));
        }

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "ADR 0022, CA 4");
    }
}
