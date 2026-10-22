using System.Net.Sockets;
using System.Text;

namespace BemAqui.BuildingBlocks.Attachments;

public enum ScanVerdict
{
    Clean,
    Infected,
    Unavailable,

    /// <summary>O clamd recusou pelo tamanho (<c>StreamMaxLength</c>): o arquivo é recusado, nunca liberado sem varredura (ADR 0019 §7 adendo).</summary>
    TooLarge,
}

public sealed record ScanResult(ScanVerdict Verdict, string? Signature = null);

public interface IVirusScanner
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default);
}

/// <summary>Seção <c>ClamAV</c>.</summary>
public sealed class ClamAvOptions
{
    public const string Section = "ClamAV";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 3310;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maior arquivo enviado ao clamd (ADR 0019 §7 adendo: 30 MB, acima dos 25 MB do maior anexo); acima, recusado.</summary>
    public long StreamMaxLength { get; set; } = 30L * 1024 * 1024;
}

/// <summary>clamd pelo protocolo INSTREAM (TCP). Falha de conexão vira <see cref="ScanVerdict.Unavailable"/> (falha fechada).</summary>
public sealed class ClamAvScanner(Microsoft.Extensions.Options.IOptions<ClamAvOptions> options) : IVirusScanner
{
    private const int ChunkSize = 64 * 1024;

    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.CanSeek && content.Length - content.Position > options.Value.StreamMaxLength)
        {
            return new ScanResult(ScanVerdict.TooLarge);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.Timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(options.Value.Host, options.Value.Port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);

            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
            {
                var size = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(read));
                await stream.WriteAsync(size, timeout.Token);
                await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            await stream.WriteAsync(new byte[4], timeout.Token);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var reply = (await reader.ReadToEndAsync(timeout.Token)).TrimEnd('\0', '\n');
            return Parse(reply);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ScanResult(ScanVerdict.Unavailable);
        }
    }

    /// <summary>
    /// "stream: OK" → limpo; "stream: Eicar-Signature FOUND" → infectado; "INSTREAM size limit exceeded" → grande demais;
    /// qualquer outra resposta → indisponível.
    /// </summary>
    public static ScanResult Parse(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Contains("size limit exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return new ScanResult(ScanVerdict.TooLarge);
        }

        if (reply.EndsWith(" OK", StringComparison.Ordinal))
        {
            return new ScanResult(ScanVerdict.Clean);
        }

        if (reply.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var signature = reply[(reply.IndexOf(':', StringComparison.Ordinal) + 1)..^" FOUND".Length].Trim();
            return new ScanResult(ScanVerdict.Infected, signature);
        }

        return new ScanResult(ScanVerdict.Unavailable);
    }
}
