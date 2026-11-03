using System.Text.RegularExpressions;
using BemAqui.Assets.Application;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.HR.Application;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application.Privacy;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using FluentAssertions;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Pdf;

/// <summary>
/// EXP-05 CA 19: regressão por texto dos 11 layouts antigos. Cada um é desenhado com um dataset fixo, o texto extraído (PdfPig, sem espaços) perde
/// só o cabeçalho, a faixa de título, o rodapé e a marca, e o corpo é comparado com o gravado em <c>Fixtures/{documento}.txt</c> antes da
/// migração para o <see cref="PdfDocumentFrame"/> (primeiro commit da EXP-05). Etiqueta e DANFE mantêm o formato próprio: só a marca sai do texto. Para regravar: <c>BQ_UPDATE_PDF_TEXT=1 sh scripts/test-docker.sh --filter-class "*LegacyPdfLayoutsTests"</c>.
/// </summary>
public partial class LegacyPdfLayoutsTests
{
    private const string LegalName = "Konebras Elevadores Ltda";
    private const string TradeName = "Konebras";
    private const string Cnpj = "47.225.005/0001-86";
    private const string Address = "Rua A, 1 · Taubaté/SP";
    private const string Contact = "(12) 3333-4444";

    private static readonly string[] Company = [LegalName, TradeName, "CNPJ " + Cnpj, Cnpj, Address, Contact];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"·?Página\d+de\d+")]
    private static partial Regex Pages();

    private static string Flat(string text) => Spaces().Replace(text, string.Empty);

    private static async Task<string> TextAsync(IDocument document)
    {
        var pdf = await new QuestPdfRenderer().RenderAsync(document);
        using var read = PdfDocument.Open(pdf);
        return Flat(string.Join(" ", read.GetPages().Select(p => p.Text)));
    }

    /// <summary>O corpo: o texto sem os trechos de cabeçalho e rodapé (do maior para o menor, para a razão social sair antes do nome) e sem "· Página x de y" (o rodapé do frame).</summary>
    private static string Body(string text, IEnumerable<string> strip)
    {
        foreach (var part in strip.Select(Flat).Where(p => p.Length > 0).Distinct().OrderByDescending(p => p.Length))
        {
            text = text.Replace(part, string.Empty, StringComparison.Ordinal);
        }

        return Pages().Replace(text, string.Empty);
    }

    private static void Fixture(string name, string body)
    {
        var path = Path.Combine(OpenApiDocumentTests.RepositoryRoot(), "tests", "BemAqui.Integration.Tests", "Pdf", "Fixtures", name + ".txt");
        if (Environment.GetEnvironmentVariable("BQ_UPDATE_PDF_TEXT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, body);
            return;
        }

        File.Exists(path).Should().BeTrue($"o texto do layout antigo fica em {path}");
        body.Should().Be(File.ReadAllText(path), $"o corpo do {name} não pode mudar na migração para o modelo único (EXP-05 CA 19)");
    }

    // ---------- Dataset fixo ----------

    private static readonly DateOnly Day = new(2026, 11, 3);

    private static QuotePdfModel QuoteModel() => new(
        "ORC-2026-0007", 2, Day,
        new QuotePdfCustomer("Condomínio Solar", "12.345.678/0001-90", "Rua B, 20 — Centro — Taubaté/SP", "Maria · maria@solar.com · (12) 9999-0000"),
        [new QuotePdfItem(1, "Elevador social 6 paradas", 1m, "un", 185_000m, 185_000m), new QuotePdfItem(2, "Instalação", 1m, null, 15_000m, 15_000m)],
        200_000m, [new QuotePdfMilestone("Sinal", 30m, 60_000m, "na aprovação"), new QuotePdfMilestone("Entrega", 70m, 140_000m, "em 10/12/2026")],
        new DateOnly(2026, 12, 3), 90, "Prazo sujeito à aprovação do projeto.");

    private static ShipmentPdfModel ShipmentModel() => new(
        "ROM-2026-0003", Day, "PRJ-2026-0001", "Condomínio Solar", "12.345.678/0001-90",
        "Rua B, 20 — Centro — Taubaté/SP", "Transportes Vale", "ABC1D23", "João", 3, 1250.5m,
        [new ShipmentPdfItem(1, "CAB-01", "Cabine 8 pessoas", 1m, "un", ["SN-0001"], null), new ShipmentPdfItem(2, "CAB-02", "Cabo de aço", 120.5m, "m", [], "L-77")]);

    private static IReadOnlyList<PayslipPdfLine> PayslipLines() =>
    [
        new("SAL", "Salário", PayrollItemKind.Earning, "30 dias", 7_654.32m),
        new("HE50", "Horas extras 50%", PayrollItemKind.Earning, "10:00 h", 250.5m),
        new("INSS", "INSS", PayrollItemKind.Deduction, "9%", 711.23m),
        new("FGTS", "FGTS", PayrollItemKind.Information, "8%", 632.39m),
    ];

    private static readonly PayslipPdfEmployee Employee = new("Ana Souza", "123", "529.***.***-25", "Soldador", "7243-15", new DateOnly(2020, 1, 2));

    private static TimesheetTotals Totals() => new(10_560, 10_620, 120, 480, 15, 60, 0, 21, 1, 0, true, 105, 345, new Dictionary<AbsenceReason, int>());

    private static IReadOnlyList<TimesheetPdfDay> Days() =>
    [
        new(new DateOnly(2026, 10, 1), "07:00 11:00 12:00 16:48", 528, 528, 0, 0, 0, "Normal"),
        new(new DateOnly(2026, 10, 2), "07:15 11:00 12:00 16:48", 528, 513, 0, 15, 0, "Atraso"),
        new(new DateOnly(2026, 10, 3), string.Empty, 0, 0, 0, 0, 0, "Folga"),
    ];

    private static IReadOnlyList<TerminationTermLine> TerminationLines() =>
    [
        new("SALDO", "Saldo de salário", "15 dias", PayrollItemKind.Earning, 1_500m),
        new("FERP", "Férias proporcionais", "5/12", PayrollItemKind.Earning, 1_250m),
        new("INSS", "INSS", "9%", PayrollItemKind.Deduction, 247.5m),
        new("FGTSR", "FGTS rescisório", "8%", PayrollItemKind.Information, 220m),
    ];

    private static IReadOnlyList<FiscalSettingsPdfRow> FiscalRows() =>
    [
        new("Empresa", "Regime tributário", "Lucro presumido", "Conferido em 02/11/2026"),
        new("NF-e", "Série", "1", "A confirmar"),
        new("Impostos", "Alíquota de ICMS", "18%", "A confirmar"),
    ];

    private static IReadOnlyList<PersonalDataSection> PersonalSections() =>
    [
        new("Identity", "Conta de acesso",
            [new PersonalDataBlock("Usuário", "identity.users", [new PersonalDataRecord([new("Nome", "Ana Souza"), new("E-mail", "ana@konebras.com.br"), new("Telefone", null)])])]),
        new("HR", "Recursos humanos", [new PersonalDataBlock("Afastamentos", "hr.leaves", [])], [new PersonalDataFile(Guid.Empty, "Employee", Guid.Empty, "contrato.pdf")],
            "Dados de saúde só aparecem para o próprio titular."),
    ];

    private static AssetLabelModel LabelModel() => new(
        [new AssetLabelItem("PAT-00012", "Furadeira de bancada", "https://app.konebras.com.br/b/PAT-00012")], TradeName, new LabelSize(50, 25), AssetLabelLayout.Label,
        new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    private static (OutgoingInvoice Invoice, IReadOnlyList<OutgoingInvoiceItem> Items) Invoice()
    {
        var invoice = OutgoingInvoice.Create(0m, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
        invoice.NatureOfOperation = "Venda de produção do estabelecimento";
        invoice.RecipientName = "Condomínio Solar";
        invoice.RecipientDocument = "12.345.678/0001-90";
        invoice.RecipientStateRegistration = "123456789";
        invoice.RecipientStreet = "Rua B";
        invoice.RecipientNumber = "20";
        invoice.RecipientDistrict = "Centro";
        invoice.RecipientCityName = "Taubaté";
        invoice.RecipientState = "SP";
        invoice.RecipientPostalCode = "12000-000";
        invoice.ApplyTotals(new InvoiceTotals(185_000m, 0m, 0m, 0m, [new TaxTotal("ICMS", 185_000m, 33_300m), new TaxTotal("IPI", 185_000m, 0m)], 185_000m, 0m), null);
        OutgoingInvoiceItem[] items =
        [
            new() { Sequence = 1, Code = "ELV-01", Description = "Elevador social 6 paradas", Ncm = "84281000", Cfop = "5101", Quantity = 1m, Unit = "UN", UnitPrice = 185_000m, GrossAmount = 185_000m, AdditionalInfo = "Série SN-0001" },
        ];
        return (invoice, items);
    }

    private const string DanfeKey = "35261147225005000186550010000000071000000070";

    // ---------- Layouts no modelo único (depois da migração) ----------

    /// <summary>O cabeçalho do frame com a mesma empresa do dataset e um emissor fixo.</summary>
    internal static readonly PdfFrame Frame = new(new PdfCompany(TradeName, LegalName, Cnpj, Address, Contact, null, true), "Fulano de Tal",
        new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    /// <summary>O que é do frame, nunca do corpo: a empresa, a faixa de título e o "Emitido por … em …".</summary>
    private static string[] FrameParts(params string[] title) => [.. Company, .. title, PdfDocumentFrame.Issued(Frame)];

    private static string[] Mark(PdfMark mark, string? notice = null) => [PdfMarks.BandText(mark, notice) ?? string.Empty, PdfMarks.Watermark(mark)];

    [Fact]
    public async Task Orcamento()
    {
        var text = await TextAsync(new QuotePdfDocument(Frame, QuoteModel()));
        Fixture("orcamento", Body(text, FrameParts("Orçamento ORC-2026-0007 rev. 2", "Data: 03/11/2026")));
    }

    /// <summary>CA 20: rascunho e aguardando o estoque com "Rascunho", cancelado e estornado com "Cancelado"; "não é documento fiscal" continua.</summary>
    [Theory]
    [InlineData(ShipmentState.Shipped, PdfMark.None)]
    [InlineData(ShipmentState.Draft, PdfMark.Draft)]
    [InlineData(ShipmentState.AwaitingStockApproval, PdfMark.Draft)]
    [InlineData(ShipmentState.Cancelled, PdfMark.Cancelled)]
    public async Task Romaneio(ShipmentState status, PdfMark mark)
    {
        ShipmentPdfDocument.Mark(status).Should().Be(mark);
        var text = await TextAsync(new ShipmentPdfDocument(Frame, ShipmentModel() with { Status = status }));
        if (mark != PdfMark.None)
        {
            text.Should().Contain(Flat(PdfMarks.Label(mark))).And.Contain(PdfMarks.Watermark(mark));
        }

        Fixture("romaneio", Body(text, [.. FrameParts("Romaneio ROM-2026-0003", "Data: 03/11/2026"), .. Mark(mark)]));
    }

    /// <summary>CA 20: o holerite da folha em rascunho sai com "Rascunho" (o aviso "folha não fechada" na faixa).</summary>
    [Fact]
    public async Task Holerite()
    {
        var model = new PayslipPdfModel(Employee, new DateOnly(2026, 10, 1), PayrollRunType.Monthly, PayslipLines(), 7_904.82m, 711.23m, 7_193.59m, 7_904.82m, 7_904.82m, 7_193.59m, 632.39m,
            PayslipPdf.Mark(true, false), PayslipPdf.Notice(true, false));
        var text = await TextAsync(new PayslipPdfDocument(Frame, model));
        text.Should().Contain("Rascunho·folhanãofechada").And.Contain("RASCUNHO");
        Fixture("holerite", Body(text, [.. FrameParts("Holerite Mensal competência 10/2026"), .. Mark(PdfMark.Draft, PayslipPdf.DraftNotice)]));
    }

    [Fact]
    public async Task Espelho_de_ponto()
    {
        var model = new TimesheetPdfModel("Ana Souza", "123", "Soldador", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Days(), Totals(),
            new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var text = await TextAsync(new TimesheetPdfDocument(Frame, model));
        Fixture("espelho-de-ponto", Body(text, FrameParts("Espelho de ponto — " + PayrollRules.PeriodLabel(new DateOnly(2026, 10, 1)))));
    }

    [Fact]
    public async Task Aviso_de_ferias()
    {
        var model = new VacationNoticeModel("Ana Souza", "123", "Soldador", new DateOnly(2025, 1, 2), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 20), 20, 10,
            new DateOnly(2026, 11, 28), Day);
        var text = await TextAsync(new VacationNoticePdfDocument(Frame, model));
        Fixture("aviso-de-ferias", Body(text, FrameParts("Aviso de férias")));
    }

    /// <summary>CA 20: o termo para conferência sai sempre com "Simulação", com o aviso de conferência interna na faixa.</summary>
    [Fact]
    public async Task Termo_de_rescisao()
    {
        var model = new TerminationTermModel("Ana Souza", "123", "Soldador", new DateOnly(2020, 1, 2), new DateOnly(2026, 11, 15), "Dispensa sem justa causa", "Indenizado", 48,
            TerminationLines(), 2_750m, 247.5m, 2_502.5m, 220m, 3_200m, new DateOnly(2026, 11, 25), Day);
        var text = await TextAsync(new TerminationTermPdfDocument(Frame, model));
        text.Should().Contain(Flat("Simulação · " + TerminationTermPdfDocument.ConferenceMark)).And.Contain("SIMULAÇÃO");
        Fixture("termo-de-rescisao", Body(text, [.. FrameParts("Termo de rescisão para conferência"), .. Mark(PdfMark.Simulation, TerminationTermPdfDocument.ConferenceMark)]));
    }

    [Fact]
    public async Task Termo_de_conferencia_da_folha()
    {
        var model = new PayrollParallelTermModel(new DateOnly(2026, 10, 1), "Mensal", 12, new PayrollParallelTermTotals(98_000m, 21_000m, 77_000m, 7_840m),
            new PayrollParallelTermTotals(98_000m, 21_000m, 77_000m, 7_840m), "2026.10", new DateTime(2026, 11, 3, 9, 30, 0, DateTimeKind.Unspecified), "Carla Contadora", "SP-123456/O-7");
        var text = await TextAsync(new PayrollParallelTermDocument(Frame, model));
        Fixture("termo-de-conferencia-da-folha", Body(text, FrameParts("Termo de conferência da competência " + PayrollRules.PeriodLabel(new DateOnly(2026, 10, 1)))));
    }

    [Fact]
    public async Task Configuracao_fiscal()
    {
        var model = new FiscalSettingsPdfModel(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero), 1, 3, FiscalRows());
        var text = await TextAsync(new FiscalSettingsPdfDocument(Frame, model));
        Fixture("configuracao-fiscal", Body(text, FrameParts("Configuração fiscal para conferência da contadora")));
    }

    [Fact]
    public async Task Dados_do_titular()
    {
        var model = new PersonalDataPdfModel("LGPD-2026-0004", "03/11/2026 09:30", "Ana Souza", PersonalSections());
        var text = await TextAsync(new PersonalDataPdfDocument(Frame, model));
        Fixture("dados-do-titular", Body(text, FrameParts("Dados pessoais do titular — pedido LGPD-2026-0004")));
    }

    /// <summary>CA 21: a etiqueta mantém o formato próprio (uma por página, no tamanho do parâmetro), sem o cabeçalho e o rodapé do frame.</summary>
    [Fact]
    public async Task Etiqueta_do_patrimonio()
    {
        var pdf = await new QuestPdfRenderer().RenderAsync(new AssetLabelPdfDocument(LabelModel()));
        using (var read = PdfDocument.Open(pdf))
        {
            var page = read.GetPage(1);
            page.Width.Should().BeApproximately(50 * 72 / 25.4, 0.5, "50 mm de largura, como em Configurações → Parâmetros → Patrimônio");
            page.Height.Should().BeApproximately(25 * 72 / 25.4, 0.5);
        }

        Fixture("etiqueta-do-patrimonio", await TextAsync(new AssetLabelPdfDocument(LabelModel())));
    }

    /// <summary>CA 20 e 21: a prévia do DANFE mantém o layout do DANFE, com a marca padrão "Prévia" ("sem valor fiscal" na faixa) e a marca d'água.</summary>
    [Fact]
    public async Task Previa_do_DANFE()
    {
        var (invoice, items) = Invoice();
        var text = await TextAsync(new DanfePreviewDocument(new DanfePreviewModel(LegalName, Cnpj, DanfeKey, 7, 1, ["Confira o NCM do item 1."], invoice, items, "Pedido 123.")));
        var band = Flat(PdfMarks.Label(PdfMark.Preview));
        text.Should().Contain(PdfMarks.Watermark(PdfMark.Preview));
        var body = text.Replace(PdfMarks.Watermark(PdfMark.Preview), string.Empty, StringComparison.Ordinal);
        body.Should().StartWith(band, "a faixa da marca abre o DANFE");
        Fixture("previa-do-danfe", Body(body[band.Length..], []));
    }
}
