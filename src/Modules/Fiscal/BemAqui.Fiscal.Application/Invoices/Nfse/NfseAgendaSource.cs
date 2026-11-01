using System.Globalization;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Fiscal.Domain;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>
/// "NFS-e a emitir" na Minha pauta (FIS-06 CA 1): as instalações com aceite técnico sem NFS-e, para quem tem <c>fiscal.create</c>; parada há
/// mais de 1 dia útil, a categoria sobe para "atrasado" e as linhas atrasadas vêm primeiro com a marca.
/// </summary>
public sealed class NfseAgendaSource(NfseInvoiceService nfse) : IAgendaSource
{
    public const string ToIssue = "fiscal.nfse_to_issue";
    public const string QueueLink = OutgoingInvoiceService.QueueLink + "?aba=a-emitir";

    private static readonly AgendaCategory[] Declared = [new(ToIssue, FiscalModuleInfo.Code, "fiscal.create", AgendaTier.DueSoon, "as NFS-e a emitir")];

    public string Module => FiscalModuleInfo.Code;

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Wants(ToIssue))
        {
            return [];
        }

        var queue = await nfse.ToIssueAsync(cancellationToken);
        if (queue.Count == 0)
        {
            return [];
        }

        var late = queue.Count(q => q.Late);
        var entries = queue.OrderByDescending(q => q.Late).ThenBy(q => q.AcceptedOn).Take(AgendaLimits.EntriesPerCategory)
            .Select(q => new AgendaEntry($"{q.OrderNumber} — {q.ProjectNumber}", $"{q.PartyName} · aceite há {q.DaysSinceAcceptance.ToString(CultureInfo.InvariantCulture)} dias",
                QueueLink + "&os=" + q.InstallationOrderId.ToString("D"), q.AcceptedOn, q.EstimatedAmount, q.Late ? "Atrasada" : null)).ToList();
        var title = queue.Count == 1 ? "1 instalação aceita espera a NFS-e" : $"{queue.Count.ToString(CultureInfo.InvariantCulture)} instalações aceitas esperam a NFS-e";
        var why = late > 0
            ? $"{late.ToString(CultureInfo.InvariantCulture)} há mais de 1 dia útil: o faturamento e o ISS do projeto só contam com a nota."
            : "Emita a NFS-e no aceite técnico: a receita do serviço conta pela data da nota.";
        return
        [
            new AgendaItem(ToIssue, title, why, QueueLink, queue.Min(q => q.AcceptedOn), queue.Sum(q => q.EstimatedAmount), late > 0 ? AgendaTier.Overdue : AgendaTier.DueSoon,
                queue.Count, "Emitir NFS-e", entries),
        ];
    }
}
