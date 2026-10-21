using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    /// <summary>Ordenações aceitas no quadro "Vencendo" (CA 8): fora delas, 422.</summary>
    public static readonly IReadOnlyList<string> FunnelSorts = ["validUntil", "-validUntil", "totalPrice", "-totalPrice", "number", "-number"];

    public const int FunnelDefaultDays = 90;
    public const int ExpiringDays = 7;
    private const int MaxFunnelDays = 3 * 366;

    private static readonly (QuoteStatus Status, string Situation)[] FunnelStages =
        [(QuoteStatus.Draft, "rascunho"), (QuoteStatus.Sent, "enviado"), (QuoteStatus.Approved, "aprovado"), (QuoteStatus.Rejected, "perdido"), (QuoteStatus.Expired, "expirado")];

    /// <summary>
    /// Funil (COM-08): revisão corrente de cada número, pelo período da criação da revisão 0 e pelo autor dela (CA 1 a 4);
    /// "Vencendo" são os enviados que vencem em até 7 dias, do autor escolhido (CA 6).
    /// </summary>
    public async Task<Result<QuoteFunnel>> FunnelAsync(DateOnly? from, DateOnly? to, Guid? authorId, string? sort, CancellationToken cancellationToken = default)
    {
        sort ??= "validUntil";
        if (!FunnelSorts.Contains(sort))
        {
            return Invalid("sort", "Ordene por validade, preço ou número.");
        }

        var today = BusinessTime.Today(clock);
        var end = to ?? today;
        var start = from ?? end.AddDays(-FunnelDefaultDays);
        if (start > end)
        {
            return Invalid("from", "A data inicial precisa ser antes da final.");
        }

        if (end.DayNumber - start.DayNumber > MaxFunnelDays)
        {
            return Invalid("from", "Escolha um período de até 3 anos.");
        }

        var current = InPeriod(db.Quotes.AsNoTracking().Where(x => x.IsCurrent), start, end, authorId);
        var byStatus = await current.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(x => x.TotalPrice) }).ToListAsync(cancellationToken);
        var stages = FunnelStages.Select(s => byStatus.FirstOrDefault(b => b.Status == s.Status) is { } b
            ? new FunnelStage(s.Status, s.Situation, b.Count, b.Amount)
            : new FunnelStage(s.Status, s.Situation, 0, 0)).ToList();
        var stage = stages.ToDictionary(s => s.Status);
        var conversion = Conversion(stage[QuoteStatus.Approved], stage[QuoteStatus.Rejected], stage[QuoteStatus.Expired]);

        var lost = current.Where(x => x.Status == QuoteStatus.Rejected);
        var reasons = await lost.GroupBy(x => x.LostReason).Select(g => new { Reason = g.Key, Count = g.Count(), Amount = g.Sum(x => x.TotalPrice) }).ToListAsync(cancellationToken);
        var lostCount = reasons.Sum(r => r.Count);
        var lossReasons = reasons.GroupBy(r => r.Reason ?? QuoteLostReason.Other)
            .Select(g => new FunnelLossReason(g.Key, g.Sum(r => r.Count), g.Sum(r => r.Amount), Pct(g.Sum(r => r.Count), lostCount)))
            .OrderByDescending(r => r.Count).ThenByDescending(r => r.Amount).ToList();
        var others = await lost.Where(x => x.LostReason == QuoteLostReason.Other).OrderByDescending(x => x.LostAt).Take(50)
            .Select(x => new FunnelOtherLoss(x.Id, x.Number, x.Revision, x.LostDetail, x.TotalPrice)).ToListAsync(cancellationToken);

        return new QuoteFunnel(start, end, authorId, stages, conversion, lossReasons, others, await ExpiringAsync(today, authorId, sort, cancellationToken), await AuthorsAsync(cancellationToken));
    }

    /// <summary>Taxa de conversão (CA 2): aprovados ÷ (aprovados + perdidos + expirados), com uma casa; "—" (nulo) sem decididos.</summary>
    public static FunnelConversion Conversion(FunnelStage approved, FunnelStage lost, FunnelStage expired)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(lost);
        ArgumentNullException.ThrowIfNull(expired);
        var decided = approved.Count + lost.Count + expired.Count;
        var decidedAmount = approved.Amount + lost.Amount + expired.Amount;
        return new FunnelConversion(
            decided == 0 ? null : Math.Round(approved.Count * 100m / decided, 1, MidpointRounding.AwayFromZero),
            decidedAmount == 0 ? null : Math.Round(approved.Amount * 100m / decidedAmount, 1, MidpointRounding.AwayFromZero),
            decided);
    }

    /// <summary>
    /// Só os números cuja revisão 0 nasceu no período (dia de negócio, America/Sao_Paulo) e, com autor, que ele criou. Serve ao
    /// funil e ao drill-down da lista (CA 5).
    /// </summary>
    private IQueryable<Quote> InPeriod(IQueryable<Quote> quotes, DateOnly? from, DateOnly? to, Guid? authorId)
    {
        if (from is null && to is null && authorId is null)
        {
            return quotes;
        }

        var origins = db.Quotes.AsNoTracking().Where(o => o.Revision == 0);
        if (from is { } start)
        {
            var startUtc = StartOfDayUtc(start);
            origins = origins.Where(o => o.CreatedAt >= startUtc);
        }

        if (to is { } end)
        {
            var endUtc = StartOfDayUtc(end.AddDays(1));
            origins = origins.Where(o => o.CreatedAt < endUtc);
        }

        if (authorId is { } author)
        {
            origins = origins.Where(o => o.CreatedBy == author);
        }

        return quotes.Where(x => origins.Any(o => o.Number == x.Number));
    }

    private async Task<List<FunnelExpiring>> ExpiringAsync(DateOnly today, Guid? authorId, string sort, CancellationToken ct)
    {
        var limit = today.AddDays(ExpiringDays);
        var sent = InPeriod(
            db.Quotes.AsNoTracking().Where(x => x.IsCurrent && x.Status == QuoteStatus.Sent && x.ValidUntil != null && x.ValidUntil >= today && x.ValidUntil <= limit), null, null, authorId);
        sent = sort switch
        {
            "-validUntil" => sent.OrderByDescending(x => x.ValidUntil).ThenBy(x => x.Number),
            "totalPrice" => sent.OrderBy(x => x.TotalPrice).ThenBy(x => x.Number),
            "-totalPrice" => sent.OrderByDescending(x => x.TotalPrice).ThenBy(x => x.Number),
            "number" => sent.OrderBy(x => x.Number),
            "-number" => sent.OrderByDescending(x => x.Number),
            _ => sent.OrderBy(x => x.ValidUntil).ThenBy(x => x.Number),
        };
        var rows = await sent.Take(50).Select(x => new { x.Id, x.Number, x.Revision, x.PartyId, x.TotalPrice, ValidUntil = x.ValidUntil!.Value }).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.PartyId), ct);
        return rows.Select(r => new FunnelExpiring(
            r.Id, r.Number, r.Revision, r.PartyId, names.GetValueOrDefault(r.PartyId, string.Empty), r.TotalPrice, r.ValidUntil, r.ValidUntil.DayNumber - today.DayNumber)).ToList();
    }

    /// <summary>Quem já criou orçamento (CA 4), para o filtro por autor.</summary>
    private async Task<List<FunnelAuthor>> AuthorsAsync(CancellationToken ct)
    {
        var ids = await db.Quotes.AsNoTracking().Where(x => x.Revision == 0 && x.CreatedBy != null).Select(x => x.CreatedBy!.Value).Distinct().Take(200).ToListAsync(ct);
        var authors = new List<FunnelAuthor>(ids.Count);
        foreach (var id in ids)
        {
            authors.Add(new FunnelAuthor(id, await users.DisplayNameAsync(id, ct) ?? "Usuário removido"));
        }

        return authors.OrderBy(a => a.Name, StringComparer.CurrentCulture).ToList();
    }

    private static decimal Pct(int part, int total) => total == 0 ? 0 : Math.Round(part * 100m / total, 1, MidpointRounding.AwayFromZero);

    private static DateTimeOffset StartOfDayUtc(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BusinessTime.Zone), TimeSpan.Zero);
}
