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

/// <summary>
/// Registrar ou editar afastamento (CAD-04c1 CA 1, CAD-04c1b CA 1), comando próprio com <c>hr.edit</c> e <c>hr.view_sensitive</c>;
/// Doença e Acidente de trabalho exigem também <c>hr.view_health</c>. <c>ExpectedEndDate</c> é o último dia afastado;
/// <c>Version</c> é o <c>xmin</c> do funcionário (ADR 0019 §2). O motivo não leva CID nem diagnóstico.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeaveInput(AbsenceReason? Type, DateOnly? StartDate, DateOnly? ExpectedEndDate, string? Reason, uint Version);

/// <summary>Encerrar antes do previsto (CAD-04c1b CA 4): <c>ReturnDate</c> é o dia da volta, entre o início e o fim previsto, até hoje.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeaveEndInput(DateOnly? ReturnDate, uint Version);

/// <summary>Cancelar o afastamento que não começou (CAD-04c1b CA 6): cancelamento lógico, com motivo sem CID nem diagnóstico.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeaveCancelInput(string? Reason, uint Version);

/// <summary>
/// Afastamento só pela rota <c>/leaves/sensitive</c> (CA 4), com <c>no-store</c> e <c>SensitiveRead</c>. Sem <c>hr.view_health</c>,
/// o de Doença ou Acidente de trabalho vem com <c>HealthProtected</c>, sem tipo nem motivo, só com as datas (CAD-04c1b CA 8).
/// <c>Changes</c>: o rastro das edições, do encerramento antes do previsto e do cancelamento, do mais recente ao mais antigo.
/// </summary>
public sealed record LeaveDetail(
    Guid Id,
    AbsenceReason? Type,
    bool HealthProtected,
    EmployeeLeaveState State,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    DateOnly ExpectedReturnDate,
    string? Reason,
    Guid? UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<LeaveChangeDetail> Changes);

/// <summary>
/// Uma mudança do afastamento (CAD-04c1b CA 7), com o período antes e depois e, na edição, o tipo antes e depois (nulos quando
/// protegidos). <c>Reason</c> é o motivo do cancelamento.
/// </summary>
public sealed record LeaveChangeDetail(
    Guid Id,
    EmployeeLeaveChangeKind Kind,
    DateOnly OldStartDate,
    DateOnly OldExpectedEndDate,
    DateOnly NewStartDate,
    DateOnly NewExpectedEndDate,
    AbsenceReason? OldType,
    AbsenceReason? NewType,
    string? Reason,
    Guid? UserId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Funcionário para a lista e o detalhe (CA 8): nunca traz dado pessoal nem salário. <c>Status</c> é a situação de hoje
/// (<c>OnLeave</c> calculado, CAD-04c1 CA 2), sem tipo, motivo nem datas do afastamento.
/// </summary>
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
    EmployeeStatus Status,
    uint Version,
    DateTimeOffset? AnonymizedAt = null,
    string? AnonymizationRequest = null);

/// <summary>Só pela rota <c>/sensitive</c> (CA 9), com <c>no-store</c> e <c>SensitiveRead</c>.</summary>
public sealed record EmployeeSensitiveDetail(
    Guid Id, string Cpf, DateOnly BirthDate, decimal BaseSalary, string? Rg, string? Pis, string? CtpsNumber, string? CtpsSeries, string? VoterId,
    string? ZipCode, string? Street, string? Number, string? Complement, string? District, string? City, string? State, string? Phone, string? Email);

/// <summary>Criação salva, com o aviso de salário fora da faixa do cargo (CA 5), quando houver.</summary>
public sealed record EmployeeCreated(EmployeeDetail Employee, string? Warning);

/// <summary><c>WorkCenterId</c>: o centro padrão do cargo do funcionário.</summary>
public sealed record EmployeeListQuery(string? Q, EmployeeStatus? Status, Guid? PositionId, Guid? WorkCenterId = null);

/// <summary>
/// Entrada da linha do tempo (CA 16): <c>OldValue</c> e <c>NewValue</c> são o nome do cargo ou o salário. Afastamentos não entram
/// aqui: saem só por <c>/leaves/sensitive</c> (CAD-04c1 CA 4).
/// </summary>
public sealed record TimelineEntry(Guid Id, EmployeeHistoryKind Kind, DateOnly EffectiveDate, string? OldValue, string NewValue, string Reason, Guid? UserId, DateTimeOffset CreatedAt);

/// <summary>Usuário vinculado ao funcionário, ou null (CA 19).</summary>
public sealed record LinkedUserView(BemAqui.Identity.Contracts.LinkableUser? User);
