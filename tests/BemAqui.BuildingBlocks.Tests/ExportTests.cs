using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.SharedKernel;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>EXP-01 (ADR 0045): exportador xlsx e PDF de lista (tipos, anti-fórmula), teto, máscara por coluna e auditoria.</summary>
public class ExportTests
{
    private sealed record Row(string Name, string Cpf, decimal Salary, decimal Hours, DateOnly Day, DateTimeOffset At, decimal Rate);

    private static readonly DateTimeOffset At = new(2026, 11, 2, 15, 30, 0, TimeSpan.Zero);

    private static ExportSpec<Row> Spec() => new ExportSpec<Row>("Employee", "funcionarios", "Funcionários")
        .Text("Nome", r => r.Name)
        .Text("CPF", r => r.Cpf, new ExportSensitivity("hr.view_sensitive"))
        .Money("Salário", r => r.Salary, new ExportSensitivity(["hr.view_sensitive", "finance.approve"]))
        .Quantity("Horas", r => r.Hours)
        .Date("Admissão", r => r.Day)
        .DateTime("Atualizado em", r => r.At).NotInPdf()
        .Percent("Adicional", r => r.Rate);

    private static readonly Row[] Rows =
    [
        new("Ana", "52998224725", 4_321.5m, 12.5m, new DateOnly(2026, 1, 15), At, 12.5m),
        new("=HYPERLINK(\"http://x\")", "11144477735", 1_000m, 0.1234m, new DateOnly(2026, 2, 1), At, 0m),
    ];

    private static ExportTable Table(Func<ExportSensitivity, bool> allowed, ExportFormat format = ExportFormat.Xlsx)
    {
        var spec = Spec();
        return new ExportTable(spec.Title, spec.OutputColumns(format, allowed), spec.Rows(format, Rows, allowed), [new("Situação", "Ativos"), new("Busca", null)], "Fulano", At);
    }

    private static XLWorkbook Open(byte[] content) => new(new MemoryStream(content));

