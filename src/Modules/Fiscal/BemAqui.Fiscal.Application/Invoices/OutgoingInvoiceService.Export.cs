using BemAqui.BuildingBlocks.Export;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Linha do livro simples das notas de saída (EXP-05 CA 16): número, série, chave, emissão, destinatário com o documento (o mesmo do detalhe da
/// nota, que só quem tem <c>fiscal.view</c> abre), CFOP principal, produtos, ICMS, IPI, total e situação. A última é o total das autorizadas.
/// </summary>
public sealed record OutgoingBookRow(
    string? Number, int? Series, string? AccessKey, DateOnly? IssuedOn, string? Recipient, string? Document, string? Cfop, decimal? Products, decimal? Icms, decimal? Ipi,
    decimal Total, string? Status, bool IsTotal);

public sealed partial class OutgoingInvoiceService
{
    /// <summary>
    /// "Exportar" das Emitidas (EXP-05 CA 16): os mesmos filtros da lista (<see cref="Filtered"/>), na mesma ordem, até o teto, com o total do
    /// filtro. O CFOP principal é o do item de maior valor da nota. Sem XML (fica fora, CA 16).
    /// </summary>
    public async Task<(int Total, IReadOnlyList<OutgoingBookRow> Rows)> BookRowsAsync(OutgoingInvoiceQuery query, int max, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invoices = Filtered(query);
        var total = await invoices.CountAsync(cancellationToken);
        if (total > max)
        {
            return (total, []);
        }

        var rows = await invoices.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).Take(Math.Max(1, max))
            .Select(i => new
            {
                i.Id, i.Type, i.Series, i.Number, i.AccessKey, i.IssuedOn, i.CreatedAt, i.PartyId, i.RecipientName, i.RecipientDocument, i.Totals, i.TotalAmount, i.Status,
            })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var cfops = (await db.OutgoingInvoiceItems.AsNoTracking().Where(x => ids.Contains(x.InvoiceId) && x.Cfop != null)
                .Select(x => new { x.InvoiceId, x.Cfop, x.Total, x.Sequence }).ToListAsync(cancellationToken))
            .GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Total).ThenBy(x => x.Sequence).First().Cfop);
        var names = (await parties.FindManyAsync(rows.Where(r => r.PartyId != null).Select(r => r.PartyId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var book = rows.Select(r => new OutgoingBookRow(
            OutgoingBook.Number(r.Type, r.Number), r.Series, r.AccessKey, r.IssuedOn ?? DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(r.CreatedAt).DateTime),
            r.PartyId is { } pid && names.TryGetValue(pid, out var name) ? name : r.RecipientName,
            r.RecipientDocument is { Length: > 0 } doc ? BrazilianDocuments.Format(doc) : null,
            cfops.GetValueOrDefault(r.Id), r.Totals.Products, OutgoingBook.Tax(r.Totals, TaxCodes.Icms), OutgoingBook.Tax(r.Totals, TaxCodes.Ipi), r.TotalAmount,
            OutgoingBook.StatusLabel(r.Status), false)).ToList();
        book.Add(OutgoingBook.AuthorizedTotal(book, rows.Select(r => r.Status).ToList()));
        return (total, book);
    }

    /// <summary>O livro com os filtros da tela pelos rótulos dela (cliente e projeto pelo nome e número).</summary>
    public async Task<ExportData<OutgoingBookRow>> BookAsync(OutgoingInvoiceQuery query, int max, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (total, rows) = await BookRowsAsync(query, max, cancellationToken);
        var party = query.PartyId is { } partyId && await parties.FindManyAsync([partyId], cancellationToken) is [var foundParty, ..] ? foundParty : null;
        var project = query.ProjectId is { } projectId && await projects.FindManyAsync([projectId], cancellationToken) is [var foundProject, ..] ? foundProject : null;
        ExportFilter[] filters =
        [
            new("De", query.From?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)),
            new("Até", query.To?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)),
            new("Situação", query.CancellationRequested == true ? "Cancelamento pedido" : query.Status is { } s ? OutgoingBook.StatusLabel(s) : null),
            new("Tipo", query.Type switch { InvoiceType.NFe => "NF-e", InvoiceType.NFSe => "NFS-e", _ => null }),
            new("Cliente", party is null ? null : party.TradeName ?? party.LegalName),
            new("Projeto", project?.Number),
        ];
        return new ExportData<OutgoingBookRow>(OutgoingBook.Spec, filters, new ExportRows<OutgoingBookRow>(total + 1, rows))
        {
            Decorate = t => t with { Notes = ["A última linha soma só as notas autorizadas. O XML das notas fica na própria nota."] },
        };
    }
}

