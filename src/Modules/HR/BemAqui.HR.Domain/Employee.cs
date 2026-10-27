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

    /// <summary>
    /// Mudança de um afastamento (CAD-04c1b): edição, encerramento antes do previsto ou cancelamento, com o período antes e
    /// depois. É o rastro na linha do tempo e nunca conta como afastamento.
    /// </summary>
    LeaveChange,
}

/// <summary>O que mudou num afastamento (CAD-04c1b).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeeLeaveChangeKind>))]
public enum EmployeeLeaveChangeKind
{
    Edited,
    EndedEarly,
    Cancelled,
}

/// <summary>Estado do afastamento no dia (CAD-04c1b), calculado na leitura como a situação "Afastado".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeeLeaveState>))]
public enum EmployeeLeaveState
{
    Scheduled,
    Current,
    Ended,
    Cancelled,
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

    /// <summary>E-mail pessoal anterior (CAD-04c2 CA 6): o aviso de troca de conta vai também para ele nos 30 dias da troca do e-mail.</summary>
    [PersonalData]
    public string? PreviousEmail { get; set; }

    /// <summary>Quando o e-mail pessoal mudou pela última vez (CAD-04c2 CA 6).</summary>
    public DateTimeOffset? EmailChangedAt { get; set; }

    // Conta para pagamento (CAD-04c2 CA 4 a 9, ADR 0036): banco, Pix e titular são sensíveis e só saem por /sensitive, mascarados.
    [PersonalData]
    public string? BankCode { get; set; }

    [PersonalData]
    public string? BankAgency { get; set; }

    [PersonalData]
    public string? BankAccount { get; set; }

    [PersonalData]
    public EmployeeBankAccountType? BankAccountType { get; set; }

    [PersonalData]
    public string? BankHolderName { get; set; }

    [PersonalData]
    public string? BankHolderCpf { get; set; }

    [PersonalData]
    public EmployeePixKeyType? PixKeyType { get; set; }

    [PersonalData]
    public string? PixKey { get; set; }

    /// <summary>Motivo da segunda confirmação (CA 5), quando a chave Pix ou o titular não é o CPF do funcionário.</summary>
    [PersonalData]
    public string? BankMismatchReason { get; set; }

    /// <summary>A conta gravada tem chave Pix ou titular diferente do CPF do funcionário (CA 5): o aviso fica visível na aba.</summary>
    public bool BankOwnerMismatch { get; set; }

    /// <summary>Quando a conta foi registrada ou trocada pela última vez, e por quem (CA 6 e 7).</summary>
    public DateTimeOffset? BankChangedAt { get; set; }

    public Guid? BankChangedBy { get; set; }

    /// <summary>Fim da quarentena da conta trocada (CA 7): troca + 30 dias; nulo na primeira conta.</summary>
    public DateTimeOffset? BankQuarantineUntil { get; set; }

    /// <summary>Revisão expressa da troca (CA 8), por quem não a fez.</summary>
    public DateTimeOffset? BankReviewedAt { get; set; }

    public Guid? BankReviewedBy { get; set; }

    /// <summary>Revisado por quem fez a troca, pela exceção do Proprietário único (marca "autorrevisado", CA 8).</summary>
    public bool BankSelfReviewed { get; set; }

    /// <summary>Como a revisão conferiu a conta (até 200).</summary>
    [PersonalData]
    public string? BankReviewNote { get; set; }

    /// <summary>Sobe a cada registro ou troca da conta (0 = sem conta); a RH-02c confere a versão no pagamento.</summary>
    public int BankAccountVersion { get; set; }

    // Adicionais e vale-transporte (CA 10): o valor do vale e o desconto vêm da folha (RH-02a), não do cadastro.
    [PersonalData]
    public int? InsalubrityPct { get; set; }

    [PersonalData]
    public bool HazardPay { get; set; }

    [PersonalData]
    public bool UsesTransportVoucher { get; set; }

    // Documentos extras (CA 11).
    [PersonalData]
    public string? CnhNumber { get; set; }

    [PersonalData]
    public string? CnhCategory { get; set; }

    [PersonalData]
    public DateOnly? CnhExpiry { get; set; }

    [PersonalData]
    public string? MilitaryCertificate { get; set; }

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

    /// <summary>Vigência; no afastamento, o início (editável enquanto o afastamento não começou, CAD-04c1b).</summary>
    public DateOnly EffectiveDate { get; set; }

    [PersonalData]
    public string? OldValue { get; init; }

    [PersonalData]
    public string NewValue { get; init; } = string.Empty;

    /// <summary>Motivo (até 200). No afastamento, sem CID nem diagnóstico, e nunca em log nem evento (CAD-04c1 CA 5).</summary>
    [PersonalData]
    public string Reason { get; set; } = string.Empty;

    public Guid? UserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Tipo do afastamento (só no Leave): dado de saúde quando é doença ou acidente (LGPD art. 11).</summary>
    [PersonalData]
    public EmployeeLeaveType? LeaveType { get; set; }

    /// <summary>
    /// Fim previsto do afastamento (só no Leave), inclusive: a volta prevista é o dia seguinte. No encerramento antes do previsto
    /// (CAD-04c1b), vira o dia anterior à volta; o previsto original fica na entrada <c>LeaveChange</c>.
    /// </summary>
    [PersonalData]
    public DateOnly? LeaveEndDate { get; set; }

