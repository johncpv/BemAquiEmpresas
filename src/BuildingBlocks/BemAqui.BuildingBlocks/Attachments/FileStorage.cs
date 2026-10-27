namespace BemAqui.BuildingBlocks.Attachments;

/// <summary>Armazenamento dos arquivos (disco local hoje; S3 depois, atrás desta interface).</summary>
public interface IFileStorage
{
    /// <summary>Grava e devolve o caminho relativo gerado (nunca derivado do nome enviado pelo usuário).</summary>
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava debaixo de um prefixo (ex.: o id do tenant, revisão pré-Fases 4 a 8 §4.3), que só aceita letras minúsculas e dígitos. Os
    /// arquivos de guarda legal (o XML da NF-e, FIS-01 CA 13) usam o prefixo do tenant.
    /// </summary>
    Task<string> SaveAsync(Stream content, string prefix, CancellationToken cancellationToken = default);

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

    public Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default) => SaveUnderAsync(content, null, cancellationToken);

    public Task<string> SaveAsync(Stream content, string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (prefix.Length > 64 || !prefix.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c)))
        {
            throw new ArgumentException("Prefixo de armazenamento inválido.", nameof(prefix));
        }

        return SaveUnderAsync(content, prefix, cancellationToken);
    }

    private async Task<string> SaveUnderAsync(Stream content, string? prefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var now = clock.GetUtcNow();
        var relative = Path.Combine(now.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture), now.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture), Guid.CreateVersion7().ToString("N"));
        if (prefix is not null)
        {
            relative = Path.Combine(prefix, relative);
        }

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
