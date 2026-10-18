using System.Globalization;
using System.Text;
using System.Text.Json;
using BemAqui.Migrations.Domain;

namespace BemAqui.Migrations.Application;

/// <summary>
/// Relatório no console, em JSON e em CSV (CA 17 e 18). O CSV é protegido contra injeção de fórmula, nada sai com dado
/// pessoal completo (a staging já guarda mascarado) e os arquivos nascem com permissão 0600.
/// </summary>
public static class ImportReportWriter
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly JsonSerializerOptions Indented = new(ImportRunner.Json) { WriteIndented = true };

    public static string Label(ImportOutcome outcome) => outcome switch
    {
        ImportOutcome.Imported => "importado",
        ImportOutcome.Updated => "atualizado",
        ImportOutcome.Unchanged => "sem alteração",
        ImportOutcome.Merged => "mesclado",
        ImportOutcome.Linked => "vinculado",
        ImportOutcome.Skipped => "ignorado",
        ImportOutcome.Conflict => "conflito",
        _ => "erro",
    };

    public static string Console(ImportReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Execução {report.RunId} ({(report.Mode == ImportMode.DryRun ? "simulação, nada gravado nos destinos" : "real")}), operador {report.Operator}").AppendLine();
        if (report.SnapshotTakenAt is { } taken)
        {
            text.Append("Snapshot extraído em ").Append(taken.ToString("dd/MM/yyyy HH:mm 'UTC'", PtBr)).AppendLine();
        }

        text.AppendLine("Entidade        Lidos  Import.  Atualiz.  Sem alt.  Mescl.  Vincul.  Ignor.  Confl.  Erros  Avisos");
        foreach (var e in report.Entities)
        {
            text.Append(CultureInfo.InvariantCulture, $"{e.Entity,-14} {e.Read,6} {e.Imported,8} {e.Updated,9} {e.Unchanged,9} {e.Merged,7} {e.Linked,8} {e.Skipped,7} {e.Conflicts,7} {e.Errors,6} {e.Warnings,7}").AppendLine();
        }

        text.AppendLine(report.Failure is null ? "Situação: concluída." : "Situação: falhou. " + report.Failure);
        return text.ToString();
    }

    public static string Json(ImportReport report, IReadOnlyList<ImportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var pending = items.Where(IsPending).Select(i => new
        {
            i.Entity, i.SourceTable, i.SourceId, Outcome = Label(i.Outcome), i.TargetType, i.TargetId, i.Message, i.Warning,
            Differences = i.Differences is null ? null : JsonSerializer.Deserialize<List<FieldDifference>>(i.Differences, ImportRunner.Json),
        });
        return JsonSerializer.Serialize(new { report, pending }, Indented);
    }

    /// <summary>Pendência da tela e do relatório: erro, conflito ou aviso (CA 19).</summary>
    public static bool IsPending(ImportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Outcome is ImportOutcome.Error or ImportOutcome.Conflict || item.Warning is not null;
    }

    public static string Csv(IReadOnlyList<ImportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var csv = new StringBuilder("entidade;tabela;id_origem;resultado;tipo_destino;id_destino;mensagem;aviso;diferencas\r\n");
        foreach (var i in items)
        {
            string?[] cells = [i.Entity, i.SourceTable, i.SourceId, Label(i.Outcome), i.TargetType, i.TargetId?.ToString(), i.Message, i.Warning, i.Differences];
            csv.AppendJoin(';', cells.Select(Cell)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>
    /// Célula segura (CA 18): começando com <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tabulação ou retorno de carro, ganha o
    /// prefixo <c>'</c> para a planilha não executar como fórmula; aspas, separador e quebra de linha vão entre aspas.
    /// </summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    /// <summary>Grava <c>run-&lt;id&gt;.json</c> e <c>.csv</c> no diretório do relatório, com permissão 0600 (CA 18).</summary>
    public static async Task<IReadOnlyList<string>> WriteFilesAsync(string directory, ImportReport report, IReadOnlyList<ImportItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        Directory.CreateDirectory(directory);
        var json = Path.Combine(directory, FileName(report.RunId, "json"));
        var csv = Path.Combine(directory, FileName(report.RunId, "csv"));
        await WriteAsync(json, Json(report, items), bom: false, cancellationToken);
        await WriteAsync(csv, Csv(items), bom: true, cancellationToken); // BOM: a planilha abre o acento certo
        return [json, csv];
    }

    public static string FileName(Guid runId, string extension) => "run-" + runId.ToString("N") + "." + extension;

    private static async Task WriteAsync(string path, string content, bool bom, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(path, options);
        if (bom)
        {
            await stream.WriteAsync(Encoding.UTF8.Preamble.ToArray(), cancellationToken);
        }

        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); // arquivo que já existia mantém o modo antigo no Create
        }
    }
}