/// <summary>Colunas e rótulos do livro simples (EXP-05 CA 16), com os textos da tela.</summary>
public static class OutgoingBook
{
    public const string TotalLabel = "Total das autorizadas";

    public static readonly IReadOnlyDictionary<OutgoingInvoiceStatus, string> StatusLabels = new Dictionary<OutgoingInvoiceStatus, string>
    {
        [OutgoingInvoiceStatus.Draft] = "Rascunho",
        [OutgoingInvoiceStatus.Processing] = "Processando",
        [OutgoingInvoiceStatus.ContingencyPending] = "Contingência",
        [OutgoingInvoiceStatus.Authorized] = "Autorizada",
        [OutgoingInvoiceStatus.Rejected] = "Rejeitada",
        [OutgoingInvoiceStatus.Denied] = "Denegada",
        [OutgoingInvoiceStatus.Cancelled] = "Cancelada",
    };

    public static readonly ExportSpec<OutgoingBookRow> Spec = new ExportSpec<OutgoingBookRow>("OutgoingInvoice", "notas-de-saida", "Notas de saída emitidas")
        .Text("Número", r => r.Number)
        .Text("Série", r => r.Series?.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Text("Chave", r => r.AccessKey).NotInPdf()
        .Date("Emissão", r => r.IssuedOn)
        .Text("Destinatário", r => r.Recipient)
        .Text("CNPJ/CPF", r => r.Document)
        .Text("CFOP", r => r.Cfop)
        .Money("Produtos", r => r.Products)
        .Money("ICMS", r => r.Icms)
        .Money("IPI", r => r.Ipi)
        .Money("Total", r => r.Total)
        .Text("Situação", r => r.Status)
        .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    public static string StatusLabel(OutgoingInvoiceStatus status) => StatusLabels.GetValueOrDefault(status, status.ToString());

    /// <summary>Número como a tela: "NF-e" e "NFS-e" pelo tipo; sem número, o rascunho.</summary>
    public static string? Number(InvoiceType type, long? number) => number is { } n
        ? n.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : type == InvoiceType.NFSe ? "Rascunho de NFS-e" : "Rascunho de nota";

    public static decimal Tax(InvoiceTotals totals, string code)
    {
        ArgumentNullException.ThrowIfNull(totals);
        return totals.Taxes.Where(t => t.Code == code).Sum(t => t.Amount);
    }

    /// <summary>A última linha: a soma das autorizadas (produtos, ICMS, IPI e total); as outras situações não entram.</summary>
    public static OutgoingBookRow AuthorizedTotal(IReadOnlyList<OutgoingBookRow> rows, IReadOnlyList<OutgoingInvoiceStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(statuses);
        var authorized = rows.Where((_, i) => statuses[i] == OutgoingInvoiceStatus.Authorized).ToList();
        return new OutgoingBookRow(TotalLabel, null, null, null, $"{authorized.Count} {(authorized.Count == 1 ? "nota" : "notas")}", null, null,
            authorized.Sum(r => r.Products ?? 0m), authorized.Sum(r => r.Icms ?? 0m), authorized.Sum(r => r.Ipi ?? 0m), authorized.Sum(r => r.Total), null, true);
    }
}
