using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

/// <summary>Fonte da receita do DRE (FIN-08 CA 4): pelos títulos (provisório, esta story) ou pela nota autorizada (FIS-03a e FIS-06).</summary>
public enum RevenueBasis
{
    Title,
    Invoice,
}

/// <summary>Receita reconhecida numa conta, num projeto (opcional) e num dia.</summary>
public sealed record RevenueAmount(Guid AccountId, Guid? ProjectId, DateOnly Date, decimal Amount);

/// <summary>
/// Sinal (CA 5): emitido em <see cref="IssueDate"/> e reconhecido como receita em <see cref="RecognizedOn"/> (nulo: ainda em
/// "Adiantamentos de clientes"). <see cref="Basis"/> diz qual regra o calculou (o saldo do mês usa a regra que vale no mês).
/// </summary>
public sealed record RevenueAdvance(Guid ReceivableId, string Number, Guid PartyId, Guid AccountId, Guid? ProjectId, DateOnly IssueDate, DateOnly? RecognizedOn, decimal Amount, string? Note, RevenueBasis Basis);

/// <summary>Resultado de uma regra: a receita do intervalo (inclusive o sinal reconhecido nele) e os sinais que afetam os adiantamentos.</summary>
public sealed record RevenueRecognitionResult(IReadOnlyList<RevenueAmount> Amounts, IReadOnlyList<RevenueAdvance> Advances);

/// <summary>Documento que compõe a receita de uma célula do DRE (drill-down, CA 13).</summary>
public sealed record RevenueDocument(string Kind, Guid DocumentId, string Number, Guid PartyId, Guid AccountId, Guid? ProjectId, DateOnly Date, decimal Amount, string? Note, string Link);

/// <summary>
/// Ponto de troca da regra da receita (FIN-08 CA 4): esta story entrega a implementação pelo título
/// (<see cref="TitleRevenueRecognition"/>); a FIS-03a registra a implementação pela nota, com o mesmo teste de contrato. O
/// <see cref="RevenueRecognitionRouter"/> escolhe a regra pela data de corte (<see cref="IRevenueCutover"/>).
/// </summary>
public interface IRevenueRecognition
{
    RevenueBasis Basis { get; }

    /// <summary>Receita com data de <paramref name="from"/> a <paramref name="to"/> (inclusive), por conta, projeto e dia; <paramref name="projectIds"/> nulo = todos.</summary>
    Task<RevenueRecognitionResult> RecognizeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default);

    /// <summary>Documentos da receita nas contas e no intervalo: a soma é igual à de <see cref="RecognizeAsync"/> nas mesmas contas.</summary>
    Task<IReadOnlyList<RevenueDocument>> DocumentsAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Data de corte fiscal (<c>fiscal.project_tax_cutover_on</c>, que a GOL-01b preenche na primeira nota em produção). Sem corte,
/// toda a receita é provisória (pelos títulos).
/// </summary>
public interface IRevenueCutover
{
    Task<DateOnly?> CutoverOnAsync(CancellationToken cancellationToken = default);
}

/// <summary>Até a FIS-03a/GOL-01b: sem notas reais, sem data de corte.</summary>
public sealed class NoRevenueCutover : IRevenueCutover
{
    public Task<DateOnly?> CutoverOnAsync(CancellationToken cancellationToken = default) => Task.FromResult<DateOnly?>(null);
}

/// <summary>
/// Primeiro romaneio expedido (<c>Shipped</c>) de cada projeto (CA 5): a PRD-06b liga o <c>IShipments</c> aqui. Até lá, nenhum
/// projeto tem romaneio e o sinal é reconhecido com o marco seguinte.
/// </summary>
public interface IProjectShipments
{
    Task<IReadOnlyDictionary<Guid, DateOnly>> FirstShippedOnAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);
}

public sealed class NoProjectShipments : IProjectShipments
{
    public Task<IReadOnlyDictionary<Guid, DateOnly>> FirstShippedOnAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateOnly>>(new Dictionary<Guid, DateOnly>());
}

