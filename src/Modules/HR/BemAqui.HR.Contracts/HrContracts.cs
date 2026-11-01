using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Contracts;

/// <summary>Dia da jornada (CAD-04a CA 15): dia da semana ISO (1 = segunda … 7 = domingo), entrada e saída "HH:mm" e intervalo.</summary>
public sealed record ScheduleDay(int Weekday, string Start, string End, int BreakMinutes);

/// <summary>
/// Custo-hora para Production (PRD-04) e Commercial (COM-01), só por cargo (CA 8): não existe custo por funcionário e o
/// contrato não exige permissão de RH do usuário. Null se o cargo não existe.
/// </summary>
public interface IHourlyCost
{
    Task<decimal?> ForPositionAsync(Guid positionId, CancellationToken cancellationToken = default);
}

/// <summary>Custo-hora de um cargo mudou (CA 6), sem valores; sem consumidor nesta fase.</summary>
public sealed record HourlyCostChanged(Guid PositionId) : IIntegrationEvent;

/// <summary>Quem usa um cargo (CAD-04b): com funcionário ativo, o cargo não é excluído, só inativado (CA 9).</summary>
public interface IPositionUsageProvider
{
    Task<bool> HasEmployeesAsync(Guid positionId, CancellationToken cancellationToken = default);
}