    [Fact]
    public void Planilha_tem_celulas_tipadas_cabecalho_congelado_autofiltro_e_a_aba_de_filtros()
    {
        using var workbook = Open(XlsxTabularExporter.Build(Table(_ => true)));
        var sheet = workbook.Worksheet("Funcionários");
        sheet.Row(1).Cells(1, 7).Select(c => c.GetString()).Should().Equal("Nome", "CPF", "Salário", "Horas", "Admissão", "Atualizado em", "Adicional");
        sheet.Cell(1, 1).Style.Font.Bold.Should().BeTrue();
        sheet.SheetView.SplitRow.Should().Be(1);
        sheet.AutoFilter.IsEnabled.Should().BeTrue();
        sheet.AutoFilter.Range.RangeAddress.ToString().Should().Be("A1:G3");

        sheet.Cell(2, 3).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(2, 3).GetValue<decimal>().Should().Be(4_321.5m);
        sheet.Cell(2, 3).Style.NumberFormat.Format.Should().Be(XlsxTabularExporter.MoneyFormat);
        sheet.Cell(3, 4).GetValue<decimal>().Should().Be(0.1234m);
        sheet.Cell(3, 4).Style.NumberFormat.Format.Should().Be(XlsxTabularExporter.QuantityFormat);
        sheet.Cell(2, 5).GetDateTime().Should().Be(new DateTime(2026, 1, 15));
        sheet.Cell(2, 5).Style.NumberFormat.Format.Should().Be(XlsxTabularExporter.DateFormat);
        sheet.Cell(2, 6).GetDateTime().Should().Be(new DateTime(2026, 11, 2, 12, 30, 0), "data e hora no horário de São Paulo");
        sheet.Cell(2, 7).GetValue<decimal>().Should().Be(0.125m, "percentual em pontos vira fração com o formato %");
        sheet.Cell(2, 7).Style.NumberFormat.Format.Should().Be(XlsxTabularExporter.PercentFormat);
        sheet.Column(3).Width.Should().BeGreaterThanOrEqualTo(16);

        var filters = workbook.Worksheet("Filtros");
        filters.Cell(2, 2).GetString().Should().Be("Fulano");
        filters.Cell(3, 2).GetDateTime().Should().Be(new DateTime(2026, 11, 2, 12, 30, 0));
        filters.Cell(4, 2).GetValue<int>().Should().Be(2);
        filters.Cell(5, 1).GetString().Should().Be("Situação");
        filters.Cell(6, 2).GetString().Should().Be("Todos", "filtro vazio é Todos, como na tela");
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+55 11 9999")]
    [InlineData("-2")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tcmd")]
    public void Texto_que_parece_formula_fica_texto_com_o_prefixo_e_o_mesmo_valor(string value)
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("x").Cell(1, 1);
        XlsxTabularExporter.Write(cell, ExportColumnType.Text, value);
        using var reopened = Open(Save(workbook));
        var read = reopened.Worksheet("x").Cell(1, 1);
        read.HasFormula.Should().BeFalse();
        read.DataType.Should().Be(XLDataType.Text);
        read.GetString().Should().Be(value);
        read.Style.IncludeQuotePrefix.Should().BeTrue();
    }

    [Fact]
    public void Texto_comum_nao_ganha_o_prefixo()
    {
        using var workbook = Open(XlsxTabularExporter.Build(Table(_ => true)));
        workbook.Worksheet("Funcionários").Cell(2, 1).Style.IncludeQuotePrefix.Should().BeFalse();
        workbook.Worksheet("Funcionários").Cell(3, 1).HasFormula.Should().BeFalse("=HYPERLINK vira texto");
    }

    [Fact]
    public void Coluna_sensivel_sem_a_acao_sai_mascarada_como_texto_e_qualquer_acao_da_lista_libera()
    {
        var spec = Spec();
        bool OnlyFinance(ExportSensitivity s) => s.AnyOf.Contains("finance.approve");
        var columns = spec.OutputColumns(ExportFormat.Xlsx, OnlyFinance);
        var rows = spec.Rows(ExportFormat.Xlsx, Rows, OnlyFinance);
        rows[0][1].Should().Be(ExportSensitivity.DefaultMask, "CPF exige hr.view_sensitive");
        columns[1].Type.Should().Be(ExportColumnType.Text);
        rows[0][2].Should().Be(4_321.5m, "salário libera com hr.view_sensitive ou finance.approve");
        columns[2].Type.Should().Be(ExportColumnType.Money);

        var none = spec.Rows(ExportFormat.Xlsx, Rows, _ => false);
        none.Select(r => r[1]).Should().AllBeEquivalentTo(ExportSensitivity.DefaultMask);
        none.Select(r => r[2]).Should().AllBeEquivalentTo(ExportSensitivity.DefaultMask);
        none[0][0].Should().Be("Ana", "coluna comum não muda");
        spec.Sensitivities.Should().HaveCount(2);
    }

    [Fact]
    public async Task PDF_leva_so_as_colunas_marcadas_e_paisagem_com_mais_de_seis()
    {
        var spec = Spec();
        spec.OutputColumns(ExportFormat.Pdf, _ => true).Select(c => c.Header).Should().NotContain("Atualizado em").And.HaveCount(6);
        var text = PdfFrameTests.Text(await new QuestPdfRenderer().RenderAsync(new TabularPdfDocument(PdfFrameTests.Frame, Table(_ => false, ExportFormat.Pdf))), out var landscape);
        landscape.Should().BeFalse("6 colunas cabem em retrato");
        text.Should().Contain("Funcionários").And.Contain("Filtros:").And.Contain("Situação:Ativos").And.Contain("2linhas").And.Contain("12,5").And.Contain("15/01/2026");
        text.Should().NotContain("52998224725", "CPF mascarado também no PDF").And.NotContain("4.321,50", "salário mascarado").And.Contain(ExportSensitivity.DefaultMask);

        var wide = new ExportTable("Larga", Enumerable.Range(1, 7).Select(i => new ExportColumn("C" + i, ExportColumnType.Text)).ToList(), [], [], "Fulano", At);
        var wideText = PdfFrameTests.Text(await new QuestPdfRenderer().RenderAsync(new TabularPdfDocument(PdfFrameTests.Frame, wide)), out landscape);
        landscape.Should().BeTrue();
        wideText.Should().Contain("Nenhumalinhaparaosfiltros.").And.Contain("Filtros:nenhum");
    }

    [Theory]
    [InlineData("xlsx", ExportFormat.Xlsx)]
    [InlineData(" PDF ", ExportFormat.Pdf)]
    [InlineData("csv", null)]
    [InlineData(null, null)]
    public void Formato_aceita_so_xlsx_e_pdf(string? value, ExportFormat? expected) => ListExportService.ParseFormat(value).Should().Be(expected);

    [Fact]
    public void Nome_do_arquivo_e_teto_padrao()
    {
        ListExportService.FileName("contas-a-pagar", new DateOnly(2026, 11, 2), "xlsx").Should().Be("contas-a-pagar-2026-11-02.xlsx");
        var options = new ExportOptions();
        options.MaxRows(ExportFormat.Xlsx).Should().Be(10_000);
        options.MaxRows(ExportFormat.Pdf).Should().Be(2_000);
        ListExportService.TooLargeMessage(12_345, 10_000, ExportFormat.Xlsx).Should().Be(
            "A lista tem 12.345 linhas e o limite para Excel é 10.000. Refine os filtros (período, situação ou busca) e exporte de novo.");
    }

    [Fact]
    public async Task Acima_do_teto_recusa_sem_gerar_nem_auditar_e_abaixo_gera_audita_e_mascara()
    {
        var audit = new FakeAudit();
        var service = Service(audit, new ExportOptions { MaxXlsxRows = 1 }, granted: []);
        var tooLarge = await service.BuildAsync("xlsx", Spec(), [], (max, _) => Task.FromResult(new ExportRows<Row>(Rows.Length, Rows.Take(max).ToList())));
        tooLarge.IsFailure.Should().BeTrue();
        tooLarge.Error!.Code.Should().Be(ListExportService.TooLargeCode);
        tooLarge.Error.Data!["total"].Should().Be(2);
        tooLarge.Error.Data["limit"].Should().Be(1);
        audit.Exports.Should().BeEmpty();

        var invalid = await service.BuildAsync("csv", Spec(), [], (_, _) => Task.FromResult(new ExportRows<Row>(0, [])));
        invalid.Error!.Code.Should().Be(ListExportService.InvalidFormatCode);

        var ok = await Service(audit, new ExportOptions(), granted: []).BuildAsync("xlsx", Spec(), [new("Situação", "Ativos"), new("Busca", null)],
            (max, _) => Task.FromResult(new ExportRows<Row>(Rows.Length, Rows)));
        ok.IsSuccess.Should().BeTrue();
        ok.Value.FileName.Should().MatchRegex(@"^funcionarios-\d{4}-\d{2}-\d{2}\.xlsx$");
        ok.Value.ContentType.Should().Be(XlsxTabularExporter.XlsxContentType);
        using var workbook = Open(ok.Value.Content);
        workbook.Worksheet("Funcionários").Cell(2, 2).GetString().Should().Be(ExportSensitivity.DefaultMask);
        var entry = audit.Exports.Should().ContainSingle().Subject;
        entry.Should().Be(("Employee", "list", "xlsx", 2));
        audit.Filters.Should().ContainSingle().Which.Should().Equal(new Dictionary<string, string?> { ["Situação"] = "Ativos" });
    }

    [Fact]
    public async Task Consulta_que_recusa_abas_a_mais_notas_e_PDF_proprio_da_tela()
    {
        var audit = new FakeAudit();
        var service = Service(audit, new ExportOptions(), granted: ["hr.view_sensitive"]);
        var refused = await service.BuildAsync<Row>("xlsx", (_, _) => Task.FromResult<Result<ExportData<Row>>>(Error.NotFound("x", "Conta não encontrada.")));
        refused.Error!.Code.Should().Be("x");
        audit.Exports.Should().BeEmpty();

        var extra = new ExportSpec<Row>("Employee", "funcionarios", "Extra").Text("Nome", r => r.Name);
        Func<int, CancellationToken, Task<Result<ExportData<Row>>>> load = (_, _) => Task.FromResult<Result<ExportData<Row>>>(new ExportData<Row>(Spec(), [], new ExportRows<Row>(2, Rows))
        {
            Decorate = t => t with { SheetName = "Principal", Sheets = [ExportSheet.From("Extra", extra, Rows)], Notes = ["Nota da tela"] },
            Document = (frame, t) => new TabularPdfDocument(frame, t with { Title = "Documento próprio" }),
        });

        var xlsx = await service.BuildAsync("xlsx", load);
        using var workbook = Open(xlsx.Value.Content);
        workbook.Worksheets.Select(w => w.Name).Should().Equal("Principal", "Extra", "Filtros");
        workbook.Worksheet("Extra").Cell(3, 1).GetString().Should().StartWith("=HYPERLINK");
        workbook.Worksheet("Filtros").Column(2).CellsUsed().Select(c => c.GetString()).Should().Contain("Nota da tela");

        var pdf = await service.BuildAsync("pdf", load);
        var text = PdfFrameTests.Text(pdf.Value.Content, out _);
        text.Should().Contain("Documentopróprio").And.Contain("Notadatela");
        audit.Exports.Should().HaveCount(2);
    }

    [Fact]
    public async Task Linha_com_destaque_sai_em_negrito_com_recuo_e_agrupada()
    {
        var spec = new ExportSpec<Row>("Employee", "funcionarios", "Funcionários").Text("Nome", r => r.Name).Indented().Money("Salário", r => -r.Salary)
            .Style(r => r.Name == "Ana" ? new ExportRowStyle(2, true, true) : null);
        var table = new ExportTable(spec.Title, spec.OutputColumns(ExportFormat.Xlsx, _ => true), spec.Rows(ExportFormat.Xlsx, Rows, _ => true), [], "Fulano", At) { RowStyles = spec.Styles(Rows) };
        using var workbook = Open(XlsxTabularExporter.Build(table));
        var sheet = workbook.Worksheet("Funcionários");
        sheet.Cell(2, 1).Style.Alignment.Indent.Should().Be(2);
        sheet.Cell(2, 2).Style.Alignment.Indent.Should().Be(0, "só a coluna marcada recua");
        sheet.Cell(2, 2).Style.Font.Bold.Should().BeTrue();
        sheet.Cell(2, 2).Style.Font.FontColor.Should().Be(XLColor.FromHtml(PdfTheme.CritInk));
        sheet.Row(2).OutlineLevel.Should().Be(2);
        sheet.Cell(3, 1).Style.Font.Bold.Should().BeFalse();

        var text = PdfFrameTests.Text(await new QuestPdfRenderer().RenderAsync(new TabularPdfDocument(PdfFrameTests.Frame, table)), out _);
        text.Should().Contain("Ana");
    }

    private static ListExportService Service(FakeAudit audit, ExportOptions options, string[] granted)
    {
        var frames = new PdfFrameFactory(new FakeCompany(), new FakeStorage(), new FakeUser(), new FakeDirectory(), TimeProvider.System);
        return new ListExportService([new XlsxTabularExporter(), new PdfTabularExporter(new QuestPdfRenderer(), frames)], new FakePermissions(granted), audit, frames,
            Options.Create(options), TimeProvider.System, new QuestPdfRenderer());
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private sealed class FakePermissions(string[] granted) : IPermissionChecker
    {
        public ValueTask<bool> HasPermissionAsync(string module, string action, CancellationToken cancellationToken = default) => ValueTask.FromResult(granted.Contains(module + "." + action));
    }

    internal sealed class FakeAudit : IAuditWriter
    {
        public List<(string Entity, string Id, string Format, int Rows)> Exports { get; } = [];

        public List<IReadOnlyDictionary<string, string?>> Filters { get; } = [];

        public Task RecordExportAsync(string entity, string entityId, string format, int rows, IReadOnlyDictionary<string, string?> filters, CancellationToken cancellationToken = default)
        {
            Exports.Add((entity, entityId, format, rows));
            Filters.Add(filters);
            return Task.CompletedTask;
        }

        public Task RecordExportAsync(string entity, string entityId, string format, int rows, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordReadAsync(string entity, string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordReadAsync(string entity, string entityId, string reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordAttachmentReadAsync(string ownerType, Guid ownerId, Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordExportsAsync(string entity, IReadOnlyCollection<string> entityIds, string format, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordImportAsync(string entity, string entityId, string format, int rows, int records, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class FakeCompany : IPdfCompanySource
    {
        public Task<PdfCompanyInfo> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PdfCompanyInfo("Konebras Elevadores Ltda", "Konebras", "47.225.005/0001-86", "Rua A, 1", "(12) 3333-4444", null, true, null));
    }

    internal sealed class FakeStorage : IFileStorage
    {
        public Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SaveAsync(Stream content, string prefix, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();

        public string? IpAddress => null;
    }

    internal sealed class FakeDirectory : IUserDirectory
    {
        public Task<string?> DisplayNameAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Fulano");

        public Task<IReadOnlyList<Guid>> OwnerIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ActiveUserIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> UsersWithPermissionAsync(string permission, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid?> OwnerGroupIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> GroupNameAsync(Guid groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> EmailOfAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
