using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Export;

/// <summary>Seção <c>Export</c> (ADR 0045 §3): teto de linhas por formato. Acima dele, 422 <c>export_too_large</c>, sem gerar nada.</summary>
public sealed class ExportOptions
{
    public const string Section = "Export";

    public int MaxXlsxRows { get; set; } = 10_000;

    public int MaxPdfRows { get; set; } = 2_000;

    public int MaxRows(ExportFormat format) => format == ExportFormat.Pdf ? MaxPdfRows : MaxXlsxRows;
}

/// <summary>
/// Contrato das rotas <c>GET …/export?format=xlsx|pdf&amp;&lt;os filtros da lista&gt;</c> (ADR 0045 §3). A rota usa a mesma permissão da
/// lista e a mesma consulta do handler (sem paginação); este serviço confere o formato e o teto, aplica a máscara das colunas sensíveis
/// pela permissão do usuário (sem 2FA, decisão do PO), gera o arquivo, registra a exportação na auditoria (quem, quando, formato, linhas e
/// filtros) e devolve o download <c>{tela}-{AAAA-MM-DD}.{ext}</c> com os cabeçalhos de download seguro.
/// </summary>
public sealed class ListExportService(
    IEnumerable<ITabularExporter> exporters, IPermissionChecker permissions, IAuditWriter audit, PdfFrameFactory frames, IOptions<ExportOptions> options, TimeProvider clock,
    IPdfRenderer renderer)
{
    public const string TooLargeCode = "export_too_large";

    /// <summary>Chave nos filtros da auditoria quando o arquivo leva dado sensível sem máscara (EXP-05: CPF ou salário completos).</summary>
    public const string SensitiveKey = "sensitive";
    public const string InvalidFormatCode = "export_format_invalid";

    /// <summary>"xlsx" ou "pdf" (sem diferenciar maiúsculas); outro valor ou vazio é inválido.</summary>
    public static ExportFormat? ParseFormat(string? format) => format?.Trim().ToUpperInvariant() switch
    {
        "XLSX" => ExportFormat.Xlsx,
        "PDF" => ExportFormat.Pdf,
        _ => null,
    };

    /// <summary>Nome do arquivo: <c>{tela}-{AAAA-MM-DD}.{ext}</c>, com a data de São Paulo.</summary>
    public static string FileName(string fileBase, DateOnly day, string extension) =>
        $"{fileBase}-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.{extension}";

    /// <summary>Mensagem do 422 acima do teto.</summary>
    public static string TooLargeMessage(int total, int limit, ExportFormat format) =>
        $"A lista tem {total.ToString("N0", PdfTheme.PtBr)} linhas e o limite para {(format == ExportFormat.Pdf ? "PDF" : "Excel")} é {limit.ToString("N0", PdfTheme.PtBr)}. "
        + "Refine os filtros (período, situação ou busca) e exporte de novo.";

    /// <param name="load">A consulta da lista com os mesmos filtros, sem paginação, lendo no máximo o teto recebido; devolve o total do filtro.</param>
    public async Task<IResult> ExportAsync<T>(HttpContext http, string? format, ExportSpec<T> spec, IReadOnlyList<ExportFilter> filters,
        Func<int, CancellationToken, Task<ExportRows<T>>> load, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(load);
        return await ExportAsync(http, format, async (max, ct) => Result<ExportData<T>>.Success(new ExportData<T>(spec, filters, await load(max, ct))), cancellationToken);
    }

    /// <summary>
    /// Exportação em que a consulta da tela pode recusar (conta não encontrada, período inválido) e decide colunas, filtros, abas e o PDF
    /// próprio pelo resultado (EXP-02). O formato é conferido antes da consulta; o teto, a máscara, a auditoria e o download, depois.
    /// </summary>
    public async Task<IResult> ExportAsync<T>(HttpContext http, string? format, Func<int, CancellationToken, Task<Result<ExportData<T>>>> load,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(load);
        var result = await BuildAsync(format, load, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>O arquivo pronto (ou o erro de formato e de teto), já auditado.</summary>
    public async Task<Result<ExportFile>> BuildAsync<T>(string? format, ExportSpec<T> spec, IReadOnlyList<ExportFilter> filters,
        Func<int, CancellationToken, Task<ExportRows<T>>> load, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(load);
        return await BuildAsync(format, async (max, ct) => Result<ExportData<T>>.Success(new ExportData<T>(spec, filters, await load(max, ct))), cancellationToken);
    }

    /// <summary>O arquivo pronto (ou o erro de formato, da consulta e do teto), já auditado; com <see cref="ExportData{T}.Document"/>, o PDF é o próprio da tela.</summary>
    public async Task<Result<ExportFile>> BuildAsync<T>(string? format, Func<int, CancellationToken, Task<Result<ExportData<T>>>> load, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (ParseFormat(format) is not { } parsed)
        {
            return Error.Validation(InvalidFormatCode, "Escolha o formato: xlsx (Excel) ou pdf.", new Dictionary<string, string[]> { ["format"] = ["Use xlsx ou pdf."] });
        }

        var exporter = exporters.Single(e => e.Format == parsed);
        var limit = options.Value.MaxRows(parsed);
        var loaded = await load(limit, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (spec, filters, rows) = loaded.Value;
        if (rows.Total > limit || rows.Items.Count > limit)
        {
            var total = Math.Max(rows.Total, rows.Items.Count);
            return Error.Validation(TooLargeCode, TooLargeMessage(total, limit, parsed)) with { Data = new Dictionary<string, object?> { ["total"] = total, ["limit"] = limit } };
        }

        var allowed = new Dictionary<ExportSensitivity, bool>();
        foreach (var sensitivity in spec.Sensitivities)
        {
            allowed[sensitivity] = await AnyAsync(sensitivity.AnyOf, cancellationToken);
        }

        bool Allowed(ExportSensitivity s) => allowed.GetValueOrDefault(s);
        var (issuer, at) = await frames.IssuerAsync(cancellationToken);
        var table = new ExportTable(spec.Title, spec.OutputColumns(parsed, Allowed), spec.Rows(parsed, rows.Items, Allowed), filters, issuer, at) { RowStyles = spec.Styles(rows.Items) };
        table = loaded.Value.Decorate?.Invoke(table) ?? table;
        byte[] content;
        if (parsed == ExportFormat.Pdf && loaded.Value.Document is { } document)
        {
            var frame = await frames.CreateAsync(cancellationToken) with { IssuedBy = issuer, IssuedAt = at };
            content = await renderer.RenderAsync(document(frame, table), cancellationToken);
        }
        else
        {
            content = await exporter.ExportAsync(table, cancellationToken);
        }

        var recorded = filters.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToDictionary(f => f.Label, f => f.Value);
        if (allowed.Values.Any(v => v))
        {
            // EXP-05: o arquivo levou alguma coluna sensível sem máscara (CPF, salário); a auditoria registra o fato, nunca o conteúdo.
            recorded[SensitiveKey] = "true";
        }

        await audit.RecordExportAsync(spec.Entity, "list", exporter.Extension, table.Rows.Count, recorded, cancellationToken);
        return new ExportFile(content, exporter.ContentType, FileName(spec.FileBase, BusinessTime.Today(clock), exporter.Extension));
    }

    private async Task<bool> AnyAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        foreach (var code in codes)
        {
            var dot = code.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && await permissions.HasPermissionAsync(code[..dot], code[(dot + 1)..], ct))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Arquivo exportado.</summary>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

public static class ExportServiceCollectionExtensions
{
    /// <summary>Exportação de listas e modelo único de PDF (ADR 0045): exportadores xlsx e PDF, o serviço das rotas e o frame.</summary>
    public static IServiceCollection AddBemAquiExport(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<ExportOptions>(configuration.GetSection(ExportOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPdfRenderer, QuestPdfRenderer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITabularExporter, XlsxTabularExporter>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITabularExporter, PdfTabularExporter>());
        services.TryAddScoped<PdfFrameFactory>();
        services.TryAddScoped<ListExportService>();
        return services;
    }
}