/// <summary>Receita do intervalo já pela regra de cada mês, com a data de corte efetiva (nula: tudo provisório).</summary>
public sealed record RoutedRevenue(IReadOnlyList<RevenueAmount> Amounts, IReadOnlyList<RevenueAdvance> Advances, DateOnly? EffectiveCutover)
{
    /// <summary>"Provisório" (CA 3 e 4): o mês começa antes da data de corte, ou ainda não há regra pela nota.</summary>
    public bool IsProvisional(DateOnly month) => RevenueRecognitionRouter.IsProvisional(month, EffectiveCutover);

    /// <summary>Saldo de "Adiantamentos de clientes" no fim do mês, pela regra que vale no mês.</summary>
    public decimal AdvanceBalance(DateOnly month)
    {
        var end = DreBuilder.MonthOf(month).AddMonths(1).AddDays(-1);
        var basis = IsProvisional(month) ? RevenueBasis.Title : RevenueBasis.Invoice;
        return DownPaymentRule.BalanceAt(end, Advances.Where(a => a.Basis == basis).Select(a => (a.IssueDate, a.RecognizedOn, a.Amount)));
    }
}

/// <summary>
/// Escolhe a regra da receita por período (CA 4): antes da data de corte, pelo título (provisório); da data de corte em diante,
/// pela nota. Sem implementação pela nota registrada, tudo continua pelo título.
/// </summary>
public sealed class RevenueRecognitionRouter(IEnumerable<IRevenueRecognition> recognitions, IRevenueCutover cutover)
{
    public static bool IsProvisional(DateOnly month, DateOnly? effectiveCutover) => effectiveCutover is not { } cut || DreBuilder.MonthOf(month) < cut;

    public async Task<RoutedRevenue> RecognizeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default)
    {
        var (title, invoice, cut) = await ResolveAsync(cancellationToken);
        var amounts = new List<RevenueAmount>();
        var advances = new List<RevenueAdvance>();
        foreach (var (rule, start, end) in Parts(title, invoice, cut, from, to))
        {
            var part = await rule.RecognizeAsync(start, end, projectIds, cancellationToken);
            amounts.AddRange(part.Amounts);
            advances.AddRange(part.Advances);
        }

        return new RoutedRevenue(amounts, advances, cut);
    }

    public async Task<IReadOnlyList<RevenueDocument>> DocumentsAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var (title, invoice, cut) = await ResolveAsync(cancellationToken);
        var documents = new List<RevenueDocument>();
        foreach (var (rule, start, end) in Parts(title, invoice, cut, from, to))
        {
            documents.AddRange(await rule.DocumentsAsync(accountIds, start, end, cancellationToken));
        }

        return documents;
    }

    /// <summary>Data de corte efetiva: nula sem implementação pela nota (tudo pelo título).</summary>
    public async Task<DateOnly?> EffectiveCutoverAsync(CancellationToken cancellationToken = default) => (await ResolveAsync(cancellationToken)).Cut;

    private async Task<(IRevenueRecognition Title, IRevenueRecognition? Invoice, DateOnly? Cut)> ResolveAsync(CancellationToken ct)
    {
        var all = recognitions.ToList();
        var title = all.FirstOrDefault(r => r.Basis == RevenueBasis.Title) ?? throw new InvalidOperationException("A regra da receita pelo título não está registrada.");
        var invoice = all.FirstOrDefault(r => r.Basis == RevenueBasis.Invoice);
        var cut = invoice is null ? null : await cutover.CutoverOnAsync(ct);
        return (title, cut is null ? null : invoice, cut);
    }

    private static IEnumerable<(IRevenueRecognition Rule, DateOnly From, DateOnly To)> Parts(IRevenueRecognition title, IRevenueRecognition? invoice, DateOnly? cut, DateOnly from, DateOnly to)
    {
        if (invoice is null || cut is not { } c || c > to)
        {
            yield return (title, from, to);
            yield break;
        }

        if (c > from)
        {
            yield return (title, from, c.AddDays(-1));
        }

        yield return (invoice, c > from ? c : from, to);
    }
}
