using System.Text.Json.Serialization;
using BemAqui.HR.Domain;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>De onde vem o pedido do "Meu ponto": o aparelho registrado da fábrica (quando há) e se a sessão é por PIN.</summary>
public sealed record TimeClockOrigin(Guid? DeviceId, bool Pin);

/// <summary>Marcação de hoje no "Meu ponto" (CA 2): hora, tipo e origem, sem localização.</summary>
public sealed record MyTimeEntryToday(Guid Id, DateTimeOffset At, TimeEntryType Type, string Label, TimeEntrySource Source, TimeEntryLocation Location);

/// <summary>
/// "Meu ponto" (RH-01a CA 1, 2, 4, 9 e 10): a faixa de controle interno, o primeiro nome, a hora do servidor, a jornada do dia, as marcações
/// de hoje e a próxima marcação sugerida; a geolocalização (desligada no aparelho registrado) e se falta a ciência do aviso.
/// </summary>
public sealed record MyTimeClock(
    string Banner,
    string FirstName,
    DateTimeOffset ServerNow,
    DateOnly Today,
    string Schedule,
    IReadOnlyList<MyTimeEntryToday> Entries,
    TimeEntryType Suggested,
    string SuggestedVerb,
    bool OutOfSequence,
    string? Hint,
    TimeClockGeolocation Geolocation,
    bool GeoConsentNeeded,
    string GeoConsentText,
    string GeoConsentVersion,
    bool RegisteredDevice);

/// <summary>
/// Marcar (CA 3, 4, 6 e 9): só o tipo e, quando a geolocalização está ligada, a localização do navegador. O funcionário vem da sessão e
/// o horário do servidor: <c>employeeId</c>, <c>at</c> ou qualquer outro campo no corpo responde 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TimeEntryInput(TimeEntryType Type, decimal? Latitude = null, decimal? Longitude = null, decimal? AccuracyM = null);

/// <summary>Comprovante de controle interno (CA 1 e 3), sem a localização: "Entrada registrada às 08:02 de 21/10/2026".</summary>
public sealed record TimeEntryReceipt(
    Guid Id,
    TimeEntryType Type,
    DateTimeOffset At,
    DateOnly Day,
    TimeEntrySource Source,
    TimeEntryLocation Location,
    string Title,
    string Text,
    string Message,
    bool OutOfSequence,
    string? Hint,
    bool SessionEnded);

/// <summary>Ciência do aviso da geolocalização (CA 10).</summary>
public sealed record GeoConsentResult(string TextVersion, DateTimeOffset AcceptedAt);

/// <summary>"Minhas marcações" (CA 13): as próprias, dos últimos 60 dias, com a própria localização.</summary>
public sealed record MyTimeEntry(
    Guid Id, DateTimeOffset At, DateOnly Day, TimeEntryType Type, TimeEntrySource Source, string? DeviceName, TimeEntryLocation Location, TimeEntryStatus Status,
    decimal? Latitude, decimal? Longitude, int? AccuracyM);

/// <summary>Linha da consulta do RH (CA 12), com <c>hr.view</c>: nenhuma propriedade de localização, só a marca "Sem localização".</summary>
public sealed record TimeEntryRow(
    Guid Id, Guid EmployeeId, string EmployeeName, DateOnly Day, DateTimeOffset At, TimeEntryType Type, TimeEntrySource Source, string? DeviceName, TimeEntryLocation Location, TimeEntryStatus Status);

/// <summary>Marcações do período; <c>Truncated</c> quando passou do limite de linhas (filtre por funcionário ou centro).</summary>
public sealed record TimeEntryList(DateOnly From, DateOnly To, IReadOnlyList<TimeEntryRow> Items, bool Truncated);

/// <summary>Localização de uma marcação (CA 11): só pela rota <c>/sensitive</c>, com <c>hr.view_sensitive</c>.</summary>
public sealed record TimeEntryLocationRow(Guid Id, DateTimeOffset At, TimeEntryType Type, TimeEntryLocation Location, decimal? Latitude, decimal? Longitude, int? AccuracyM);
