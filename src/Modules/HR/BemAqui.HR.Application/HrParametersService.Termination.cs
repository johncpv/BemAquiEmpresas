using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Parâmetros da rescisão (FLH-05), pré-configurados pela CLT, "A confirmar com a contadora" e com o jurídico (ADR 0042): a matriz
/// <c>hr.termination_matrix</c> (por tipo: o aviso, as férias, o 13º proporcional e a multa do FGTS), o aviso proporcional
/// <c>hr.notice_days_rule</c>, o prazo do pagamento <c>hr.termination_payment_days</c> e o banco de horas <c>hr.hour_bank_termination</c>.
/// </summary>
public sealed partial class HrParametersService
{
    public const string NoticeBaseDays = "hr.notice_days_rule.base";
    public const string NoticeDaysPerYear = "hr.notice_days_rule.per_year";
    public const string NoticeMaxDays = "hr.notice_days_rule.max";
    public const string TerminationPaymentDays = "hr.termination_payment_days";
    public const string HourBankTermination = "hr.hour_bank_termination";

    public static string MatrixKey(TerminationKind kind, string field) => $"hr.termination_matrix.{TerminationPolicy.KindKey(kind)}.{field}";

    /// <summary>As chaves que a rescisão lê (o holerite de rescisão guarda os ids delas).</summary>
    public static string[] TerminationKeys() =>
    [
        .. Enum.GetValues<TerminationKind>().SelectMany(k => new[] { MatrixKey(k, "notice"), MatrixKey(k, "vacation"), MatrixKey(k, "thirteenth"), MatrixKey(k, "fgts_fine") }),
        NoticeBaseDays, NoticeDaysPerYear, NoticeMaxDays, TerminationPaymentDays, HourBankTermination,
    ];

    private static IEnumerable<Definition> TerminationDefinitions()
    {
        foreach (var row in TerminationParameters.DefaultMatrix)
        {
            var label = TerminationPolicy.KindLabel(row.Kind);
            yield return new(MatrixKey(row.Kind, "notice"), $"{label}: aviso prévio", "O que o aviso prévio vira nesse tipo de desligamento: pago (indenizado) inteiro, pago pela metade (acordo, CLT art. 484-A), sem aviso, ou descontado quando o empregado não cumpre (pedido de demissão, art. 487 §2º).",
                HrParameterKind.Choice, (decimal)row.Notice, 1m, 4m, true, Options: [new(1m, "Aviso integral"), new(2m, "Metade do aviso (acordo)"), new(3m, "Sem aviso"), new(4m, "Desconto do aviso não cumprido")]);
            yield return new(MatrixKey(row.Kind, "vacation"), $"{label}: férias", "Que férias entram na rescisão, sempre com o 1/3: as vencidas e as proporcionais, ou só as vencidas (justa causa, Súmula 171 do TST).",
                HrParameterKind.Choice, (decimal)row.Vacation, 1m, 2m, true, Options: [new(1m, "Vencidas e proporcionais com 1/3"), new(2m, "Só as vencidas com 1/3")]);
            yield return new(MatrixKey(row.Kind, "thirteenth"), $"{label}: 13º proporcional", "Ligado: o 13º proporcional do ano entra na rescisão (com os avos do aviso indenizado), menos o que já foi pago.",
                HrParameterKind.Toggle, row.Thirteenth ? 1m : 0m, 0m, 1m, true);
            yield return new(MatrixKey(row.Kind, "fgts_fine"), $"{label}: multa do FGTS", "Percentual da multa do FGTS que a empresa recolhe sobre o saldo do extrato (40% na dispensa sem justa causa, 20% no acordo, Lei 8.036/1990, art. 18).",
                HrParameterKind.Percent, row.FgtsFinePct, 0m, 100m, true);
        }

        yield return new(NoticeBaseDays, "Aviso prévio: dias de base", "Dias do aviso prévio de quem tem menos de um ano de casa (Lei 12.506/2011: 30 dias).", HrParameterKind.Days, 30m, 1m, 90m, true);
        yield return new(NoticeDaysPerYear, "Aviso prévio: dias por ano completo", "Dias a mais no aviso prévio por ano completo de serviço na empresa, a favor do empregado (Lei 12.506/2011: 3 dias).", HrParameterKind.Days, 3m, 0m, 30m, true);
        yield return new(NoticeMaxDays, "Aviso prévio: máximo de dias", "Limite do aviso prévio com os dias por ano (Lei 12.506/2011: 90 dias).", HrParameterKind.Days, 90m, 30m, 180m, true);
        yield return new(TerminationPaymentDays, "Prazo do pagamento da rescisão", "Dias corridos depois do desligamento para pagar a rescisão (CLT art. 477 §6º: 10 dias). É o vencimento do título do funcionário e das guias do FGTS rescisório e da multa.",
            HrParameterKind.Days, 10m, 1m, 30m, true);
        yield return new(HourBankTermination, "Banco de horas na rescisão", "O saldo positivo do banco é pago como horas extras de dia útil; o negativo só é descontado quando o acordo de banco de horas permite.",
            HrParameterKind.Choice, 1m, 1m, 2m, true, Options: [new(1m, "Pagar o saldo positivo; não descontar o negativo"), new(2m, "Pagar o positivo e descontar o negativo (o acordo permite)")]);
    }

    /// <summary>Parâmetros da rescisão (FLH-05) com os ids gravados; fora dos limites ou sem gravar, o pré-configurado pela CLT.</summary>
    public async Task<(TerminationParameters Parameters, List<Guid> Ids)> TerminationParametersAsync(CancellationToken cancellationToken = default)
    {
        var keys = TerminationKeys();
        var stored = await db.HrParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToListAsync(cancellationToken);
        decimal Number(string key)
        {
            var d = Definitions.First(x => x.Key == key);
            return stored.FirstOrDefault(p => p.Key == key)?.Value is { } v && v >= d.Min && v <= d.Max && (d.Kind == HrParameterKind.Percent || decimal.Truncate(v) == v) ? v : d.Default!.Value;
        }

        var matrix = Enum.GetValues<TerminationKind>().Select(k => new TerminationMatrixRow(k, (TerminationNoticeRule)(int)Number(MatrixKey(k, "notice")),
            (TerminationVacationRule)(int)Number(MatrixKey(k, "vacation")), Number(MatrixKey(k, "thirteenth")) == 1m, Number(MatrixKey(k, "fgts_fine")))).ToList();
        var parameters = new TerminationParameters(matrix, (int)Number(NoticeBaseDays), (int)Number(NoticeDaysPerYear), (int)Number(NoticeMaxDays), (int)Number(TerminationPaymentDays),
            Number(HourBankTermination) == 2m);
        return (parameters, stored.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Id).ToList());
    }
}
