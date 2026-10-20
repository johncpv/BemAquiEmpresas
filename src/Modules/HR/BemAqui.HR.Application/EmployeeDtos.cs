using System.Text.Json.Serialization;
using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>Criação em página única (CA 1 a 4): só o obrigatório. CPF e salário são sensíveis: exige <c>hr.view_sensitive</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeCreateInput(string Name, string Cpf, DateOnly BirthDate, DateOnly AdmissionDate, Guid PositionId, Guid? WorkScheduleId, decimal BaseSalary, string? Registration);

/// <summary>Dados gerais (CA 7 e 10): campo sensível no corpo responde 400 e nunca anula o gravado.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeGeneralInput(string Name, string? Registration, Guid? WorkScheduleId, Guid? UnionId, DateOnly AdmissionDate);

/// <summary>Dados pessoais (CA 6), comando próprio com <c>hr.view_sensitive</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeePersonalInput(
    string Cpf, DateOnly BirthDate, string? Rg, string? Pis, string? CtpsNumber, string? CtpsSeries, string? VoterId,
    string? ZipCode, string? Street, string? Number, string? Complement, string? District, string? City, string? State, string? Phone, string? Email);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalaryChangeInput(decimal NewSalary, DateOnly? EffectiveDate, string? Reason);

/// <summary>Troca de cargo (CA 15); <c>NewSalary</c> (o de referência sugerido) só com <c>hr.view_sensitive</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PositionChangeInput(Guid PositionId, DateOnly? EffectiveDate, string? Reason, decimal? NewSalary);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TerminationInput(DateOnly TerminationDate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UserLinkInput(Guid UserId);

/// <summary>Funcionário para a lista e o detalhe (CA 8): nunca traz dado pessoal nem salário.</summary>
public sealed record EmployeeDetail(
    Guid Id,
    string Name,
    string? Registration,
    Guid PositionId,
    string PositionName,
    Guid? WorkCenterId,
    string? WorkCenterName,
    Guid? WorkScheduleId,
    Guid? UnionId,
    DateOnly AdmissionDate,
    DateOnly? TerminationDate,
    EmployeeStatus Status);

/// <summary>Só pela rota <c>/sensitive</c> (CA 9), com <c>no-store</c> e <c>SensitiveRead</c>.</summary>
public sealed record EmployeeSensitiveDetail(
    Guid Id, string Cpf, DateOnly BirthDate, decimal BaseSalary, string? Rg, string? Pis, string? CtpsNumber, string? CtpsSeries, string? VoterId,
    string? ZipCode, string? Street, string? Number, string? Complement, string? District, string? City, string? State, string? Phone, string? Email);

/// <summary>Criação salva, com o aviso de salário fora da faixa do cargo (CA 5), quando houver.</summary>
public sealed record EmployeeCreated(EmployeeDetail Employee, string? Warning);

/// <summary><c>WorkCenterId</c>: o centro padrão do cargo do funcionário.</summary>
public sealed record EmployeeListQuery(string? Q, EmployeeStatus? Status, Guid? PositionId, Guid? WorkCenterId = null);

/// <summary>Entrada da linha do tempo (CA 16): <c>OldValue</c> e <c>NewValue</c> são o nome do cargo ou o salário.</summary>
public sealed record TimelineEntry(Guid Id, EmployeeHistoryKind Kind, DateOnly EffectiveDate, string? OldValue, string NewValue, string Reason, Guid? UserId, DateTimeOffset CreatedAt);

/// <summary>Usuário vinculado ao funcionário, ou null (CA 19).</summary>
public sealed record LinkedUserView(BemAqui.Identity.Contracts.LinkableUser? User);
