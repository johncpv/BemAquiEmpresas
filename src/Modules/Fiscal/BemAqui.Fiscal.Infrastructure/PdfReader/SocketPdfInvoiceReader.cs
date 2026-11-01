using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Fiscal.Application.Incoming;
using BemAqui.Fiscal.Domain;
using Microsoft.Extensions.Options;

namespace BemAqui.Fiscal.Infrastructure.PdfReader;

/// <summary>Onde está o worker <c>fiscal-pdf-reader</c> (FIS-05 CA 2): o socket Unix do volume compartilhado e o tempo limite.</summary>
public sealed class PdfReaderOptions
{
    public const string Section = "Fiscal:PdfReader";

    public string SocketPath { get; set; } = "/run/pdf-reader/reader.sock";

    /// <summary>Tempo limite da leitura no worker (30 s); a API espera mais 5 s pela resposta "falhou por tempo".</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Cliente do worker pelo socket Unix (FIS-05 CA 2): manda o PDF (4 bytes de tamanho e o conteúdo) e lê a resposta (até 2 MB de JSON).
/// Worker fora do ar é <c>Failed</c>; sem resposta no tempo limite (o worker foi morto) é <c>TimedOut</c>. A API nunca abre o PDF.
/// </summary>
internal sealed class SocketPdfInvoiceReader(IOptions<PdfReaderOptions> options) : IPdfInvoiceReader
{
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<PdfReaderReply> ReadAsync(byte[] pdf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var settings = options.Value;
        var limit = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 300));
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limit + TimeSpan.FromSeconds(5));
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(settings.SocketPath), deadline.Token);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PdfReaderReply.Of(PdfReadingOutcome.Failed, Elapsed(clock));
        }

        await using var stream = new NetworkStream(socket, ownsSocket: false);
        try
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, pdf.Length);
            await stream.WriteAsync(header, deadline.Token);
            await stream.WriteAsync(pdf, deadline.Token);
            await stream.FlushAsync(deadline.Token);

            await stream.ReadExactlyAsync(header, deadline.Token);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is <= 0 or > MaxResponseBytes)
            {
                return PdfReaderReply.Of(PdfReadingOutcome.Failed, Elapsed(clock));
            }

            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, deadline.Token);
            var reply = JsonSerializer.Deserialize<WorkerReply>(payload, Json);
            if (reply is null || !Enum.TryParse<PdfReadingOutcome>(reply.Result, ignoreCase: false, out var outcome) || !Enum.IsDefined(outcome))
            {
                return PdfReaderReply.Of(PdfReadingOutcome.Failed, Elapsed(clock));
            }

            var fields = (reply.Fields ?? []).Where(f => f is { Key: not null })
                .Select(f => new ReadingField(f.Key!, f.Value, Enum.TryParse<ReadingFieldState>(f.State, ignoreCase: false, out var s) && Enum.IsDefined(s) ? s : ReadingFieldState.Uncertain))
                .ToList();
            return new PdfReaderReply(outcome, reply.Pages is >= 0 and <= 10_000 ? reply.Pages : null, Math.Clamp(reply.DurationMs, 0, int.MaxValue), fields);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PdfReaderReply.Of(PdfReadingOutcome.TimedOut, Elapsed(clock));
        }
        catch (Exception e) when (e is IOException or SocketException or EndOfStreamException or JsonException)
        {
            // Conexão caída sem resposta: o worker foi morto no tempo limite (TimedOut) ou caiu antes (Failed).
            return PdfReaderReply.Of(clock.Elapsed >= limit ? PdfReadingOutcome.TimedOut : PdfReadingOutcome.Failed, Elapsed(clock));
        }
    }

    private static int Elapsed(Stopwatch clock) => (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);

    private sealed record WorkerField(string? Key, string? Value, string? State);

    private sealed record WorkerReply(string? Result, int? Pages, int DurationMs, IReadOnlyList<WorkerField>? Fields);
}
