using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Conta para pagamento (CAD-04c2 CA 4 a 9, ADR 0036 e D14): gravar exige <c>hr.view_sensitive</c> e o 2FA de novo; chave Pix ou
/// titular diferente do CPF do funcionário exige a segunda confirmação com motivo; a troca avisa o Proprietário e o funcionário,
/// na mesma transação, e entra em quarentena de 30 dias até a revisão expressa por quem não a fez. Adicionais e vale-transporte
/// (CA 10) ficam na mesma aba.
/// </summary>
public sealed partial class EmployeeComplementService
{
    public const string BankChangedNotification = "hr.bank_account_changed";
    public const string BankReviewedNotification = "hr.bank_account_reviewed";
    public const string BankChangedTemplate = "bank-account-changed";

    public const string StepUpMessage = "Confirme com o código do aplicativo autenticador para salvar a conta para pagamento.";
    public const string SelfReviewMessage = "Você não pode revisar uma troca feita por você.";

    private static readonly int[] InsalubrityGrades = [10, 20, 40];

    /// <summary>Aba Pagamento e adicionais (CA 7, 9 e 10): conta mascarada, situação e adicionais. A leitura vai para a auditoria.</summary>
    public async Task<Result<EmployeePaymentDetail>> PaymentAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        await audit.RecordReadAsync(nameof(Employee) + ".Payment", employeeId.ToString(), cancellationToken);
        return new EmployeePaymentDetail(e.Id, Masked(e), await StatusAsync(e, cancellationToken), e.InsalubrityPct, e.HazardPay, e.UsesTransportVoucher, e.Version);
    }

    /// <summary>Situação da conta sem dado bancário (CA 7), para o chip do cabeçalho; não é leitura sensível.</summary>
    public async Task<Result<BankAccountStatusView>> BankStatusAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        return e is null ? NotFound() : await StatusAsync(e, cancellationToken);
    }

    /// <summary>Conta em claro ("Mostrar" e o diálogo de troca, CA 9), com o nome e o CPF do funcionário; cada leitura vai para a auditoria.</summary>
    public async Task<Result<BankAccountRevealed>> RevealBankAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        await audit.RecordReadAsync(nameof(Employee) + ".BankAccount", employeeId.ToString(), cancellationToken);
        return new BankAccountRevealed(e.BankCode, e.BankAgency, e.BankAccount, e.BankAccountType, e.BankHolderName, e.BankHolderCpf, e.PixKeyType, e.PixKey, e.Name, e.Cpf);
    }

    /// <summary>
    /// Registrar ou trocar a conta (CA 4 a 7). Ordem: permissão, validação, segunda confirmação (422), 2FA de novo (403) e, só
    /// então, a gravação. A troca (banco, agência, conta, titular ou chave) entra em quarentena, avisa o Proprietário pelo sino e
    /// enfileira o e-mail ao funcionário, tudo na mesma transação; a primeira conta não entra em quarentena.
    /// </summary>
    public async Task<Result<EmployeePaymentDetail>> SaveBankAsync(Guid employeeId, BankAccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        if (BankError(input, out var bank) is { } invalid)
        {
            return invalid;
        }

        var mismatches = EmployeeBankRules.Mismatches(e.Cpf, bank.HolderCpf, bank.PixType, bank.PixKey);
        if (mismatches.Count > 0 && !EmployeeBankRules.MismatchConfirmed(input.CheckedWithEmployee, input.MismatchReason))
        {
            return Error.Validation(EmployeeBankRules.MismatchCode, "A chave Pix (ou o titular) não é o CPF do funcionário. Marque \"Conferi com o funcionário\" e informe o motivo, de 10 a 200 caracteres.");
        }

        if (currentUser.UserId is not { } me || !await stepUp.VerifyAsync(me, input.TwoFactorCode, cancellationToken))
        {
            return Error.Forbidden(EmployeeBankRules.StepUpCode, StepUpMessage);
        }

        var first = e.BankAccountVersion == 0;
        var changed = first || e.BankCode != bank.BankCode || e.BankAgency != bank.Agency || e.BankAccount != bank.Account || e.BankAccountType != bank.AccountType
            || e.BankHolderName != bank.HolderName || e.BankHolderCpf != bank.HolderCpf || e.PixKeyType != bank.PixType || e.PixKey != bank.PixKey;
        if (!changed)
        {
            return await PaymentAfterSaveAsync(e, cancellationToken);
        }

        var now = clock.GetUtcNow();
        e.BankCode = bank.BankCode;
        e.BankAgency = bank.Agency;
        e.BankAccount = bank.Account;
        e.BankAccountType = bank.AccountType;
        e.BankHolderName = bank.HolderName;
        e.BankHolderCpf = bank.HolderCpf;
        e.PixKeyType = bank.PixType;
        e.PixKey = bank.PixKey;
        e.BankOwnerMismatch = mismatches.Count > 0;
        e.BankMismatchReason = mismatches.Count > 0 ? input.MismatchReason!.Trim() : null;
        e.BankChangedAt = now;
        e.BankChangedBy = me;
        e.BankAccountVersion++;
        e.BankReviewedAt = null;
        e.BankReviewedBy = null;
        e.BankSelfReviewed = false;
        e.BankReviewNote = null;
        e.BankQuarantineUntil = first ? null : EmployeeBankRules.QuarantineEnd(now);
        if (!first)
        {
            await WarnChangeAsync(e, me, mismatches.Count > 0, now, cancellationToken);
        }

        await SaveEmployeeAsync(e, input.Version, cancellationToken);
        return await PaymentAfterSaveAsync(e, cancellationToken);
    }

    /// <summary>
    /// Revisão expressa (CA 8): <c>hr.approve</c> (na rota) e <c>hr.view_sensitive</c>, por quem não fez a troca (exceção do
    /// Proprietário único, com a marca "autorrevisado"), com como conferiu (até 200) e o 2FA de novo. Avisa o Proprietário.
    /// </summary>
    public async Task<Result<EmployeePaymentDetail>> ReviewBankAsync(Guid employeeId, BankReviewInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        var now = clock.GetUtcNow();
        if (!EmployeeBankRules.InQuarantine(e.BankQuarantineUntil, e.BankReviewedAt, now))
        {
            return Error.Conflict(EmployeeBankRules.NotInQuarantineCode, "Esta conta não está em quarentena: não há troca para revisar.");
        }

        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length is 0 or > EmployeeBankRules.ReviewNoteMax)
        {
            return DependentRules.Field("note", "Informe como conferiu a conta, com até 200 caracteres.");
        }

        if (currentUser.UserId is not { } me)
        {
            return Forbidden();
        }

        var (allowed, self) = EmployeeBankRules.CanReview(me, e.BankChangedBy, await SoleReviewerAsync(me, cancellationToken));
        if (!allowed)
        {
            return Error.Forbidden(EmployeeBankRules.SelfReviewCode, SelfReviewMessage);
        }

        if (!await stepUp.VerifyAsync(me, input.TwoFactorCode, cancellationToken))
        {
            return Error.Forbidden(EmployeeBankRules.StepUpCode, "Confirme com o código do aplicativo autenticador para liberar a conta.");
        }

        e.BankReviewedAt = now;
        e.BankReviewedBy = me;
        e.BankSelfReviewed = self;
        e.BankReviewNote = note;
        var who = await users.DisplayNameAsync(me, cancellationToken) ?? "um usuário";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, BankReviewedNotification, $"Conta para pagamento de {e.Name} revisada por {who}" + (self ? " (autorrevisado)." : "."), PaymentLink(e.Id));
        }

        await SaveEmployeeAsync(e, input.Version, cancellationToken);
        return await PaymentAfterSaveAsync(e, cancellationToken);
    }

    /// <summary>Adicionais e vale-transporte (CA 10): insalubridade 10, 20 ou 40 (ou nenhuma), periculosidade e "usa vale-transporte".</summary>
    public async Task<Result<EmployeePaymentDetail>> SaveAdditionalAsync(Guid employeeId, AdditionalInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        if (input.InsalubrityPct is { } pct && !InsalubrityGrades.Contains(pct))
        {
            return DependentRules.Field("insalubrityPct", "Escolha o grau da insalubridade: 10%, 20% ou 40%.");
        }

        e.InsalubrityPct = input.InsalubrityPct;
        e.HazardPay = input.HazardPay;
        e.UsesTransportVoucher = input.UsesTransportVoucher;
        await SaveEmployeeAsync(e, input.Version, cancellationToken);
        return await PaymentAfterSaveAsync(e, cancellationToken);
    }

    /// <summary>
    /// Aviso da troca (CA 6), na transação do comando: sino de todos os membros do Proprietário e o e-mail ao funcionário no e-mail
    /// pessoal vigente e, se ele mudou nos últimos 30 dias, também no anterior. Nem o sino nem o e-mail levam banco, conta ou chave.
    /// </summary>
    private async Task WarnChangeAsync(Employee e, Guid me, bool mismatch, DateTimeOffset now, CancellationToken ct)
    {
        var who = await users.DisplayNameAsync(me, ct) ?? "um usuário";
        var recipients = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Email))
        {
            recipients.Add(e.Email);
        }

        if (!string.IsNullOrWhiteSpace(e.PreviousEmail) && e.EmailChangedAt is { } changed && now - changed < TimeSpan.FromDays(EmployeeBankRules.PreviousEmailDays)
            && !string.Equals(e.PreviousEmail, e.Email, StringComparison.OrdinalIgnoreCase))
        {
            recipients.Add(e.PreviousEmail);
        }

        var title = $"Conta para pagamento de {e.Name} alterada por {who}" + (mismatch ? ", com chave ou titular diferente do CPF." : ".")
            + (recipients.Count == 0 ? " O funcionário não tem e-mail pessoal: avise pessoalmente." : string.Empty);
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, BankChangedNotification, title, PaymentLink(e.Id));
        }

        var date = BusinessTime.ToBusiness(now).ToString("dd/MM/yyyy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        foreach (var to in recipients)
        {
            // Só o nome e a data no modelo (CA 15): nada de banco, conta, titular ou chave em email_logs.model nem no job.
            email.Enqueue(BankChangedTemplate, to, new Dictionary<string, object?> { ["employee_name"] = e.Name, ["changed_on"] = date }, nameof(Employee), e.Id);
        }
    }

    private async Task<EmployeePaymentDetail> PaymentAfterSaveAsync(Employee e, CancellationToken ct) =>
        new(e.Id, Masked(e), await StatusAsync(e, ct), e.InsalubrityPct, e.HazardPay, e.UsesTransportVoucher, e.Version);

    private async Task<BankAccountStatusView> StatusAsync(Employee e, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var inQuarantine = EmployeeBankRules.InQuarantine(e.BankQuarantineUntil, e.BankReviewedAt, now);
        var (canReview, reason) = await CanReviewAsync(e, inQuarantine, ct);
        return new BankAccountStatusView(
            e.BankAccountVersion > 0,
            e.BankAccountVersion == 1,
            e.BankChangedAt,
            e.BankChangedBy is { } by ? await users.DisplayNameAsync(by, ct) : null,
            e.BankQuarantineUntil,
            inQuarantine,
            e.BankReviewedAt,
            e.BankReviewedBy is { } rb ? await users.DisplayNameAsync(rb, ct) : null,
            e.BankSelfReviewed,
            e.BankAccountVersion,
            canReview,
            reason);
    }

    /// <summary>Se o usuário atual pode revisar a troca em quarentena (CA 8), para a tela mostrar "Revisar conta" ou o motivo.</summary>
    private async Task<(bool, string?)> CanReviewAsync(Employee e, bool inQuarantine, CancellationToken ct)
    {
        if (!inQuarantine || currentUser.UserId is not { } me || !await permissions.HasPermissionAsync("hr", StandardActions.Approve, ct))
        {
            return (false, null);
        }

        var (allowed, _) = EmployeeBankRules.CanReview(me, e.BankChangedBy, await SoleReviewerAsync(me, ct));
        return allowed ? (true, null) : (false, SelfReviewMessage);
    }

    /// <summary>
    /// Proprietário único (CA 8, como a D6 do Financeiro): quem fez a troca só revisa se é do Proprietário e não existe outro usuário
    /// ativo com <c>hr.approve</c> e <c>hr.view_sensitive</c>.
    /// </summary>
    private async Task<bool> SoleReviewerAsync(Guid me, CancellationToken ct)
    {
        if (!(await users.OwnerIdsAsync(ct)).Contains(me))
        {
            return false;
        }

        var sensitive = await users.UsersWithPermissionAsync("hr." + PositionService.SensitiveAction, ct);
        var reviewers = (await users.UsersWithPermissionAsync("hr." + StandardActions.Approve, ct)).Where(sensitive.Contains);
        return SoleOwnerRule.NoOtherEligible(reviewers, me, [me]);
    }

    private static BankAccountMasked? Masked(Employee e) => e.BankAccountVersion == 0 ? null : new BankAccountMasked(
        e.BankCode,
        e.BankAgency is null ? null : EmployeeBankRules.Tail(e.BankAgency),
        e.BankAccount is null ? null : EmployeeBankRules.Tail(e.BankAccount),
        e.BankAccountType,
        EmployeeBankRules.MaskName(e.BankHolderName),
        EmployeeBankRules.MaskCpf(e.BankHolderCpf),
        e.PixKeyType,
        e.PixKey is null ? null : EmployeeBankRules.MaskPix(e.PixKeyType, e.PixKey),
        EmployeeBankRules.Mismatches(e.Cpf, e.BankHolderCpf, e.PixKeyType, e.PixKey));

    private static string PaymentLink(Guid employeeId) => "/app/rh/funcionarios/" + employeeId + "?aba=pagamento";

    private sealed record BankFields(string? BankCode, string? Agency, string? Account, BankAccountType? AccountType, string HolderName, string HolderCpf, PixKeyType? PixType, string? PixKey);

    /// <summary>
    /// Validação (CA 4): banco completo (COMPE de 3 dígitos, agência, conta e tipo), Pix completo (tipo e chave válida pelo tipo),
    /// ou os dois; titular (nome e CPF) sempre.
    /// </summary>
    private static Error? BankError(BankAccountInput input, out BankFields fields)
    {
        fields = null!;
        var code = Digits(input.BankCode);
        var agency = Clean(input.Agency);
        var account = Clean(input.Account);
        var anyBank = code is not null || agency is not null || account is not null || input.AccountType is not null;
        if (anyBank)
        {
            if (code is not { Length: 3 })
            {
                return DependentRules.Field("bankCode", "Informe o código do banco (COMPE), com 3 dígitos.");
            }

            if (agency is not { Length: <= 10 } || !agency.All(c => char.IsAsciiDigit(c) || c == '-'))
            {
                return DependentRules.Field("agency", "Informe a agência, só com números (e o dígito, se houver).");
            }

            if (account is not { Length: <= 20 } || !account.All(c => char.IsAsciiDigit(c) || c is '-' or 'X'))
            {
                return DependentRules.Field("account", "Informe a conta com o dígito, só com números.");
            }

            if (input.AccountType is not { } type || !Enum.IsDefined(type))
            {
                return DependentRules.Field("accountType", "Escolha o tipo da conta.");
            }
        }

        string? pix = null;
        if (input.PixKeyType is { } pixType)
        {
            if (!Enum.IsDefined(pixType) || (pix = EmployeeBankRules.NormalizePixKey(pixType, input.PixKey)) is null)
            {
                return DependentRules.Field("pixKey", pixType switch
                {
                    PixKeyType.Cpf => "Chave Pix inválida: informe um CPF válido.",
                    PixKeyType.Email => "Chave Pix inválida: informe um e-mail válido.",
                    PixKeyType.Phone => "Chave Pix inválida: informe o celular com DDD.",
                    _ => "Chave Pix inválida: copie a chave aleatória do app do banco (36 caracteres, com hifens).",
                });
            }
        }
        else if (!string.IsNullOrWhiteSpace(input.PixKey))
        {
            return DependentRules.Field("pixKeyType", "Escolha o tipo da chave Pix.");
        }

        if (!anyBank && pix is null)
        {
            return DependentRules.Field("bankCode", "Informe o banco, a agência e a conta, ou uma chave Pix.");
        }

        var holder = (input.HolderName ?? string.Empty).Trim();
        if (holder.Length is 0 or > 150)
        {
            return DependentRules.Field("holderName", "Informe o nome do titular da conta, com até 150 caracteres.");
        }

        var holderCpf = BrazilianDocuments.Normalize(input.HolderCpf);
        if (holderCpf.Length != 11 || !BrazilianDocuments.IsValidCpf(holderCpf))
        {
            return DependentRules.Field("holderCpf", "Informe um CPF válido para o titular da conta.");
        }

        fields = new BankFields(code, agency, account, anyBank ? input.AccountType : null, holder, holderCpf, input.PixKeyType, pix);
        return null;
    }

    private static string? Digits(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static string? Clean(string? value)
    {
        var text = new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '.').ToArray()).ToUpperInvariant();
        return text.Length == 0 ? null : text;
    }
}