    /// <summary>Tipo anterior (só no LeaveChange de edição, CAD-04c1b): dado de saúde, como o tipo.</summary>
    [PersonalData]
    public EmployeeLeaveType? PreviousLeaveType { get; init; }

    /// <summary>Afastamento a que o LeaveChange se refere (CAD-04c1b).</summary>
    public Guid? LeaveId { get; init; }

    /// <summary>O que mudou (só no LeaveChange, CAD-04c1b).</summary>
    public EmployeeLeaveChangeKind? LeaveChange { get; init; }

    /// <summary>
    /// Cancelamento lógico do afastamento que não começou (CAD-04c1b): sai da situação, da sobreposição e do job, e continua na
    /// linha do tempo como "Cancelado", com o motivo na entrada LeaveChange.
    /// </summary>
    public DateTimeOffset? CancelledAt { get; set; }

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

    public const string CancelledCode = "leave_cancelled";
    public const string EndedCode = "leave_ended";
    public const string NotStartedCode = "leave_not_started";
    public const string StartedCode = "leave_started";

    /// <summary>Doença e acidente de trabalho são dado de saúde: tipo e detalhes só com <c>hr.view_health</c> (CAD-04c1b, PO 22/10/2026).</summary>
    public static bool IsHealth(EmployeeLeaveType? type) => type is EmployeeLeaveType.Illness or EmployeeLeaveType.WorkAccident;

    /// <summary>
    /// Tipo e motivo de qualquer afastamento só com <c>hr.view_health</c> (PO 22/10/2026, CAD-04c1b 1.1): mostrar os demais tipos
    /// deixaria inferir que o "Afastado" sem tipo é de saúde. Sem a ação, só "Afastado" e as datas.
    /// </summary>
    public static bool ShowsDetails(bool canViewHealth) => canViewHealth;

    /// <summary>Estado no dia (CAD-04c1b): cancelado prevalece; antes do início, previsto; até o fim, em curso; depois, encerrado.</summary>
    public static EmployeeLeaveState State(DateOnly start, DateOnly end, bool cancelled, DateOnly today) =>
        cancelled ? EmployeeLeaveState.Cancelled
        : today < start ? EmployeeLeaveState.Scheduled
        : today <= end ? EmployeeLeaveState.Current
        : EmployeeLeaveState.Ended;

    /// <summary>
    /// Editar (CAD-04c1b CA 1 e 2): só o previsto ou o em curso. No em curso, o início fica hoje ou antes (o começo já foi
    /// publicado) e o fim previsto, hoje ou depois (para terminar antes existe o encerramento). As demais validações são as do
    /// registro (tipo, datas, admissão, motivo, sobreposição).
    /// </summary>
    public static Error? EditError(EmployeeLeaveState state, DateOnly newStart, DateOnly newEnd, DateOnly today) => state switch
    {
        EmployeeLeaveState.Cancelled => Error.Conflict(CancelledCode, "Este afastamento foi cancelado e não pode mais ser alterado."),
        EmployeeLeaveState.Ended => Error.Conflict(EndedCode, "Este afastamento já terminou e não pode mais ser alterado."),
        EmployeeLeaveState.Current when newStart > today => Field("startDate", "O afastamento já começou: o início precisa ser hoje ou antes."),
        EmployeeLeaveState.Current when newEnd < today => Field("expectedEndDate", "Para terminar antes do previsto, use \"Encerrar afastamento\"."),
        _ => null,
    };

    /// <summary>Encerrar antes (CAD-04c1b CA 4): só o em curso; a volta fica depois do início, até o fim previsto e nunca depois de hoje.</summary>
    public static Error? EndError(EmployeeLeaveState state, DateOnly start, DateOnly end, DateOnly? returnDate, DateOnly today)
    {
        switch (state)
        {
            case EmployeeLeaveState.Cancelled:
                return Error.Conflict(CancelledCode, "Este afastamento foi cancelado.");
            case EmployeeLeaveState.Ended:
                return Error.Conflict(EndedCode, "Este afastamento já terminou.");
            case EmployeeLeaveState.Scheduled:
                return Error.Conflict(NotStartedCode, "O afastamento ainda não começou. Para desistir dele, use \"Cancelar afastamento\".");
        }

        if (returnDate is not { } back)
        {
            return Field("returnDate", "Informe a data da volta.");
        }

        if (back <= start)
        {
            return Field("returnDate", "A volta precisa ser depois do início do afastamento.");
        }

        if (back > end)
        {
            return Field("returnDate", "A volta precisa ser até o fim previsto. Para estender o afastamento, use \"Editar\".");
        }

        return back > today ? Field("returnDate", "A volta não pode ser depois de hoje.") : null;
    }

    /// <summary>Cancelar (CAD-04c1b CA 6): só o que ainda não começou, com motivo de até 200 caracteres.</summary>
    public static Error? CancelError(EmployeeLeaveState state, string? reason)
    {
        if (state == EmployeeLeaveState.Cancelled)
        {
            return Error.Conflict(CancelledCode, "Este afastamento já foi cancelado.");
        }

        if (state != EmployeeLeaveState.Scheduled)
        {
            return Error.Conflict(StartedCode, "O afastamento já começou: não dá para cancelar, só encerrar.");
        }

        return string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > ReasonMaxLength
            ? Field("reason", "Informe o motivo do cancelamento, com até 200 caracteres. Não informe CID nem diagnóstico.")
            : null;
    }

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
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
