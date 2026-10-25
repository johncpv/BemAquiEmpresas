using System.Text;

namespace BemAqui.BuildingBlocks.Export;

/// <summary>
/// CSV seguro para planilha (MIG-01a CA 18, EST-04 CA 4): separador <c>;</c>, quebra <c>\r\n</c>, UTF-8 com BOM (a planilha abre o acento
/// certo) e sem injeção de fórmula. Toda exportação para o usuário passa por aqui.
/// </summary>
public static class SafeCsv
{
    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>
    /// Célula segura: começando com <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tabulação ou retorno de carro, ganha o prefixo <c>'</c> para a
    /// planilha não executar como fórmula; aspas, separador e quebra de linha vão entre aspas.
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

    /// <summary>Texto do CSV: o cabeçalho e as linhas, cada célula por <see cref="Cell"/>.</summary>
    public static string Text(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);
        var csv = new StringBuilder();
        csv.AppendJoin(';', header.Select(Cell)).Append("\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(';', row.Select(Cell)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>Bytes do arquivo: BOM do UTF-8 seguido do texto.</summary>
    public static byte[] Bytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(text)];
    }
}
