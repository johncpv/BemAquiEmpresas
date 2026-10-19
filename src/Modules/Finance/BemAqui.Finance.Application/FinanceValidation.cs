using System.Text.RegularExpressions;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

/// <summary>Validação das contas e condições (CA 1 a 3, 11 e 12), com mensagens em PT-BR por campo.</summary>
public static partial class FinanceValidation
{
    [GeneratedRegex(@"^\d{1,5}(-[0-9Xx])?$")]
    private static partial Regex AgencyPattern();

    [GeneratedRegex(@"^\d{1,13}-[0-9Xx]$")]
    private static partial Regex AccountPattern();

    private static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    public static Error? BankAccount(BankAccountInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            return Invalid("name", "Informe o nome da conta, com até 100 caracteres.");
        }

        if (input.Kind == BankAccountKind.Bank)
        {
            if (Banks.Find(input.BankCode) is null)
            {
                return Invalid("bankCode", "Escolha o banco da lista.");
            }

            if (string.IsNullOrWhiteSpace(input.Agency) || !AgencyPattern().IsMatch(input.Agency.Trim()))
            {
                return Invalid("agency", "Informe a agência só com números, com o dígito depois do traço se houver (ex.: 1234-5).");
            }

            if (string.IsNullOrWhiteSpace(input.Account) || !AccountPattern().IsMatch(input.Account.Trim()))
            {
                return Invalid("account", "Informe a conta com o dígito depois do traço (ex.: 12345678-9).");
            }
        }

        if (input.OpeningBalance is not null && input.OpeningDate is null)
        {
            return Invalid("openingDate", "Informe a data do saldo inicial.");
        }

        if (input.OpeningBalance is null && input.OpeningDate is not null)
        {
            return Invalid("openingBalance", "Informe o saldo inicial.");
        }

        if (input.OpeningBalance is { } balance && (decimal.Round(balance, 2) != balance || Math.Abs(balance) >= 10_000_000_000_000_000m))
        {
            return Invalid("openingBalance", "O saldo inicial tem no máximo 2 casas decimais.");
        }

        return null;
    }

    public static Error? PaymentTerm(PaymentTermInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            return Invalid("name", "Informe o nome da condição, com até 100 caracteres.");
        }

        return Installments(input.Installments);
    }

    public static Error? Installments(IReadOnlyList<Installment>? installments) =>
        Domain.PaymentTermSplitter.Validate(installments) is { } message ? Invalid("installments", message) : null;

    /// <summary>Agência para quem só vê (CA 10): <c>1234-5</c> vira <c>****-5</c>.</summary>
    public static string? MaskAgency(string? agency) => agency is null ? null
        : agency.Contains('-', StringComparison.Ordinal) ? new string('*', agency.IndexOf('-', StringComparison.Ordinal)) + agency[agency.IndexOf('-', StringComparison.Ordinal)..]
        : new string('*', agency.Length);

    /// <summary>Conta para quem só vê (CA 10): <c>12345678-9</c> vira <c>*****678-9</c> (os 3 últimos dígitos e o dígito verificador).</summary>
    public static string? MaskAccount(string? account)
    {
        if (account is null)
        {
            return null;
        }

        var dash = account.IndexOf('-', StringComparison.Ordinal);
        var number = dash < 0 ? account : account[..dash];
        var keep = Math.Min(3, Math.Max(0, number.Length - 1));
        return new string('*', number.Length - keep) + number[^keep..] + (dash < 0 ? string.Empty : account[dash..]);
    }
}
