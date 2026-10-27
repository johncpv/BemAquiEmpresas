using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>Tipo da conta para pagamento (CAD-04c2 CA 4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeeBankAccountType>))]
public enum EmployeeBankAccountType
{
    Checking,
    Savings,
    Salary,
    Payment,
}

/// <summary>Tipo da chave Pix (CAD-04c2 CA 4): a chave é validada pelo tipo.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeePixKeyType>))]
public enum EmployeePixKeyType
{
    Cpf,
    Email,
    Phone,
    Random,
}

/// <summary>Parentesco do dependente (CAD-04c2 CA 1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DependentKinship>))]
public enum DependentKinship
{
    Child,
    Stepchild,
    Spouse,
    Partner,
    Parent,
    Ward,
    Other,
}

/// <summary>O que não bate com o CPF do funcionário na conta para pagamento (CAD-04c2 CA 5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeeBankMismatchKind>))]
public enum EmployeeBankMismatchKind
{
    Holder,
    PixKey,
}

/// <summary>
/// Dependente do funcionário (<c>hr.employee_dependents</c>, CAD-04c2 CA 1 a 3): nome, nascimento, parentesco e as marcas de IR e
/// salário-família. O CPF só existe no dependente de IR. Tudo é dado pessoal (pode ser de menor): só sai por <c>/sensitive</c>.
/// </summary>
public sealed class EmployeeDependent : Entity, IAuditable, ISoftDeletable, IConcurrencyVersioned
{
    public Guid EmployeeId { get; init; }

    [PersonalData]
    public string Name { get; set; } = string.Empty;

    [PersonalData]
    public DateOnly BirthDate { get; set; }

    [PersonalData]
    public DependentKinship Kinship { get; set; }

    /// <summary>Só no dependente de IR; único entre os dependentes não excluídos do funcionário (CA 2).</summary>
    [PersonalData]
    public string? Cpf { get; set; }

    [PersonalData]
    public bool IrDependent { get; set; }

