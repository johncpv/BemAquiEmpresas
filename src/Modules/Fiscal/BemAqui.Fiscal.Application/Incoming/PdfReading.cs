using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Domain;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>
/// Resposta do worker <c>fiscal-pdf-reader</c> (FIS-05 CA 2): o resultado, as páginas, a duração e os campos lidos. É dado de fora (o
/// worker lê um arquivo do fornecedor): o serviço confere chave por chave antes de guardar.
/// </summary>
public sealed record PdfReaderReply(PdfReadingOutcome Outcome, int? Pages, int DurationMs, IReadOnlyList<ReadingField> Fields)
{
    public static PdfReaderReply Of(PdfReadingOutcome outcome, int durationMs = 0) => new(outcome, null, durationMs, []);
}

/// <summary>
/// Leitura do PDF da DANFE fora do processo da API (FIS-05 CA 2): o worker isolado, sem rede, só leitura, com memória limitada e morto no
/// tempo limite, falado por um socket Unix. A API nunca abre o PDF.
/// </summary>
public interface IPdfInvoiceReader
{
    Task<PdfReaderReply> ReadAsync(byte[] pdf, CancellationToken cancellationToken = default);
}

/// <summary>
/// XML da nota pela chave (Distribuição DF-e, FIS-05 CA 6). O EmissorFiscal não oferece no MVP (ADR 0027, ponto 64): o padrão é
/// <see cref="NoIncomingXmlDownloader"/> e a tela segue para a leitura assistida. Quando o <c>IFiscalEmitter</c> tiver o
/// <c>DownloadByKeyAsync</c>, troca-se este registro e a nota segue pela FIS-04a, sem leitura assistida.
/// </summary>
public interface IIncomingXmlDownloader
{
    bool IsAvailable { get; }

    /// <summary>O XML autorizado da chave, ou null se não houver.</summary>
    Task<byte[]?> DownloadByKeyAsync(string accessKey, CancellationToken cancellationToken = default);
}

/// <summary>Sem Distribuição DF-e no MVP (ponto 64).</summary>
public sealed class NoIncomingXmlDownloader : IIncomingXmlDownloader
{
    public bool IsAvailable => false;

    public Task<byte[]?> DownloadByKeyAsync(string accessKey, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
}

/// <summary>
/// Conferência rápida dos bytes do PDF na API (FIS-05 CA 1), sem abrir o arquivo: nomes de conteúdo ativo (JavaScript, ação de envio ou
/// de importação, abertura de programa ou de arquivo remoto, anexo embutido, mídia, formulário XFA) e criptografia, inclusive escritos com
/// <c>#xx</c>. O worker confere de novo depois de abrir (os objetos comprimidos ficam para ele).
/// </summary>
public static class PdfActiveContentScan
{
    private static readonly HashSet<string> Dangerous = new(StringComparer.Ordinal)
    {
        "JavaScript", "JS", "Launch", "SubmitForm", "ImportData", "EmbeddedFile", "EmbeddedFiles", "RichMedia", "XFA", "Encrypt", "GoToR", "GoToE",
    };

    public static bool IsPdf(ReadOnlySpan<byte> content) => content.StartsWith("%PDF-"u8);

    /// <summary>true se algum nome do PDF (<c>/Nome</c>) é de conteúdo ativo ou de criptografia.</summary>
    public static bool HasActiveContent(ReadOnlySpan<byte> content)
    {
        var name = new System.Text.StringBuilder(32);
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != (byte)'/')
            {
                continue;
            }

            name.Clear();
            var j = i + 1;
            for (; j < content.Length && name.Length <= 32 && !IsDelimiter(content[j]); j++)
            {
                if (content[j] == (byte)'#' && j + 2 < content.Length && IsHex(content[j + 1]) && IsHex(content[j + 2]))
                {
                    name.Append((char)((Hex(content[j + 1]) << 4) | Hex(content[j + 2])));
                    j += 2;
                }
                else
                {
                    name.Append((char)content[j]);
                }
            }

            if (name.Length > 0 && Dangerous.Contains(name.ToString()))
            {
                return true;
            }

            i = j - 1;
        }

        return false;
    }

    private static bool IsDelimiter(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0 or (byte)'/' or (byte)'[' or (byte)']' or (byte)'<'
        or (byte)'>' or (byte)'(' or (byte)')' or (byte)'{' or (byte)'}' or (byte)'%';

    private static bool IsHex(byte b) => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';

    private static int Hex(byte b) => b <= (byte)'9' ? b - '0' : (b | 0x20) - 'a' + 10;
}

/// <summary>Argumento do job: só o id do rascunho (ADR 0037 §2: job não leva dado pessoal nem conteúdo).</summary>
public sealed record PdfInvoiceReadingArgs(Guid ReadingId);

/// <summary>
/// Leitura do PDF enviado (FIS-05 CA 1 e 2): no escopo do tenant, manda o PDF ao worker e guarda o resultado no rascunho. Sem nova
/// tentativa automática: a leitura que falhou ou estourou o tempo fica assim, e a tela oferece pedir o XML ou digitar a nota.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class PdfInvoiceReadingJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PdfInvoiceReadingJob> logger)
    : TenantJob<PdfInvoiceReadingArgs>(directory, scopes, logger)
{
    public static void Enqueue(IBackgroundJobClient jobs, Guid tenantId, Guid readingId)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        jobs.Create<PdfInvoiceReadingJob>(j => j.ExecuteAsync(tenantId, new PdfInvoiceReadingArgs(readingId), CancellationToken.None), new Hangfire.States.EnqueuedState(JobQueues.Default));
    }

    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, PdfInvoiceReadingArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        return services.GetRequiredService<IncomingInvoiceService>().CompleteReadingAsync(args.ReadingId, cancellationToken);
    }
}
