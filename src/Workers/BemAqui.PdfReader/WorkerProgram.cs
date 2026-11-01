using System.Globalization;

namespace BemAqui.PdfReader;

/// <summary>
/// Entrada do worker <c>fiscal-pdf-reader</c> (FIS-05 CA 2): escuta o socket Unix do volume compartilhado só com a API
/// (<c>PDF_READER_SOCKET</c>, padrão <c>/run/pdf-reader/reader.sock</c>) e lê um PDF por vez com o tempo limite
/// (<c>PDF_READER_TIMEOUT_SECONDS</c>, padrão 30). No tempo limite o processo sai com <see cref="TimeoutExitCode"/> e o container reinicia.
/// </summary>
internal static class WorkerProgram
{
    public const int TimeoutExitCode = 75;

    public static async Task<int> Main()
    {
        var socket = Environment.GetEnvironmentVariable("PDF_READER_SOCKET") is { Length: > 0 } path ? path : "/run/pdf-reader/reader.sock";
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("PDF_READER_TIMEOUT_SECONDS"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s is > 0 and <= 300 ? s : 30;
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
        var server = new ReaderServer(socket, TimeSpan.FromSeconds(seconds), DanfePdfReader.Read, Log, () => Environment.Exit(TimeoutExitCode));
        await server.RunAsync(stop.Token).ConfigureAwait(false);
        return 0;
    }

    private static void Log(string message) =>
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow:O} fiscal-pdf-reader {message}"));
}
