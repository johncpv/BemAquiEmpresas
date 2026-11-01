using System.Diagnostics;
using System.Net.Sockets;

namespace BemAqui.PdfReader;

/// <summary>
/// Servidor do worker no socket Unix (FIS-05 CA 2): uma leitura por vez, cada uma com o tempo limite. Estourado o tempo, responde
/// <c>TimedOut</c> (se ainda der) e chama <see cref="onTimeout"/>: no container, o processo sai e o Docker o reinicia, matando a leitura
/// presa. O log leva só o resultado, a duração e a chave mascarada (CA 12).
/// </summary>
internal sealed class ReaderServer(string socketPath, TimeSpan timeout, Func<byte[], CancellationToken, ReadResponse> read, Action<string> log, Action onTimeout)
{
    public async Task RunAsync(CancellationToken stop)
    {
        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        if (!OperatingSystem.IsWindows())
        {
            // Só o dono (o worker) e o grupo (a API) falam com o socket.
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
        }

        listener.Listen(16);
        log("pronto");
        try
        {
            while (!stop.IsCancellationRequested)
            {
                Socket connection;
                try
                {
                    connection = await listener.AcceptAsync(stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!await HandleAsync(connection, stop).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        finally
        {
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }
        }
    }

    /// <summary>Uma leitura; false quando estourou o tempo (o servidor para depois de avisar).</summary>
    private async Task<bool> HandleAsync(Socket connection, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        await using var stream = new NetworkStream(connection, ownsSocket: true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(timeout);
        byte[]? pdf;
        try
        {
            pdf = await ReaderWire.ReadFrameAsync(stream, ReaderWire.MaxRequestBytes, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or EndOfStreamException or SocketException)
        {
            log("recebimento interrompido");
            return true;
        }

        if (pdf is null)
        {
            await TryWriteAsync(stream, ReadResponse.Of("Invalid", null, Elapsed(clock))).ConfigureAwait(false);
            log("resultado=Invalid ms=" + Elapsed(clock));
            return true;
        }

        var work = Task.Factory.StartNew(() => Run(pdf, deadline.Token), deadline.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var remaining = timeout - clock.Elapsed;
        var finished = remaining > TimeSpan.Zero && await Task.WhenAny(work, Task.Delay(remaining, stop)).ConfigureAwait(false) == work;
        if (!finished)
        {
            await TryWriteAsync(stream, ReadResponse.Of("TimedOut", null, Elapsed(clock))).ConfigureAwait(false);
            log("resultado=TimedOut ms=" + Elapsed(clock));
            onTimeout();
            return false;
        }

        var response = await work.ConfigureAwait(false);
        await TryWriteAsync(stream, response).ConfigureAwait(false);
        var key = response.Fields.FirstOrDefault(f => f.Key == "accessKey")?.Value;
        log($"resultado={response.Result} ms={response.DurationMs} paginas={response.Pages?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} chave={Mask(key)}");
        return true;
    }

    private ReadResponse Run(byte[] pdf, CancellationToken cancellationToken)
    {
        try
        {
            return read(pdf, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ReadResponse.Of("TimedOut", null, 0);
        }
#pragma warning disable CA1031 // qualquer erro do leitor vira "Failed", sem o conteúdo no log
        catch (Exception)
#pragma warning restore CA1031
        {
            return ReadResponse.Of("Failed", null, 0);
        }
    }

    private static async Task TryWriteAsync(Stream stream, ReadResponse response)
    {
        using var write = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await ReaderWire.WriteFrameAsync(stream, ReaderWire.Serialize(response), write.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // a API desistiu ou caiu: nada a fazer
        }
    }

    private static int Elapsed(Stopwatch clock) => (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);

    /// <summary>Só os últimos 6 dígitos da chave (CA 12).</summary>
    public static string Mask(string? key) => key is { Length: >= 6 } k ? "…" + k[^6..] : "-";
}
