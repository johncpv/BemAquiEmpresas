using System.Text;

namespace BemAqui.BuildingBlocks.Attachments;

/// <summary>Tipo aceito: extensões, content type gravado e verificação pelos primeiros bytes.</summary>
public sealed record AllowedFileType(string Name, string ContentType, string[] Extensions, Func<ReadOnlySpan<byte>, bool> MatchesContent);

/// <summary>
/// Allowlist de anexos (ADR 0009): pdf, png, jpg, webp, xml, xlsx, docx, csv, dwg, dxf, zip, até 25 MB.
/// Extensão e conteúdo (magic bytes) precisam concordar; SVG, HTML e executáveis nunca entram.
/// </summary>
public static class AttachmentPolicy
{
    public const long MaxSize = 25L * 1024 * 1024;

    public static readonly IReadOnlyList<AllowedFileType> Types =
    [
        new("PDF", "application/pdf", [".pdf"], b => StartsWith(b, "%PDF-"u8)),
        new("PNG", "image/png", [".png"], b => StartsWith(b, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        new("JPEG", "image/jpeg", [".jpg", ".jpeg"], b => StartsWith(b, [0xFF, 0xD8, 0xFF])),
        new("WEBP", "image/webp", [".webp"], b => b.Length >= 12 && StartsWith(b, "RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)),
        new("XML", "application/xml", [".xml"], b => IsText(b) && TrimText(b).StartsWith('<') && !LooksLikeMarkupPage(b)),
        new("XLSX", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [".xlsx"], IsZip),
        new("DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", [".docx"], IsZip),
        new("ZIP", "application/zip", [".zip"], IsZip),
        new("CSV", "text/csv", [".csv"], b => IsText(b) && !LooksLikeMarkupPage(b)),
        new("DWG", "application/acad", [".dwg"], b => StartsWith(b, "AC10"u8)),
        new("DXF", "application/dxf", [".dxf"], b => IsText(b) && (TrimText(b).StartsWith('0') || TrimText(b).StartsWith("999", StringComparison.Ordinal))),
    ];

    /// <summary>Tipo aceito para o nome e o início do conteúdo, ou null se recusado.</summary>
    public static AllowedFileType? Match(string fileName, ReadOnlySpan<byte> head)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        foreach (var type in Types)
        {
            if (type.Extensions.Contains(extension) && type.MatchesContent(head))
            {
                return type;
            }
        }

        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) => data.StartsWith(prefix);

    private static bool IsZip(ReadOnlySpan<byte> b) => b.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]);

    private static bool IsText(ReadOnlySpan<byte> b) => b.Length > 0 && !b.Contains((byte)0);

    private static string TrimText(ReadOnlySpan<byte> b)
    {
        var text = Encoding.UTF8.GetString(b);
        return text.TrimStart('﻿', ' ', '\t', '\r', '\n');
    }

    // Conteúdo que um navegador executaria (HTML/SVG/script) é recusado mesmo com extensão de texto.
    private static bool LooksLikeMarkupPage(ReadOnlySpan<byte> b)
    {
        var text = Encoding.UTF8.GetString(b).ToLowerInvariant();
        return text.Contains("<html", StringComparison.Ordinal) || text.Contains("<svg", StringComparison.Ordinal)
            || text.Contains("<script", StringComparison.Ordinal) || text.Contains("<!doctype html", StringComparison.Ordinal);
    }
}
