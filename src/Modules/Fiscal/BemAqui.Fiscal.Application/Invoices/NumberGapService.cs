using System.Globalization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// "Números a inutilizar" (FIS-03b CA 10 e 11): os números gastos por rejeição, em faixas contínuas por série; "Inutilizar números" com
/// <c>fiscal.approve</c> e o 2FA de novo (no endpoint, D6), recusando antes do Emissor o número com nota autorizada, cancelada, denegada ou
/// em processamento. Homologada, cada número fica "Inutilizado" com o evento e o protocolo. A faixa vai inteira ou nada.
/// </summary>
public sealed class NumberGapService(IFiscalDb db, FiscalEmitterAccess access, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock)
{
    public const int OldAfterDays = 30;
    public const int MaxRange = 1000;
    public const string Link = "/app/fiscal/saida?aba=emitidas&situacao=a-inutilizar";
    public const string InvalidatedMessage = "Números inutilizados";
    public const string SentMessage = "Inutilização enviada. A SEFAZ não respondeu ainda; confira de novo em alguns minutos.";

    public async Task<NumberGapList> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.NumberGaps.AsNoTracking().Where(g => g.Reason == NumberGapReasons.Rejected && g.Status == NumberGapStatus.Pending)
            .OrderBy(g => g.Series).ThenBy(g => g.Number).Take(5_000)
            .Select(g => new NumberGapRow(g.Id, g.Series, g.Number, g.Reason, g.CreatedAt, g.InvoiceId)).ToListAsync(cancellationToken);
        var old = clock.GetUtcNow().AddDays(-OldAfterDays);
        var ranges = FiscalEventRules.Ranges(rows.Select(r => (r.Series, r.Number))).Select(range =>
        {
            var numbers = rows.Where(r => r.Series == range.Series && r.Number >= range.First && r.Number <= range.Last).ToList();
            var oldest = numbers.Min(n => n.CreatedAt);
            return new NumberGapRangeView(range.Series, range.First, range.Last, range.Count, range.Label, oldest, oldest <= old, numbers);
        }).ToList();
        return new NumberGapList(ranges, rows.Count, await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "approve", cancellationToken));
    }

    public async Task<Result<InvalidationResultView>> InvalidateAsync(InvalidateNumbersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (text, invalid) = FiscalEventRules.Justification(request.Justification, "a justificativa");
        if (invalid is not null)
        {
            return Error.Validation(FiscalErrorCodes.Validation, invalid, new Dictionary<string, string[]> { ["justification"] = [invalid] });
        }

        if (request.Series is < 0 or > 999 || request.FirstNumber <= 0 || request.LastNumber < request.FirstNumber || request.LastNumber - request.FirstNumber + 1 > MaxRange)
        {
            return Error.Validation(FiscalErrorCodes.Validation, $"Escolha uma faixa da mesma série, do menor para o maior número, com até {MaxRange} números.",
                new Dictionary<string, string[]> { ["lastNumber"] = ["Faixa inválida."] });
        }

        // CA 11: número com nota autorizada, cancelada, denegada ou em andamento nunca é inutilizado (recusa antes do Emissor).
        var used = await db.OutgoingInvoices.AsNoTracking()
            .Where(i => i.Series == request.Series && i.Number != null && i.Number >= request.FirstNumber && i.Number <= request.LastNumber)
            .Select(i => new { Number = i.Number!.Value, i.Status }).ToListAsync(cancellationToken);
        if (used.Select(u => (u.Number, Situation: FiscalEventRules.BlockingSituation(u.Status))).Where(u => u.Situation != null).OrderBy(u => u.Number).FirstOrDefault() is { Situation: { } situation } blocked)
        {
            return Error.Conflict(FiscalErrorCodes.NumberNotInvalidable, FiscalEventRules.Blocked(blocked.Number, situation));
        }

        var gaps = await db.NumberGaps.Where(g => g.Series == request.Series && g.Number >= request.FirstNumber && g.Number <= request.LastNumber).OrderBy(g => g.Number).ToListAsync(cancellationToken);
        for (var n = request.FirstNumber; n <= request.LastNumber; n++)
        {
            var gap = gaps.FirstOrDefault(g => g.Number == n);
            if (gap is null || !gap.ToInvalidate)
            {
                db.ChangeTracker.Clear();
                return Error.Conflict(FiscalErrorCodes.NumberNotInvalidable, $"O número {n.ToString(CultureInfo.InvariantCulture)} não está em Números a inutilizar.");
            }
        }

        var integration = await db.FiscalIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        if (emitter.IsFailure || integration?.IssuerDocument is not { } issuer)
        {
            db.ChangeTracker.Clear();
            return emitter.IsFailure ? emitter.Error! : Error.Conflict(FiscalErrorCodes.NotConfigured, FiscalEmitterAccess.InactiveMessage);
        }

        var now = clock.GetUtcNow();
        var sentIds = gaps.Where(g => g.InvalidationEventId != null).Select(g => g.InvalidationEventId!.Value).Distinct().ToList();
        var sent = sentIds.Count == 0 ? new Dictionary<Guid, DateTimeOffset>()
            : await db.FiscalEvents.AsNoTracking().Where(e => sentIds.Contains(e.Id) && e.Status == FiscalEventStatus.Requested).ToDictionaryAsync(e => e.Id, e => e.RequestedAt, cancellationToken);
        var evt = new FiscalEvent
        {
            Type = FiscalEventType.Invalidation, Text = text!, Series = request.Series, FirstNumber = request.FirstNumber, LastNumber = request.LastNumber, RequestedBy = currentUser.UserId,
            RequestedAt = now, NotifyCustomer = false,
        };
        foreach (var gap in gaps)
        {
            if (gap.BeginInvalidation(evt.Id, gap.InvalidationEventId is { } previous && sent.TryGetValue(previous, out var at) ? at : null, now) is { } refused)
            {
                db.ChangeTracker.Clear();
                return refused;
            }
        }

        db.FiscalEvents.Add(evt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Error.Conflict("concurrency_conflict", "Os números mudaram desde que você abriu (outra inutilização já foi enviada). Recarregue.");
        }

        var result = await emitter.Value.InvalidateRangeAsync(new NumberRangeInvalidation(issuer, request.Series, request.FirstNumber, request.LastNumber, text!), cancellationToken);
        string message;
        if (result.IsSuccess && result.Value.Accepted)
        {
            evt.Register(result.Value.Protocol, null, now);
            foreach (var gap in gaps)
            {
                gap.Invalidate(evt.Id, now);
            }

            message = InvalidatedMessage;
        }
        else if (result.IsSuccess || NfeSubmissionService.Classify(result.Error!) == SubmissionOutcome.NotCreated)
        {
            var why = result.IsSuccess ? result.Value.Reason ?? "sem motivo informado" : result.Error!.Message;
            evt.Reject(result.IsSuccess ? result.Value.Code : null, why, now);
            foreach (var gap in gaps)
            {
                gap.InvalidationRefused(evt.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            return Error.Conflict(FiscalErrorCodes.EventRejected, "A SEFAZ recusou a inutilização: " + why);
        }
        else
        {
            message = SentMessage;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new InvalidationResultView(evt.Status, message, request.Series, request.FirstNumber, request.LastNumber, evt.Protocol, evt.Id);
    }
}
