using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.HR.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>Tipo da marcação de ponto (RH-01a): entrada, saída para o intervalo, volta do intervalo e saída.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeEntryType>))]
public enum TimeEntryType
{
    In,
    BreakStart,
    BreakEnd,
    Out,
}

/// <summary>De onde veio a marcação: o app com login completo (celular, computador), o tablet por PIN ou o lançamento manual do RH (RH-01b).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeEntrySource>))]
public enum TimeEntrySource
{
    App,
    Pin,
    Manual,
}

/// <summary>Situação da marcação: válida ou desconsiderada por ajuste aprovado (RH-01b). Nunca é apagada.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeEntryStatus>))]
public enum TimeEntryStatus
{
    Valid,
    Voided,
}

/// <summary>
/// Localização da marcação (RH-01a CA 9): não pedida (geolocalização desligada ou tablet registrado), gravada ou "Sem localização"
/// (pedida e negada ou com erro). Não é dado pessoal: diz só se há localização, nunca onde.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeEntryLocation>))]
public enum TimeEntryLocation
{
    NotAsked,
    Captured,
    Missing,
}

/// <summary>Parâmetro <c>hr.time_clock_geolocation</c> (RH-01a CA 9): desligado (padrão) ou pedir a localização ao marcar.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeClockGeolocation>))]
public enum TimeClockGeolocation
{
    Off,
    Ask,
}

/// <summary>
/// Marcação de ponto (<c>hr.time_entries</c>, RH-01a): <b>controle interno de jornada</b>, não registro oficial (REP-P, Portaria 671; ADR
/// 0035 §2). A hora é sempre a do servidor; a marcação é imutável (nem a hora nem o tipo mudam) e nunca é apagada: o ajuste aprovado
/// (RH-01b) a desconsidera e cria outra. A localização (latitude, longitude e precisão) é <c>[PersonalData]</c>: mascarada na
/// auditoria, fora do log e de qualquer evento, e só sai pela rota <c>/sensitive</c>.
/// </summary>
public sealed class TimeEntry : Entity, IAuditable
{
    public Guid EmployeeId { get; init; }

    /// <summary>Instante da marcação, do relógio do servidor, em UTC.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>Dia da marcação na data local (America/Sao_Paulo).</summary>
    public DateOnly Day { get; init; }

    public TimeEntryType Type { get; init; }

    public TimeEntrySource Source { get; init; }

    public TimeEntryStatus Status { get; init; } = TimeEntryStatus.Valid;

    public Guid? RecordedByUserId { get; init; }

    /// <summary>Aparelho registrado da fábrica (só o id do Identity), quando a marcação veio dele.</summary>
    public Guid? DeviceId { get; init; }

    public TimeEntryLocation Location { get; init; } = TimeEntryLocation.NotAsked;

    /// <summary>Latitude com 4 casas (precisão reduzida, ~11 m).</summary>
    [PersonalData]
    public decimal? Latitude { get; init; }

    [PersonalData]
    public decimal? Longitude { get; init; }

