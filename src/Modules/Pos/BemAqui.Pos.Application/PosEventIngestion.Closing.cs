using System.Text.Json;
using BemAqui.Pos.Domain;

namespace BemAqui.Pos.Application;

/// <summary>
/// CX-02: a subida do fechamento do caixa (eventos <c>CashCountStarted</c> e <c>CashCounted</c> em <c>pos.cash</c>). Herda as conferências da
/// OFF-02 (caixa e época do principal, operador na sessão por PIN com <c>pos.create</c> no instante, ordem e janela de horário) e acrescenta: quem
/// fecha é o operador da sessão ou um supervisor (<c>sales.approve</c> no instante); uma contagem por fechamento; e nenhum esperado vem do
/// aparelho (o servidor calcula a quebra).
/// </summary>
public sealed partial class PosEventIngestion
{
    public const string CountStartedType = "CashCountStarted";
    public const string CountedType = "CashCounted";

    private async Task<Projection> ProjectCountAsync(SyncContext context, JsonElement root, string type, CashSession session, Guid operatorId, DateTimeOffset at,
        Projection projection, CancellationToken ct)
    {
        if (!context.Trusted && operatorId != session.OperatorId && !await context.HadPermissionAsync(operators, operatorId, "sales.approve", at, ct))
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Quem fechou não é o operador da sessão nem supervisor.");
        }

        if (type == CountStartedType)
        {
            var started = await closing.StartCountAsync(session.Id, at, ct);
            return started.IsSuccess ? projection : throw CashFailure(started.Error!);
        }

        if (PosSaleReplay.GuidOf(root, "countId") is not { } countId || PosSaleReplay.GuidOf(root, "depositMovementId") is not { } depositMovementId
            || PosSaleReplay.LongOf(root, "keptCents") is not { } kept || PosSaleReplay.LongOf(root, "salesCount") is not { } salesCount
            || !root.TryGetProperty("lines", out var linesElement) || linesElement.ValueKind != JsonValueKind.Array)
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Contagem sem id, fundo que fica, recolhimento, número de vendas ou linhas.");
        }

        var lines = new List<CashCountLineInput>();
        foreach (var line in linesElement.EnumerateArray())
        {
            if (PosSaleReplay.LongOf(line, "countedCents") is not { } counted)
            {
                return Hold(projection, SyncDivergenceKind.Validation, "Linha da contagem sem o valor contado.");
            }

            List<CashDenominationInput>? pieces = null;
            if (line.TryGetProperty("denominations", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                pieces = [];
                foreach (var piece in list.EnumerateArray())
                {
                    if (PosSaleReplay.LongOf(piece, "cents") is not { } cents || PosSaleReplay.LongOf(piece, "quantity") is not { } quantity || quantity is < 0 or > int.MaxValue)
                    {
                        return Hold(projection, SyncDivergenceKind.Validation, "Contagem por cédula e moeda inválida.");
                    }

                    pieces.Add(new CashDenominationInput(cents, (int)quantity));
                }
            }

            lines.Add(new CashCountLineInput(PosSaleReplay.GuidOf(line, "methodId"), counted, pieces));
        }

        var input = new CashCountInput(session.Id, countId, operatorId, at, kept, depositMovementId, (int)Math.Clamp(salesCount, -1, int.MaxValue), lines);
        var result = await closing.CountAsync(input, ct);
        if (result.IsFailure)
        {
            // Contagem repetida (recontar é do supervisor) e dados inválidos ficam na quarentena, com o motivo; nunca são descartados.
            throw result.Error!.Code == "cash_count_repeated"
                ? new ProjectionFailedException(SyncDivergenceKind.CashSession, result.Error.Message)
                : CashFailure(result.Error);
        }

        return projection;
    }

    /// <summary>A venda é de depois do "Fechar caixa", ou a sessão já foi fechada: não entra (CX-02, "nada é lançado depois do fechamento").</summary>
    private static string? AfterCountStarted(CashSession session, DateTimeOffset completedAt) => session.Status switch
    {
        CashSessionStatus.Closed or CashSessionStatus.PendingReview => "Venda de uma sessão de caixa já fechada: reabra o caixa antes de liberar.",
        CashSessionStatus.Counting or CashSessionStatus.Counted when session.CountStartedAt is { } started && completedAt > started
            => "Venda concluída depois do \"Fechar caixa\" da sessão.",
        _ => null,
    };
}
