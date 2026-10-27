using System.Globalization;
using System.Text;

namespace BemAqui.HR.Domain;

/// <summary>Linha válida do arquivo: o número da linha no arquivo, a chave (matrícula ou CPF), a rubrica, a referência e o valor.</summary>
public sealed record PayrollCsvRow(int Line, string Key, string Code, string? Reference, decimal Amount);

/// <summary>Leitura do arquivo: as linhas válidas e os erros por linha ("Linha 12: ..."), sem nunca repetir o CPF lido.</summary>
public sealed record PayrollCsvResult(IReadOnlyList<PayrollCsvRow> Rows, IReadOnlyList<string> Errors);

/// <summary>
/// Layout padrão do sistema <c>v1</c> para importar os lançamentos da contadora (RH-02a CA 8 e 10, P-06):
/// <c>matricula_ou_cpf;codigo_rubrica;referencia;valor</c>, separador <c>;</c>, decimal com vírgula ("1.234,56"), UTF-8 (com ou sem
/// BOM), cabeçalho opcional na primeira linha. Até 2 MB e 5 mil linhas. É só texto: a célula que começa com <c>=</c>, <c>+</c>,
/// <c>-</c> ou <c>@</c> é tratada como texto (MIG-01a), por isso não vale como valor; nada é executado.
/// </summary>
public static class PayrollCsv
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxLines = 5000;
    public const string Header = "matricula_ou_cpf;codigo_rubrica;referencia;valor";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Lê o arquivo inteiro; erro de formato do arquivo (tamanho, codificação, linhas) vem como erro único sem linha.</summary>
    public static PayrollCsvResult Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return Fail("O arquivo está vazio.");
        }

        if (content.Length > MaxBytes)
        {
            return Fail("O arquivo passa de 2 MB. Divida em partes menores.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Fail("O arquivo não está em UTF-8. Salve de novo como \"CSV UTF-8\".");
        }

        text = text.TrimStart('﻿');
        var lines = text.Split('\n');
        var count = lines.Length > 0 && lines[^1].Trim().Length == 0 ? lines.Length - 1 : lines.Length;
        if (count > MaxLines + 1)
        {
            return Fail("O arquivo passa de 5 mil linhas. Divida em partes menores.");
        }

        var rows = new List<PayrollCsvRow>();
        var errors = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var number = i + 1;
            var raw = lines[i].TrimEnd('\r');
            if (raw.Trim().Length == 0)
            {
                continue;
            }

            var cells = raw.Split(';');
            if (i == 0 && cells.Length > 0 && cells[0].Trim().Equals("matricula_ou_cpf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (cells.Length != 4)
            {
                errors.Add($"Linha {number}: são 4 colunas separadas por ponto e vírgula (matrícula ou CPF; rubrica; referência; valor).");
                continue;
            }

            var key = Unquote(cells[0]);
            var code = Unquote(cells[1]);
            var reference = Unquote(cells[2]);
            var value = Unquote(cells[3]);
            if (key.Length == 0)
            {
                errors.Add($"Linha {number}: informe a matrícula ou o CPF.");
                continue;
            }

            if (code.Length == 0)
            {
                errors.Add($"Linha {number}: informe o código da rubrica.");
                continue;
            }

            if (reference.Length > PayrollRules.ReferenceMaxLength)
            {
                errors.Add($"Linha {number}: a referência aceita até 20 caracteres.");
                continue;
            }

            if (ParseAmount(value) is not { } amount)
            {
                errors.Add($"Linha {number}: valor inválido. Use número maior que zero com vírgula, como 1.234,56.");
                continue;
            }

            rows.Add(new PayrollCsvRow(number, key, code, reference.Length == 0 ? null : reference, amount));
        }

        if (rows.Count == 0 && errors.Count == 0)
        {
            return Fail("O arquivo não tem lançamentos.");
        }

        return new PayrollCsvResult(rows, errors);
    }

    /// <summary>
    /// Valor no padrão brasileiro, maior que zero e com até 2 casas. Célula que começa com <c>=</c>, <c>+</c>, <c>-</c> ou <c>@</c> é
    /// texto (proteção contra fórmula), nunca número.
    /// </summary>
    public static decimal? ParseAmount(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0 || value[0] is '=' or '+' or '-' or '@' or '\t' || !value.All(c => char.IsAsciiDigit(c) || c is '.' or ','))
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, PtBr, out var amount))
        {
            return null;
        }

        return PayrollRules.ValidAmount(amount) ? amount : null;
    }

    /// <summary>Código da rubrica seguro para a mensagem de erro: só letras, dígitos, "_" e "-", até 10.</summary>
    public static string SafeCode(string code) => new string((code ?? string.Empty).Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(PayrollRules.CodeMaxLength).ToArray());

    /// <summary>Chave da linha: CPF (11 dígitos válidos, com ou sem máscara) ou matrícula (o texto).</summary>
    public static bool IsCpf(string key, out string cpf)
    {
        cpf = BemAqui.SharedKernel.BrazilianDocuments.Normalize(key);
        return cpf.Length == 11 && key.All(c => char.IsAsciiDigit(c) || c is '.' or '-' or ' ') && BemAqui.SharedKernel.BrazilianDocuments.IsValidCpf(cpf);
    }

    private static string Unquote(string cell)
    {
        var text = cell.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal).Trim();
        }

        return text;
    }

    private static PayrollCsvResult Fail(string message) => new([], [message]);
}
