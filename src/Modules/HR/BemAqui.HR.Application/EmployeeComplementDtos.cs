using System.Text.Json.Serialization;
using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>
/// Dependente (CAD-04c2 CA 1 e 2), comando próprio com <c>hr.edit</c> e <c>hr.view_sensitive</c>. O CPF só no dependente de IR; na
/// edição, CPF vazio num dependente de IR mantém o gravado. <c>Version</c> é o <c>xmin</c> do dependente (só na edição).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DependentInput(string? Name, DateOnly? BirthDate, DependentKinship? Kinship, bool IrDependent, bool FamilyAllowance, string? Cpf, uint? Version = null);

/// <summary>Dependente só pela rota <c>/dependents/sensitive</c> (CA 3), com o CPF mascarado ("***.456.789-**").</summary>
public sealed record DependentDetail(Guid Id, string Name, DateOnly BirthDate, DependentKinship Kinship, bool IrDependent, bool FamilyAllowance, string? CpfMasked, uint Version);

/// <summary>
/// Conta para pagamento (CAD-04c2 CA 4 e 5), comando próprio com <c>hr.edit</c>, <c>hr.view_sensitive</c> e o 2FA de novo
/// (<c>TwoFactorCode</c>, ADR 0008 adendo de 21/10/2026). Banco (COMPE, agência, conta e tipo) completo, chave Pix (tipo e chave),
/// ou os dois; titular sempre. Chave Pix ou titular diferente do CPF do funcionário exige <c>CheckedWithEmployee</c> e o motivo
/// (10 a 200). <c>Version</c> é o <c>xmin</c> do funcionário (ADR 0019 §2).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BankAccountInput(
    string? BankCode,
    string? Agency,
    string? Account,
    BankAccountType? AccountType,
    string? HolderName,
    string? HolderCpf,
    PixKeyType? PixKeyType,
    string? PixKey,
    bool CheckedWithEmployee,
    string? MismatchReason,
    string? TwoFactorCode,
    uint Version);

/// <summary>Revisão expressa da conta em quarentena (CA 8): como conferiu (até 200) e o 2FA de novo.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BankReviewInput(string? Note, string? TwoFactorCode, uint Version);

/// <summary>Adicionais e vale-transporte (CA 10): insalubridade 10, 20 ou 40 (ou nula), periculosidade e "usa vale-transporte".</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdditionalInput(int? InsalubrityPct, bool HazardPay, bool UsesTransportVoucher, uint Version);

/// <summary>Documentos extras (CA 11): CNH (número, categoria e validade) e certificado de reservista.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeDocumentsInput(string? CnhNumber, string? CnhCategory, DateOnly? CnhExpiry, string? MilitaryCertificate, uint Version);

/// <summary>
/// Conta mascarada (CA 9): agência, conta, titular e chave Pix mascarados também para quem tem a permissão; "Mostrar" lê
/// <see cref="BankAccountRevealed"/>. <c>Mismatches</c>: o que não bate com o CPF do funcionário (CA 5).
/// </summary>
public sealed record BankAccountMasked(
    string? BankCode,
    string? Agency,
    string? Account,
    BankAccountType? AccountType,
    string HolderName,
    string HolderCpf,
    PixKeyType? PixKeyType,
    string? PixKey,
    IReadOnlyList<BankMismatchKind> Mismatches);

/// <summary>
/// Situação da conta (CA 7 e 8), sem dado bancário: primeira conta ou troca, quarentena, revisão e se o usuário atual pode revisar.
/// </summary>
public sealed record BankAccountStatusView(
    bool HasAccount,
    bool IsFirstAccount,
    DateTimeOffset? ChangedAt,
    string? ChangedByName,
    DateTimeOffset? QuarantineUntil,
    bool InQuarantine,
    DateTimeOffset? ReviewedAt,
    string? ReviewedByName,
    bool SelfReviewed,
    int AccountVersion,
    bool CanReview,
    string? CannotReviewReason);

/// <summary>Aba Pagamento e adicionais (CA 7, 9 e 10), só por <c>/payment/sensitive</c>, com <c>SensitiveRead</c> e <c>no-store</c>.</summary>
public sealed record EmployeePaymentDetail(
    Guid EmployeeId,
    BankAccountMasked? Account,
    BankAccountStatusView Status,
    int? InsalubrityPct,
    bool HazardPay,
    bool UsesTransportVoucher,
    uint Version);

/// <summary>
/// Conta em claro (CA 9, "Mostrar" e o diálogo de troca), com o nome e o CPF do funcionário para o titular vir preenchido. Cada
/// leitura grava <c>SensitiveRead</c> na auditoria.
/// </summary>
public sealed record BankAccountRevealed(
    string? BankCode,
    string? Agency,
    string? Account,
    BankAccountType? AccountType,
    string? HolderName,
    string? HolderCpf,
    PixKeyType? PixKeyType,
    string? PixKey,
    string EmployeeName,
    string EmployeeCpf);

/// <summary>Documentos extras só por <c>/documents/sensitive</c> (CA 11).</summary>
public sealed record EmployeeDocumentsDetail(string? CnhNumber, string? CnhCategory, DateOnly? CnhExpiry, string? MilitaryCertificate, uint Version);
