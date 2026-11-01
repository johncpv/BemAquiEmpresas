using System.Globalization;
using BemAqui.Fiscal.Application.Incoming;
using BemAqui.Fiscal.Domain;
using BemAqui.Fiscal.Infrastructure.PdfReader;
using BemAqui.PdfReader;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-05 (unidade, sem banco): o worker lê a DANFE fictícia (chave, emissão, destinatário, totais e itens com a situação) e recusa PDF
/// com JavaScript (também escrito com <c>#xx</c> e dentro de object stream comprimido), anexo embutido, criptografia, sem texto e com páginas
/// demais (CA 1 a 4); a varredura dos bytes na API; o servidor do socket com o tempo limite e o processo "morto" (CA 2); o cliente com o
/// worker fora do ar.
/// </summary>
public class PdfReaderTests
{
    private static Dictionary<string, ReadField> Fields(ReadResponse r) => r.Fields.ToDictionary(f => f.Key);

    [Fact]
    public async Task Le_a_DANFE_com_a_chave_os_totais_e_os_itens_marcando_o_que_nao_bate()
    {
        var sample = new NfeSample
        {
            Freight = 20m,
            Items = [new("CH-2MM", "CHAPA DE ACO 2MM", "72085100", "UN", 10m, 12.5m), new("PERF-40", "PERFIL U 40X20 GALVANIZADO", "72163100", "M", 3.5m, 48.9m, IpiRate: 5m)],
        };
        var response = DanfePdfReader.Read(await DanfeSamples.PdfAsync(sample), CancellationToken.None);

        response.Result.Should().Be("Read");
        response.Pages.Should().Be(1);
        var f = Fields(response);
        f["accessKey"].Should().Be(new ReadField("accessKey", NfeSamples.AccessKey(sample), DanfeParser.Read));
        f["issuedAt"].Value.Should().Be(sample.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        f["issuerName"].Should().Be(new ReadField("issuerName", sample.IssuerName, DanfeParser.Uncertain), "o nome vem por heurística e é sempre conferido");
        f["issuerDocument"].Value.Should().Be(sample.IssuerCnpj);
        f["recipientDocument"].Should().Be(new ReadField("recipientDocument", NfeSamples.TenantCnpj, DanfeParser.Read));
        var (products, icms, ipi, total) = DanfeSamples.Totals(sample);
        f["totals.products"].Should().Be(new ReadField("totals.products", products.ToString(CultureInfo.InvariantCulture), DanfeParser.Read));
        f["totals.icms"].Value.Should().Be(icms.ToString(CultureInfo.InvariantCulture));
        f["totals.icmsSt"].Value.Should().Be("0", "o rótulo do ICMS não confunde com o do ICMS-ST");
        f["totals.freight"].Value.Should().Be("20");
        f["totals.ipi"].Value.Should().Be(ipi.ToString(CultureInfo.InvariantCulture));
        f["totals.total"].Should().Be(new ReadField("totals.total", total.ToString(CultureInfo.InvariantCulture), DanfeParser.Read));
        f["items.0.code"].Value.Should().Be("CH-2MM");
        f["items.0.description"].Value.Should().Be("CHAPA DE ACO 2MM");
        f["items.1.description"].Value.Should().Be("PERFIL U 40X20 GALVANIZADO");
        f["items.1.ncm"].Should().Be(new ReadField("items.1.ncm", "72163100", DanfeParser.Read));
        f["items.1.unit"].Value.Should().Be("M");
        f["items.1.quantity"].Should().Be(new ReadField("items.1.quantity", "3.5", DanfeParser.Read));
        f["items.1.unitPrice"].Value.Should().Be("48.9");
        f["items.1.amount"].Value.Should().Be("171.15");
        f.Keys.Should().NotContain("items.2.code");

        // Itens que não somam o total dos produtos: o total fica "incerto" (a conferência decide).
        var wrong = DanfePdfReader.Read(await DanfeSamples.PdfAsync(sample, new DanfeOptions(PrintedProducts: products + 10m)), CancellationToken.None);
        Fields(wrong)["totals.products"].State.Should().Be(DanfeParser.Uncertain);
        Fields(wrong)["totals.total"].State.Should().Be(DanfeParser.Uncertain);

        // Sem chave impressa: "não encontrado".
        var noKey = DanfePdfReader.Read(await DanfeSamples.PdfAsync(sample, new DanfeOptions(WithKey: false)), CancellationToken.None);
        Fields(noKey)["accessKey"].Should().Be(new ReadField("accessKey", null, DanfeParser.NotFound));
    }

    [Fact]
    public async Task Recusa_conteudo_ativo_criptografia_sem_texto_paginas_demais_e_o_que_nao_e_pdf()
    {
        DanfePdfReader.Read(DanfeSamples.JavaScript(), CancellationToken.None).Result.Should().Be("Unsafe");
        DanfePdfReader.Read(DanfeSamples.JavaScript(obfuscated: true), CancellationToken.None).Result.Should().Be("Unsafe", "o PdfPig decodifica o #xx do nome");
        DanfePdfReader.Read(DanfeSamples.CompressedJavaScript(), CancellationToken.None).Result.Should().Be("Unsafe", "a ação dentro do object stream comprimido também conta");
        DanfePdfReader.Read(DanfeSamples.EmbeddedFile(), CancellationToken.None).Result.Should().Be("Unsafe");
        DanfePdfReader.Read(DanfeSamples.Encrypted(), CancellationToken.None).Result.Should().BeOneOf("Unsafe", "Invalid");
        DanfePdfReader.Read(await DanfeSamples.ImageOnlyAsync(), CancellationToken.None).Result.Should().Be("NoText", "sem OCR no MVP (ponto 65)");
        DanfePdfReader.Read(await DanfeSamples.PagesAsync(11), CancellationToken.None).Should().BeEquivalentTo(new { Result = "TooManyPages", Pages = 11 });
        DanfePdfReader.Read("<html><script>alert(1)</script></html>"u8.ToArray(), CancellationToken.None).Result.Should().Be("Invalid");
        DanfePdfReader.Read("%PDF-1.4 lixo sem estrutura"u8.ToArray(), CancellationToken.None).Result.Should().BeOneOf("Invalid", "Failed", "TooManyPages");
        DanfePdfReader.Read(new byte[ReaderWire.MaxRequestBytes + 1], CancellationToken.None).Result.Should().Be("Invalid");
    }

    [Fact]
    public async Task A_API_recusa_pelos_bytes_o_conteudo_ativo_visivel_sem_abrir_o_pdf()
    {
        PdfActiveContentScan.HasActiveContent(DanfeSamples.JavaScript()).Should().BeTrue();
        PdfActiveContentScan.HasActiveContent(DanfeSamples.JavaScript(obfuscated: true)).Should().BeTrue("/J#61vaScript é /JavaScript");
        PdfActiveContentScan.HasActiveContent(DanfeSamples.EmbeddedFile()).Should().BeTrue();
        PdfActiveContentScan.HasActiveContent(DanfeSamples.Encrypted()).Should().BeTrue();
        PdfActiveContentScan.HasActiveContent(DanfeSamples.CompressedJavaScript()).Should().BeFalse("comprimido não aparece nos bytes: o worker confere depois de abrir");
        PdfActiveContentScan.HasActiveContent(await DanfeSamples.PdfAsync(new NfeSample())).Should().BeFalse();
        PdfActiveContentScan.HasActiveContent("%PDF-1.4 /JSON /JavaScripts /Encrypted"u8).Should().BeFalse("só o nome inteiro conta");
    }

    [Fact]
    public async Task O_servidor_responde_falhou_por_tempo_mata_a_leitura_e_o_log_nao_tem_o_texto()
    {
        var path = Path.Combine(Path.GetTempPath(), "bq-pdf-" + Guid.NewGuid().ToString("N")[..12] + ".sock");
        var logs = new List<string>();
        var killed = 0;
        var slow = false;
        ReadResponse Read(byte[] pdf, CancellationToken ct)
        {
            if (slow)
            {
                Thread.Sleep(TimeSpan.FromSeconds(10)); // leitura presa que nem olha o cancelamento: só a morte do processo resolve
            }

            return DanfePdfReader.Read(pdf, ct);
        }

        using var stop = new CancellationTokenSource();
        var server = new ReaderServer(path, TimeSpan.FromSeconds(2), Read, m => { lock (logs) { logs.Add(m); } }, () => Interlocked.Increment(ref killed));
        var running = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await server.RunAsync(stop.Token); // o "container" reinicia depois de cada morte
            }
        });
        await WaitForAsync(() => File.Exists(path));
        var client = new SocketPdfInvoiceReader(Options.Create(new PdfReaderOptions { SocketPath = path, TimeoutSeconds = 2 }));
        var sample = new NfeSample();

        var ok = await client.ReadAsync(await DanfeSamples.PdfAsync(sample));
        ok.Outcome.Should().Be(PdfReadingOutcome.Read);
        ok.Fields.Should().Contain(new ReadingField("accessKey", NfeSamples.AccessKey(sample), ReadingFieldState.Read));

        slow = true;
        var late = await client.ReadAsync(await DanfeSamples.PdfAsync(sample));
        late.Outcome.Should().Be(PdfReadingOutcome.TimedOut);
        killed.Should().Be(1, "estourado o tempo, o processo do worker é morto");

        slow = false;
        PdfReaderReply again;
        var tries = 0;
        do
        {
            await Task.Delay(100);
            again = await client.ReadAsync(DanfeSamples.JavaScript());
        }
        while (again.Outcome == PdfReadingOutcome.Failed && ++tries < 50);
        again.Outcome.Should().Be(PdfReadingOutcome.Unsafe, "o worker reiniciado volta a atender");

        string all;
        lock (logs)
        {
            all = string.Join("\n", logs);
        }

        all.Should().Contain("resultado=TimedOut").And.Contain("…" + NfeSamples.AccessKey(sample)[^6..]);
        all.Should().NotContain(NfeSamples.AccessKey(sample)).And.NotContain(sample.IssuerName).And.NotContain("CHAPA DE ACO", "o log leva só o resultado, a duração e a chave mascarada (CA 12)");
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Worker_fora_do_ar_e_leitura_que_falhou()
    {
        var client = new SocketPdfInvoiceReader(Options.Create(new PdfReaderOptions { SocketPath = Path.Combine(Path.GetTempPath(), "bq-nada-" + Guid.NewGuid().ToString("N")[..8] + ".sock") }));
        (await client.ReadAsync("%PDF-1.4"u8.ToArray())).Outcome.Should().Be(PdfReadingOutcome.Failed);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("O worker de teste não subiu.");
            }

            await Task.Delay(50);
        }
    }
}
