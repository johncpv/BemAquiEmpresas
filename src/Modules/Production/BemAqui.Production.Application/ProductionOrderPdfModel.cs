using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>
/// O que a ficha da OS de fabricação mostra (EXP-03 CA 10 a 15): cabeçalho, indicador de etapa, QR da OS, etapas com checklist e QR, materiais
/// da aba Material e os anexos listados. <b>Não existe</b> campo de custo, custo-hora, preço nem margem aqui, para nenhum usuário (CA 14,
/// conferido por teste no record).
/// </summary>
public sealed record ProductionOrderPdfModel(
    string Number, ProductionOrderStatus Status, string StatusLabel, string ProjectNumber, string ProjectName, string PartyName, string? ProductCode, string ProductDescription,
    decimal Quantity, DateOnly? PromisedDate, string? PlannedEnd, string? RoutingName, IReadOnlyList<string> Letters, int CurrentIndex, string OrderUrl, string? CancelReason,
    bool Started, IReadOnlyList<ProductionOrderPdfStep> Steps, ProductionOrderPdfMaterial Material, IReadOnlyList<ProductionOrderPdfFile> Files, DateOnly IssueDate);

/// <param name="Forecast">"dd/MM/aaaa → dd/MM/aaaa" do plano, "Sem previsão: {motivo}" ou null (concluída).</param>
/// <param name="Url">Endereço da etapa no app do operador (<c>/fab/etapa/{stepId}</c>), para o QR.</param>
public sealed record ProductionOrderPdfStep(
    string Code, string Name, string? WorkCenter, decimal StandardHours, string? Forecast, string StatusLabel, bool IsQualityStep, IReadOnlyList<string> Checklist, bool Done, string Url);

/// <param name="Note">"BOM ainda não liberada." ou "BOM em rascunho: materiais a confirmar."; null com a BOM liberada.</param>
public sealed record ProductionOrderPdfMaterial(string? Note, IReadOnlyList<ProductionOrderPdfMaterialLine> Lines);

public sealed record ProductionOrderPdfMaterialLine(string ItemCode, string ItemName, decimal Required, string Unit, decimal Reserved, decimal Issued, decimal Shortage, string? Locations);

public sealed record ProductionOrderPdfFile(string FileName, string Type, string Size, DateTimeOffset CreatedAt, bool Pending);

/// <summary>Montagem pura da ficha da OS (EXP-03), testada sem banco: marcas, textos de etapa, materiais e o limite de linhas.</summary>
public static class ProductionOrderPdf
{
    public const int MaxLines = 300;
    public const string NoBom = "BOM ainda não liberada.";
    public const string DraftBom = "BOM em rascunho: materiais a confirmar.";
    public const string NotStartedNotice = "Etapas ainda podem mudar.";

    /// <summary>Cancelada: "Cancelado" com o motivo; aberta sem etapa iniciada: só a faixa "Etapas ainda podem mudar."; demais: nada (CA 15).</summary>
    public static (PdfMark Mark, string? Notice) Mark(ProductionOrderStatus status, bool started, string? cancelReason) => status switch
    {
        ProductionOrderStatus.Cancelled => (PdfMark.Cancelled, string.IsNullOrWhiteSpace(cancelReason) ? null : "Motivo: " + cancelReason.Trim()),
        ProductionOrderStatus.Open when !started => (PdfMark.None, NotStartedNotice),
        _ => (PdfMark.None, null),
    };