    /// <summary>Precisão informada pelo navegador, em metros.</summary>
    [PersonalData]
    public int? AccuracyM { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Ciência do aviso da geolocalização (<c>hr.time_clock_geo_consents</c>, RH-01a CA 10): funcionário, quem deu, quando e a versão do
/// texto. Texto novo pede a ciência de novo.
/// </summary>
public sealed class TimeClockGeoConsent : Entity, IAuditable
{
    public Guid EmployeeId { get; init; }

    public Guid? UserId { get; init; }

    public DateTimeOffset AcceptedAt { get; init; }

    public string TextVersion { get; init; } = string.Empty;
}

/// <summary>Regras do ponto sem banco (RH-01a): sequência sugerida, duplo toque, localização reduzida, textos e a jornada do dia.</summary>
public static class TimeClockRules
{
    /// <summary>Faixa fixa das telas do ponto (CA 1).</summary>
    public const string InternalControlBanner = "Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).";

    /// <summary>Título do comprovante (CA 1): nunca "comprovante de registro de ponto".</summary>
    public const string ReceiptTitle = "Comprovante de controle interno";

    /// <summary>Aviso antes de pedir a localização (CA 10) e a versão dele: mudou o texto, mude a versão (a ciência volta a ser pedida).</summary>
    public const string GeoConsentText = "Para que serve a localização: registrar onde a marcação foi feita, como indício. Só o RH com permissão vê. Você pode negar.";

    public const string GeoConsentVersion = "2026-10-27";

    public const string DuplicateCode = "time_entry_duplicate";
    public const string OutOfSequenceHint = "Esqueceu uma marcação? Peça o ajuste ao RH.";

    /// <summary>Dois toques do mesmo tipo com menos que isto entre eles: só o primeiro vale (CA 5).</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(1);

    /// <summary>"Minhas marcações" (CA 13).</summary>
    public const int MineDays = 60;

    /// <summary>Período máximo da consulta do RH, em dias.</summary>
    public const int MaxRangeDays = 62;

    /// <summary>Próxima marcação pela última válida do dia: Entrada → Saída para o intervalo → Volta do intervalo → Saída (e de novo Entrada).</summary>
    public static TimeEntryType Suggest(TimeEntryType? last) => last switch
    {
        null => TimeEntryType.In,
        TimeEntryType.In => TimeEntryType.BreakStart,
        TimeEntryType.BreakStart => TimeEntryType.BreakEnd,
        TimeEntryType.BreakEnd => TimeEntryType.Out,
        _ => TimeEntryType.In,
    };

    /// <summary>A marcação foge da sequência sugerida (vira inconsistência no espelho, RH-01c; aceita mesmo assim).</summary>
    public static bool IsOutOfSequence(TimeEntryType? last, TimeEntryType type) => Suggest(last) != type;

    /// <summary>Duplo toque (CA 5): a anterior do mesmo tipo a menos de 1 minuto.</summary>
    public static bool IsDuplicate(DateTimeOffset? previousSameType, DateTimeOffset now) =>
        previousSameType is { } previous && now - previous < DuplicateWindow && now >= previous;

    /// <summary>Precisão reduzida (CA 9): 4 casas, arredondado para longe do zero no meio.</summary>
    public static decimal Round(decimal coordinate) => Math.Round(coordinate, 4, MidpointRounding.AwayFromZero);

    /// <summary>Localização válida (latitude −90 a 90, longitude −180 a 180, precisão de 0 a 100 km) já reduzida, ou null.</summary>
    public static (decimal Latitude, decimal Longitude, int? AccuracyM)? Normalize(decimal? latitude, decimal? longitude, decimal? accuracyM)
    {
        if (latitude is not { } lat || longitude is not { } lng || lat is < -90m or > 90m || lng is < -180m or > 180m)
        {
            return null;
        }

        int? accuracy = accuracyM is { } a && a >= 0m && a <= 100_000m ? (int)Math.Round(a, MidpointRounding.AwayFromZero) : null;
        return (Round(lat), Round(lng), accuracy);
    }

    public static string Label(TimeEntryType type) => type switch
    {
        TimeEntryType.In => "Entrada",
        TimeEntryType.BreakStart => "Saída para o intervalo",
        TimeEntryType.BreakEnd => "Volta do intervalo",
        _ => "Saída",
    };

    /// <summary>Verbo do botão: "Registrar entrada".</summary>
    public static string Verb(TimeEntryType type) => "Registrar " + Lower(Label(type));

    /// <summary>Particípio da mensagem de sucesso (ADR 0021): "Entrada registrada", "Volta do intervalo registrada".</summary>
    public static string Done(TimeEntryType type) => Label(type) + " registrada";

    /// <summary>Comprovante (CA 3): "Entrada registrada às 08:02 de 21/10/2026", na hora local.</summary>
    public static string ReceiptText(TimeEntryType type, DateTimeOffset local) =>
        $"{Done(type)} às {local.ToString("HH:mm", CultureInfo.InvariantCulture)} de {local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    /// <summary>Recusa do duplo toque (CA 5): "Você já registrou entrada às 08:02."</summary>
    public static string DuplicateMessage(TimeEntryType type, DateTimeOffset local) =>
        $"Você já registrou {Lower(Label(type))} às {local.ToString("HH:mm", CultureInfo.InvariantCulture)}.";

    /// <summary>Jornada do dia (CA 2): "08:00–17:48, intervalo 1 h"; sem o dia na jornada, "Sem jornada hoje".</summary>
    public static string ScheduleText(IReadOnlyList<ScheduleDay>? days, DateOnly day)
    {
        var weekday = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
        var today = days?.FirstOrDefault(d => d.Weekday == weekday);
        if (today is null)
        {
            return "Sem jornada hoje";
        }

        var text = $"{today.Start}–{today.End}";
        if (today.BreakMinutes <= 0)
        {
            return text + ", sem intervalo";
        }

        var hours = today.BreakMinutes / 60;
        var minutes = today.BreakMinutes % 60;
        var pause = hours > 0 && minutes == 0 ? $"{hours} h" : hours > 0 ? $"{hours} h {minutes} min" : $"{minutes} min";
        return $"{text}, intervalo {pause}";
    }

    /// <summary>Primeiro nome para "Olá, {primeiro nome}".</summary>
    public static string FirstName(string name)
    {
        var first = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "funcionário" : first;
    }

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
