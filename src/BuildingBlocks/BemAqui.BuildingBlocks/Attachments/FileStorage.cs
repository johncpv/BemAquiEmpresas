namespace BemAqui.BuildingBlocks.Attachments;

/// <summary>Armazenamento dos arquivos (disco local hoje; S3 depois, atrás desta interface).</summary>
public interface IFileStorage
{
    /// <summary>Grava e devolve o caminho relativo gerado (nunca derivado do nome enviado pelo usuário).</summary>
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Seção <c>Storage</c>.</summary>
public sealed class FileStorageOptions
{
    public const string Section = "Storage";

    /// <summary>Raiz no volume (ex.: <c>/data/files</c>), fora da raiz web.</summary>
    public string Root { get; set; } = "storage";
}

public sealed class LocalFileStorage(Microsoft.Extensions.Options.IOptions<FileStorageOptions> options, TimeProvider clock) : IFileStorage
{
    private string Root => Path.GetFullPath(options.Value.Root);

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var now = clock.GetUtcNow();
        var relative = Path.Combine(now.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture), now.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture), Guid.CreateVersion7().ToString("N"));
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return relative.Replace((char)92, '/');
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }

        return Task.CompletedTask;
    }

    // Defesa contra path traversal: o caminho final precisa ficar dentro da raiz.
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Caminho de arquivo fora do storage.");
        }

        return full;
    }
}