    /// <summary>Linhas somadas (etapas, itens de checklist e materiais) para o limite da ADR 0022 (CA 15).</summary>
    public static int Lines(FabricationOrderDetail order, OrderMaterialInfo? material)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.Steps.Count + order.Steps.Sum(s => s.Checklist.Count) + (material?.Lines.Count ?? 0);
    }

    public static string StatusLabel(ProductionOrderStatus status) => status switch
    {
        ProductionOrderStatus.Open => "Aberta",
        ProductionOrderStatus.InProgress => "Em produção",
        ProductionOrderStatus.Paused => "Pausada",
        ProductionOrderStatus.Quality => "Aguardando qualidade",
        ProductionOrderStatus.ReadyToComplete => "Pronta para concluir",
        ProductionOrderStatus.Completed => "Concluída",
        _ => "Cancelada",
    };

    public static string StepLabel(ProductionStepStatus status) => status switch
    {
        ProductionStepStatus.Waiting => "Aguardando",
        ProductionStepStatus.Ready => "Pronta",
        ProductionStepStatus.InProgress => "Em andamento",
        ProductionStepStatus.Paused => "Pausada",
        ProductionStepStatus.Done => "Concluída",
        _ => "Reprovada",
    };

    /// <summary>A ficha a partir do detalhe da tela, da previsão do plano, do material da aba Material e dos anexos (só os limpos e os em verificação).</summary>
    /// <param name="link">Monta a URL absoluta do tenant para um caminho do app (o <c>TenantLinks</c>).</param>
    public static ProductionOrderPdfModel Build(
        FabricationOrderDetail order, OrderForecastInfo? forecast, OrderMaterialInfo? material, bool bomDraft, IReadOnlyList<AttachmentDto> files, Func<string, string> link,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(link);
        var closed = order.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled;
        var steps = order.Steps.Select(s =>
        {
            var f = forecast?.Steps.FirstOrDefault(x => x.StepId == s.Id);
            var text = s.Status == ProductionStepStatus.Done || closed || f is null
                ? null
                : f.PlannedStart is { } start && f.PlannedEnd is { } end ? $"{PdfTheme.Date(start)} → {PdfTheme.Date(end)}" : "Sem previsão: " + (f.NoForecastReason ?? "fora do plano");
            return new ProductionOrderPdfStep(s.Code, s.Name, s.WorkCenterName, s.StandardHours, text, StepLabel(s.Status), s.IsQualityStep, s.Checklist, s.Status == ProductionStepStatus.Done,
                link("/fab/etapa/" + s.Id));
        }).ToList();
        var plannedEnd = closed || forecast is null ? null : forecast.PlannedEnd is { } pe ? PdfTheme.Date(pe) : "Sem previsão: " + (forecast.NoForecastReason ?? "fora do plano");
        var materialModel = material is { BomReleased: true }
            ? new ProductionOrderPdfMaterial(null, material.Lines.Select(l => new ProductionOrderPdfMaterialLine(
                l.ItemCode, l.ItemName, l.Required, l.UnitCode, l.Reserved, l.Issued, l.Shortage, l.Locations.Count == 0 ? null : string.Join(", ", l.Locations))).ToList())
            : new ProductionOrderPdfMaterial(bomDraft ? DraftBom : NoBom, []);
        var listed = files.Where(a => a.ScanStatus is ScanStatus.Clean or ScanStatus.Pending)
            .Select(a => new ProductionOrderPdfFile(a.FileName, TypeOf(a), SizeOf(a.Size), a.CreatedAt, a.ScanStatus == ScanStatus.Pending)).ToList();
        var allDone = order.Steps.Count > 0 && order.Steps.All(s => s.Status == ProductionStepStatus.Done);
        return new ProductionOrderPdfModel(
            order.Number, order.Status, StatusLabel(order.Status), order.ProjectNumber, order.ProjectName, order.PartyName, order.ProductCode,
            order.ProductName ?? order.ItemDescription, order.Quantity, order.PromisedDate, plannedEnd, order.RoutingTemplateName, order.Steps.Select(s => s.Code).ToList(),
            allDone ? order.Steps.Count : order.CurrentStepIndex, link("/app/producao/os/" + order.Id), order.CancelReason, order.Started, steps, materialModel, listed, today);
    }

    private static string TypeOf(AttachmentDto a)
    {
        var ext = Path.GetExtension(a.FileName).TrimStart('.').ToUpperInvariant();
        return ext.Length > 0 ? ext : a.ContentType;
    }

    private static string SizeOf(long bytes) => bytes >= 1024 * 1024
        ? (bytes / (1024m * 1024m)).ToString("0.#", PdfTheme.PtBr) + " MB"
        : Math.Max(1, (int)Math.Ceiling(bytes / 1024m)).ToString(CultureInfo.InvariantCulture) + " KB";
}
