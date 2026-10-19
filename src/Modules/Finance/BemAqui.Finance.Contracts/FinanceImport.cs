using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>Registro gravado pela importação: id e <c>xmin</c> da linha de destino.</summary>
public sealed record FinanceImportWrite(Guid Id, long Xmin);

public sealed record PaymentTermImportData(string Name, IReadOnlyList<Installment> Installments, bool IsActive, string ImportSource);

/// <summary>Conta como vem da origem: sempre sem saldo inicial e sem data (MIG-01b CA 16, ADR 0017).</summary>
public sealed record BankAccountImportData(string Name, BankAccountKind Kind, string? BankCode, string? Agency, string? Account, bool IsActive, string ImportSource);

/// <summary>Escrita da importação nas condições de pagamento (MIG-01b CA 13): só o módulo Migrations usa.</summary>
public interface IPaymentTermImport
{
    /// <summary>Condição do seed pela <c>seed_key</c> (ex.: <c>a-vista</c>), para o de-para sem duplicar.</summary>
    Task<Guid?> FindBySeedKeyAsync(string seedKey, CancellationToken cancellationToken = default);

    Task<Guid?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<FinanceImportWrite>> CreateAsync(PaymentTermImportData data, CancellationToken cancellationToken = default);

    Task<Result<FinanceImportWrite>> UpdateAsync(Guid termId, PaymentTermImportData data, CancellationToken cancellationToken = default);

    /// <summary>Liga a uma condição existente: grava o <c>import_source</c> só se estiver vazio.</summary>
    Task<Result<FinanceImportWrite>> LinkAsync(Guid termId, string importSource, CancellationToken cancellationToken = default);

    Task<PaymentTermImportData?> CurrentAsync(Guid termId, CancellationToken cancellationToken = default);

    Task<long?> VersionAsync(Guid termId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Escrita da importação nas contas (MIG-01b CA 15 e 16): só o cadastro, nunca saldo. Valida como a tela (CAD-05b), mas é
/// gravação do sistema: não passa pela troca protegida de dados bancários.
/// </summary>
public interface IBankAccountImport
{
    Task<Result<FinanceImportWrite>> CreateAsync(BankAccountImportData data, CancellationToken cancellationToken = default);

    Task<Result<FinanceImportWrite>> UpdateAsync(Guid accountId, BankAccountImportData data, CancellationToken cancellationToken = default);

    Task<BankAccountImportData?> CurrentAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<long?> VersionAsync(Guid accountId, CancellationToken cancellationToken = default);
}
