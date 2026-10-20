using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<EmployeeStatus>))]
public enum EmployeeStatus
{
    Active,
    Terminated,
}

[JsonConverter(typeof(JsonStringEnumConverter<EmployeeHistoryKind>))]
public enum EmployeeHistoryKind
{
    Salary,
    Position,
}

/// <summary>
/// Funcionário (<c>hr.employees</c>, CAD-04b). Nome, matrícula, cargo, jornada, sindicato, admissão, desligamento e situação não
/// são sensíveis; documentos, nascimento, endereço, contatos pessoais e salário são <c>[PersonalData]</c> e só saem pela rota
/// <c>/sensitive</c> e pelos comandos próprios (ADR 0016).
/// </summary>
public sealed class Employee : Entity, IAuditable, ISoftDeletable
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

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>
/// Linha do tempo do funcionário (<c>hr.employee_history</c>, CA 14 a 16): reajustes (sensíveis) e trocas de cargo, com o valor
/// anterior e o novo em JSON. Não substitui a auditoria. Os valores vão mascarados para a auditoria nos dois tipos.
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

    public string Reason { get; init; } = string.Empty;

    public Guid? UserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
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
