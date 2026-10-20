using System.Net.Mail;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

public static partial class PaymentDetails
{
    /// <summary>Avisos âmbar (CA 6 e 7): titular diferente do fornecedor e boleto que não bate com o título.</summary>
    public static PaymentDetailsView View(PaymentDetailsInput details, string? supplierDocument, decimal netAmount, DateOnly? dueDate, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(details);
        string? holder = null;
        var supplier = BrazilianDocuments.Normalize(supplierDocument);
        if (details.HolderDocument is { Length: > 0 } doc && supplier.Length > 0 && doc != supplier)
        {
            holder = $"O titular ({Mask(doc)}) não é o fornecedor.";
        }

        string? boleto = null;
        if (details.Kind == PaymentMethodKind.Boleto && BoletoLine.Parse(details.Barcode, today) is { } info)
        {
            var parts = new List<string>();
            if (info.Amount is { } amount && amount != netAmount)
            {
                parts.Add("valor " + ReceivableService.Money(amount));
            }

            if (info.DueDate is { } due && dueDate is { } titleDue && due != titleDue)
            {
                parts.Add($"vencimento {due:dd/MM/yyyy}");
            }

            boleto = parts.Count > 0 ? "O boleto traz " + string.Join(" e ", parts) + ", diferente do título." : null;
        }

        var masked = details with { HolderDocument = details.HolderDocument is { } d ? Mask(d) : null, PixKey = details.PixKey is { } k ? MaskKey(k) : null };
        return new PaymentDetailsView(masked, holder, boleto);
    }

    /// <summary>CPF com o começo e o fim escondidos; CNPJ inteiro, porque é de empresa.</summary>
    public static string Mask(string document) =>
        document.Length == 11 ? $"***.{document[3..6]}.{document[6..9]}-**" : document;

    private static string MaskKey(string key) => key.Length <= 6 ? "***" : key[..3] + new string('*', Math.Max(3, key.Length - 6)) + key[^3..];

    private static Error? Holder(string? name, string doc)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            return Bad("holderName", "Informe o nome do titular.");
        }

        return BrazilianDocuments.IsValid(doc) ? null : Bad("holderDocument", "Informe um CPF ou CNPJ válido do titular.");
    }

    private static string? NormalizePixKey(PixKeyType type, string key)
    {
        var trimmed = key.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        return type switch
        {
            PixKeyType.Cpf => BrazilianDocuments.IsValidCpf(digits) ? digits : null,
            PixKeyType.Cnpj => BrazilianDocuments.IsValidCnpj(digits) ? digits : null,
            PixKeyType.Email => MailAddress.TryCreate(trimmed, out var mail) && mail.Address == trimmed && trimmed.Length <= 77 ? trimmed.ToLowerInvariant() : null,
            PixKeyType.Phone => digits.Length is 10 or 11 ? "+55" + digits : digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal) ? "+" + digits : null,
            _ => Guid.TryParse(trimmed, out var evp) ? evp.ToString() : null,
        };
    }
}
