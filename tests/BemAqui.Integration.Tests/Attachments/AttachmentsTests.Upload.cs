using System.Text;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Attachments;

public partial class AttachmentsTests
{
    [Fact]
    public async Task Upload_valido_fica_limpo_e_baixa_com_o_conteudo()
    {
        var (provider, _, root) = await CreateAsync(postgres);
        await using var disposeProvider = provider;

        var uploaded = await UploadAsync(provider, "../../etc/proposta.pdf", Pdf);

        uploaded.IsSuccess.Should().BeTrue();
        uploaded.Value.FileName.Should().Be("proposta.pdf", "o nome enviado nunca vira caminho");
        uploaded.Value.ScanStatus.Should().Be(ScanStatus.Clean);
        FilesIn(root).Should().ContainSingle().Which.Should().NotContain("proposta", "o arquivo fica em caminho aleatório");

        var download = await DownloadAsync(provider, uploaded.Value.Id);
        using var reader = new StreamReader(download.Value.Content);
        (await reader.ReadToEndAsync()).Should().StartWith("%PDF-1.7");
        download.Value.ContentType.Should().Be("application/pdf");
    }

    [Theory]
    [InlineData("logo.svg", "<svg><script>alert(1)</script></svg>")]
    [InlineData("fatura.pdf", "MZ executavel disfarcado")]
    public async Task Tipo_ou_conteudo_fora_da_allowlist_e_recusado(string fileName, string content)
    {
        var (provider, _, root) = await CreateAsync(postgres);
        await using var disposeProvider = provider;

        var uploaded = await UploadAsync(provider, fileName, Encoding.UTF8.GetBytes(content));

        uploaded.Error!.Code.Should().Be("attachment.type_not_allowed");
        uploaded.Error.Type.Should().Be(ErrorType.Validation);
        FilesIn(root).Should().BeEmpty();
    }

    [Fact]
    public async Task Arquivo_acima_de_25MB_e_recusado()
    {
        var (provider, _, _) = await CreateAsync(postgres);
        await using var disposeProvider = provider;
        var big = new byte[AttachmentPolicy.MaxSize + 1];
        Pdf.CopyTo(big, 0);

        (await UploadAsync(provider, "grande.pdf", big)).Error!.Code.Should().Be("attachment.size");
    }

    [Fact]
    public async Task Ameaca_detectada_descarta_o_arquivo_e_recusa_o_envio()
    {
        var (provider, scanner, root) = await CreateAsync(postgres);
        await using var disposeProvider = provider;
        scanner.Verdict = ScanVerdict.Infected;

        var uploaded = await UploadAsync(provider, "proposta.pdf", Pdf);

        uploaded.Error!.Code.Should().Be("attachment.infected");
        FilesIn(root).Should().BeEmpty();
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Attachments.CountAsync()).Should().Be(0);
    }
}
