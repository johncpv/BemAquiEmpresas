using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<EmployeeStatus>))]
public enum EmployeeStatus
{
    Active,
    Terminated,

    /// <summary>Afastado (CAD-04c1 CA 2): só na leitura, pelo afastamento vigente; nunca gravado na coluna <c>status</c>.</summary>
    OnLeave,
}

[JsonConverter(typeof(JsonStringEnumConverter<EmployeeHistoryKind>))]
public enum EmployeeHistoryKind
{
    Salary,
    Position,

    /// <summary>Afastamento (CAD-04c1): tipo, fim previsto e motivo sensíveis (LGPD art. 11).</summary>
    Leave,
}

/// <summary>Tipos de afastamento (CAD-04c1, decisão do PO de 21/10/2026). Férias não são afastamento (ficam para a folha).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeeLeaveType>))]
public enum EmployeeLeaveType
{
    Illness,
    WorkAccident,
    Maternity,
    Paternity,
    Military,
    Other,
}

/// <summary>
/// Funcionário (<c>hr.employees</c>, CAD-04b). Nome, matrícula, cargo, jornada, sindicato, admissão, desligamento e situação não
/// são sensíveis; documentos, nascimento, endereço, contatos pessoais e salário são <c>[PersonalData]</c> e só saem pela rota
/// <c>/sensitive</c> e pelos comandos próprios (ADR 0016).
/// </summary>
public sealed class Employee : Entity, IAuditable, ISoftDeletable, IConcurrencyVersioned
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Matrícula em texto, opcional, única entre os não excluídos (CA 7).</summary>
    public string? Registration { get; set; }

    public Guid PositionId { get; set; }

    public Guid? WorkScheduleId { get; set; }

    public Guid? UnionId { get; set; }

    public DateOnly AdmissionDate { get; set; }

    public DateOnly? TerminationDate { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    [PersonalData]
    public string Cpf { get; set; } = string.Empty;

    [PersonalData]
    public DateOnly BirthDate { get; set; }

    [PersonalData]
    public decimal BaseSalary { get; set; }

    [PersonalData]
    public string? Rg { get; set; }

    [PersonalData]
    public string? Pis { get; set; }

    [PersonalData]
    public string? CtpsNumber { get; set; }

    [PersonalData]
    public string? CtpsSeries { get; set; }

    [PersonalData]
    public string? VoterId { get; set; }

    [PersonalData]
    public string? ZipCode { get; set; }

    [PersonalData]
    public string? Street { get; set; }

    [PersonalData]
    public string? Number { get; set; }

    [PersonalData]
    public string? Complement { get; set; }

    [PersonalData]
    public string? District { get; set; }

    [PersonalData]
    public string? City { get; set; }

    [PersonalData]
    public string? State { get; set; }

    [PersonalData]
    public string? Phone { get; set; }

    [PersonalData]
    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary><c>xmin</c> (ADR 0019 §2): o registro de afastamento confere a versão do funcionário (CAD-04c1).</summary>
    public uint Version { get; set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>
/// Linha do tempo do funcionário (<c>hr.employee_history</c>, CA 14 a 16): reajustes (sensíveis), trocas de cargo e afastamentos
/// (CAD-04c1). Não substitui a auditoria. Valores, motivo, tipo e fim do afastamento vão mascarados para a auditoria.
/// No afastamento, <see cref="EffectiveDate"/> é o início e <see cref="LeaveEndDate"/> o fim previsto (último dia afastado).
/// </summary>
public sealed class EmployeeHistoryEntry : Entity, IAuditable
{
    public Guid EmployeeId { get; init; }

    public EmployeeHistoryKind Kind { get; init; }

    public DateOnly EffectiveDate { get; init; }

    [PersonalData]
    public string? OldValue { get; init; }

    [PersonalData]
    public string NewValue { get; init; } = string.Empty;

    /// <summary>Motivo (até 200). No afastamento, sem CID nem diagnóstico, e nunca em log nem evento (CAD-04c1 CA 5).</summary>
    [PersonalData]
    public string Reason { get; init; } = string.Empty;

    public Guid? UserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Tipo do afastamento (só no Leave): dado de saúde quando é doença ou acidente (LGPD art. 11).</summary>
    [PersonalData]
    public EmployeeLeaveType? LeaveType { get; init; }

    /// <summary>Fim previsto do afastamento (só no Leave), inclusive: a volta prevista é o dia seguinte.</summary>
    [PersonalData]
    public DateOnly? LeaveEndDate { get; init; }

    /// <summary>Quando o <c>EmployeeLeaveStarted</c> foi para o outbox (CA 7): cada evento sai uma vez só.</summary>
    public DateTimeOffset? StartPublishedAt { get; set; }

    /// <summary>Quando o <c>EmployeeLeaveEnded</c> foi para o outbox (CA 7).</summary>
    public DateTimeOffset? EndPublishedAt { get; set; }
}

/// <summary>Regras do afastamento sem banco (CAD-04c1 CA 1 e 2).</summary>
public static class EmployeeLeaveRules
{
    public const int ReasonMaxLength = 200;

    /// <summary>Vigente quando hoje está entre o início e o fim previsto, inclusive.</summary>
    public static bool Covers(DateOnly start, DateOnly end, DateOnly day) => start <= day && day <= end;

    /// <summary>Dois afastamentos do mesmo funcionário se sobrepõem quando têm pelo menos um dia em comum.</summary>
    public static bool Overlaps(DateOnly start, DateOnly end, DateOnly otherStart, DateOnly otherEnd) => start <= otherEnd && otherStart <= end;

    /// <summary>Situação na leitura: Desligado prevalece sobre Afastado; a coluna gravada só tem Active ou Terminated.</summary>
    public static EmployeeStatus Situation(EmployeeStatus stored, bool onLeaveToday) =>
        stored == EmployeeStatus.Terminated ? EmployeeStatus.Terminated : onLeaveToday ? EmployeeStatus.OnLeave : EmployeeStatus.Active;
}

/// <summary>PIS/PASEP/NIT (CA 6): 11 dígitos, pesos 3 2 9 8 7 6 5 4 3 2; dígito = 11 − resto, e 10 ou 11 viram 0.</summary>
public static class Pis
{
    private static readonly int[] Weights = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static bool IsValid(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 11 || digits.All(c => c == digits[0]))
        {
            return false;
        }

        var sum = Weights.Select((w, i) => w * (digits[i] - '0')).Sum();
        var check = 11 - (sum % 11);
        return (check >= 10 ? 0 : check) == digits[10] - '0';
    }
}