/// <summary>Quem usa um sindicato (CAD-04b): em uso, só inativa (CA 17).</summary>
public interface IUnionUsageProvider
{
    Task<bool> InUseAsync(Guid unionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Quem guarda o id de um funcionário fora do RH (PAT-01a CA 14: o responsável pelo bem): em uso, o funcionário não é excluído, só
/// desligado. Cada módulo implementa e registra no DI.
/// </summary>
public interface IEmployeeUsageProvider
{
    /// <summary>Descrição curta do uso ("bens do patrimônio") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Situação do funcionário para os outros módulos (CAD-04c1 CA 3): <c>OnLeave</c> é calculado na leitura pelo afastamento
/// vigente (hoje em São Paulo) e <c>Terminated</c> prevalece. Sem tipo, motivo nem datas do afastamento.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeeSituation>))]
public enum EmployeeSituation
{
    Active,
    OnLeave,
    Terminated,
}

/// <summary>
/// Funcionário para Production e Assets (CAD-04b CA 21): só o que não é sensível. O custo-hora vem de <see cref="IHourlyCost"/>,
/// por cargo. <c>IsActive</c>: não desligado nem excluído (o afastado continua ativo); <c>Status</c>: a situação de hoje, pela qual
/// o apontamento recusa afastado ou desligado (PRD-03a).
/// </summary>
public sealed record EmployeeSummary(Guid Id, string Name, string? Registration, Guid PositionId, string PositionName, Guid? WorkCenterId, bool IsActive, EmployeeSituation Status);

public interface IEmployees
{
    Task<IReadOnlyList<EmployeeSummary>> ActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Data de nascimento, só para o Identity recusar PIN óbvio (PRD-03b CA 12); nunca exibida nem gravada fora do RH.
    /// </summary>
    Task<DateOnly?> BirthDateAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Situação da conta para pagamento (CAD-04c2 CA 7, ADR 0036), sem nenhum dado bancário: para a folha (RH-02c) tirar a conta em
    /// quarentena do pagamento automático e mostrar "Conta alterada" ou "Novo na folha" no fechamento. Só os funcionários com conta.
    /// </summary>
    Task<IReadOnlyList<EmployeeBankAccountStatus>> BankAccountStatusAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Conta para pagamento do funcionário sem dado bancário (CAD-04c2 CA 7): <c>IsFirstAccount</c> é a primeira conta (sem quarentena,
/// "Novo na folha"); <c>ChangedAt</c> é a data do registro ou da troca; <c>QuarantineUntil</c> é o fim da quarentena da troca;
/// <c>InQuarantine</c> é hoje, sem revisão; <c>Version</c> sobe a cada troca (a RH-02c confere no pagamento).
/// </summary>
public sealed record EmployeeBankAccountStatus(
    Guid EmployeeId,
    bool IsFirstAccount,
    DateTimeOffset ChangedAt,
    DateTimeOffset? QuarantineUntil,
    bool InQuarantine,
    DateTimeOffset? ReviewedAt,
    bool SelfReviewed,
    int Version);

/// <summary>
/// Conta de salário para o Finance (RH-02c CA 8 a 10, ADR 0036 §3): o resumo sem dado bancário em claro (para o retrato da OP e o
/// diálogo de aprovação) e os dados para pagar, lidos só no "Pagar" da OP, com <c>finance.pay</c> no request e <c>SensitiveRead</c>.
/// </summary>
public interface IEmployeeBankAccounts
{
    /// <summary>Resumo de cada funcionário: matrícula, nome, a versão da conta, o final mascarado ("****-12") e as marcas de quarentena; sem conta, <c>Version</c> 0.</summary>
    Task<IReadOnlyList<EmployeeBankAccountSummary>> SummaryAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dados para o pagamento da OP <paramref name="paymentOrderId"/>: 403 sem <c>finance.pay</c>; 409 <c>payee_account_changed</c> se a
    /// versão não é a <paramref name="expectedVersion"/> do retrato; 422 <c>payee_account_in_quarantine</c> com a conta em quarentena. Grava
    /// <c>SensitiveRead</c> com o número da OP, sem o dado lido.
    /// </summary>
    Task<Result<EmployeeBankAccountForPayment>> GetForPaymentAsync(Guid employeeId, int expectedVersion, Guid paymentOrderId, string paymentOrderNumber, CancellationToken cancellationToken = default);
}

/// <summary>Conta de salário sem dado bancário em claro: <c>AccountTail</c> é o final mascarado da conta ou o tipo da chave Pix.</summary>
public sealed record EmployeeBankAccountSummary(
    Guid EmployeeId, string Name, string? Registration, int Version, string? AccountTail, bool NewOnPayroll, bool InQuarantine, DateTimeOffset? QuarantineUntil, DateTimeOffset? ReviewedAt);

/// <summary>Para onde vai o salário (só no "Pagar"): banco, agência, conta, tipo, titular e a chave Pix.</summary>
public sealed record EmployeeBankAccountForPayment(
    Guid EmployeeId, string Name, string? Registration, int Version, string? BankCode, string? Agency, string? Account, string? AccountType, string? HolderName, string? HolderCpf,
    string? PixKeyType, string? PixKey);

/// <summary>Folha fechada (RH-02c CA 2): só para aviso e projeção, sem valor nem dado pessoal.</summary>
public sealed record PayrollClosed(Guid PayrollRunId, DateOnly Period) : IIntegrationEvent;

/// <summary>Folha reaberta (RH-02c CA 13): só para aviso.</summary>
public sealed record PayrollReopened(Guid PayrollRunId) : IIntegrationEvent;

/// <summary>Desligamento registrado (CA 17), sem consumidor nesta fase.</summary>
public sealed record EmployeeTerminated(Guid EmployeeId, DateOnly TerminationDate) : IIntegrationEvent;

/// <summary>Salário mudou (CA 14), só o id e a vigência, sem valor; sem consumidor nesta fase.</summary>
public sealed record EmployeeSalaryChanged(Guid EmployeeId, DateOnly EffectiveDate) : IIntegrationEvent;

/// <summary>Afastamento começou (CAD-04c1 CA 6 e 7): só o id do funcionário e as datas, nunca o tipo nem o motivo.</summary>
public sealed record EmployeeLeaveStarted(Guid EmployeeId, DateOnly StartDate, DateOnly ExpectedEndDate) : IIntegrationEvent;

/// <summary>Afastamento terminou (no dia seguinte ao fim previsto, CA 7): só o id do funcionário e as datas.</summary>
public sealed record EmployeeLeaveEnded(Guid EmployeeId, DateOnly StartDate, DateOnly EndDate) : IIntegrationEvent;

/// <summary>
/// Versão de tabela legal da folha conferida (FLH-01): o tipo (<c>Inss</c>, <c>Irrf</c>...), o "vale a partir de" e o id. Dentro do HR
/// (o motor da FLH-02a marca as folhas em rascunho); sem valores.
/// </summary>
public sealed record PayrollTaxTableActivated(string Kind, DateOnly ValidFrom, Guid TableId) : IIntegrationEvent;