    [PersonalData]
    public bool FamilyAllowance { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Regras do dependente sem banco (CAD-04c2 CA 2).</summary>
public static class DependentRules
{
    public const int NameMaxLength = 150;

    /// <summary>
    /// CPF só com "Dependente de IR": desmarcado, o CPF não é aceito; marcado, é obrigatório e válido. Devolve o CPF normalizado
    /// (ou null) e o erro.
    /// </summary>
    public static (string? Cpf, Error? Error) Cpf(bool irDependent, string? value)
    {
        var cpf = BrazilianDocuments.Normalize(value);
        if (!irDependent)
        {
            return cpf.Length == 0 ? (null, null) : (null, Field("cpf", "O CPF só é informado no dependente de IR. Marque \"Dependente de IR\" ou apague o CPF."));
        }

        return cpf.Length == 0 ? (null, Field("cpf", "Informe o CPF do dependente de IR."))
            : !BrazilianDocuments.IsValidCpf(cpf) ? (null, Field("cpf", "CPF inválido."))
            : (cpf, null);
    }

    public static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>
/// Regras da conta para pagamento sem banco (CAD-04c2 CA 4 a 9, ADR 0036): chave Pix por tipo, divergência com o CPF do funcionário,
/// quarentena de 30 dias, revisão por quem não fez a troca e as máscaras de exibição.
/// </summary>
public static class EmployeeBankRules
{
    /// <summary>Quarentena da conta trocada (ADR 0036, decisão do PO D14): 30 dias corridos a partir da troca.</summary>
    public const int QuarantineDays = 30;

    public const int MismatchReasonMin = 10;
    public const int MismatchReasonMax = 200;
    public const int ReviewNoteMax = 200;

    /// <summary>Janela do e-mail anterior (CA 6): trocado há menos de 30 dias, o aviso vai também para ele.</summary>
    public const int PreviousEmailDays = 30;

    public const string StepUpCode = "mfa_step_up_required";
    public const string MismatchCode = "bank_owner_mismatch_unconfirmed";
    public const string SelfReviewCode = "bank_self_review";
    public const string NotInQuarantineCode = "bank_not_in_quarantine";

    /// <summary>Chave normalizada pelo tipo (CA 4), ou null se não vale: CPF com 11 dígitos válidos, e-mail, telefone (+55) ou aleatória (UUID).</summary>
    public static string? NormalizePixKey(EmployeePixKeyType type, string? key)
    {
        var text = (key ?? string.Empty).Trim();
        switch (type)
        {
            case EmployeePixKeyType.Cpf:
                var cpf = BrazilianDocuments.Normalize(text);
                return cpf.Length == 11 && BrazilianDocuments.IsValidCpf(cpf) ? cpf : null;
            case EmployeePixKeyType.Email:
                var email = text.ToLowerInvariant();
                var at = email.IndexOf('@', StringComparison.Ordinal);
                return email.Length is > 5 and <= 77 && at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.EndsWith('.')
                    && !email.Any(char.IsWhiteSpace) ? email : null;
            case EmployeePixKeyType.Phone:
                var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
                if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
                {
                    digits = digits[2..];
                }

                return digits.Length is 10 or 11 && digits[0] != '0' ? "+55" + digits : null;
            case EmployeePixKeyType.Random:
                return Guid.TryParseExact(text, "D", out var random) ? random.ToString("D") : null;
            default:
                return null;
        }
    }

    /// <summary>O que não bate com o CPF do funcionário (CA 5): titular com outro CPF, ou chave Pix do tipo CPF de outra pessoa.</summary>
    public static IReadOnlyList<EmployeeBankMismatchKind> Mismatches(string employeeCpf, string? holderCpf, EmployeePixKeyType? pixType, string? pixKey)
    {
        var list = new List<EmployeeBankMismatchKind>();
        if (holderCpf is not null && holderCpf != employeeCpf)
        {
            list.Add(EmployeeBankMismatchKind.Holder);
        }

        if (pixType == EmployeePixKeyType.Cpf && pixKey is not null && pixKey != employeeCpf)
        {
            list.Add(EmployeeBankMismatchKind.PixKey);
        }

        return list;
    }

    /// <summary>Motivo da segunda confirmação (CA 5): de 10 a 200 caracteres, depois de marcar "Conferi com o funcionário".</summary>
    public static bool MismatchConfirmed(bool checkedWithEmployee, string? reason) =>
        checkedWithEmployee && reason?.Trim().Length is >= MismatchReasonMin and <= MismatchReasonMax;

    public static DateTimeOffset QuarantineEnd(DateTimeOffset changedAt) => changedAt.AddDays(QuarantineDays);

    /// <summary>Em quarentena: trocada (não a primeira), dentro do prazo e sem revisão (CA 7 e 8).</summary>
    public static bool InQuarantine(DateTimeOffset? until, DateTimeOffset? reviewedAt, DateTimeOffset now) => until is { } u && reviewedAt is null && now < u;

    /// <summary>
    /// Quem pode revisar (CA 8): quem não fez a troca; quem fez só com a exceção do Proprietário único (nenhum outro revisor
    /// elegível), e aí a revisão leva a marca "autorrevisado".
    /// </summary>
    public static (bool Allowed, bool Self) CanReview(Guid reviewer, Guid? changedBy, bool soleOwner) =>
        changedBy != reviewer ? (true, false) : soleOwner ? (true, true) : (false, false);

    /// <summary>CPF mascarado no padrão "***.456.789-**" (CA 5).</summary>
    public static string MaskCpf(string? cpf)
    {
        var d = BrazilianDocuments.Normalize(cpf);
        return d.Length == 11 ? "***." + d[3..6] + "." + d[6..9] + "-**" : Tail(d, 2);
    }

    /// <summary>Nome mascarado: a inicial de cada palavra ("Maria Silva" vira "M**** S****").</summary>
    public static string MaskName(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[0] + new string('*', Math.Max(w.Length - 1, 1))));

    /// <summary>Só os últimos <paramref name="visible"/> caracteres (agência e conta).</summary>
    public static string Tail(string? value, int visible = 2)
    {
        var text = value ?? string.Empty;
        return text.Length <= visible ? new string('*', text.Length) : new string('*', text.Length - visible) + text[^visible..];
    }

    /// <summary>Chave Pix mascarada pelo tipo (CA 9).</summary>
    public static string MaskPix(EmployeePixKeyType? type, string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        switch (type)
        {
            case EmployeePixKeyType.Cpf:
                return MaskCpf(key);
            case EmployeePixKeyType.Email:
                var at = key.IndexOf('@', StringComparison.Ordinal);
                return at <= 0 ? Tail(key, 2) : key[0] + new string('*', Math.Max(at - 1, 3)) + key[at..];
            case EmployeePixKeyType.Phone:
                return "(**) *****-" + key[^4..];
            default:
                return new string('*', 8) + key[^4..];
        }
    }
}
