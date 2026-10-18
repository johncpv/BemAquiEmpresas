using System.Net.Mail;
using System.Text.RegularExpressions;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Tenants;

public sealed partial class TenantAdminService
{
    [GeneratedRegex("^[a-z][a-z0-9-]{1,29}[a-z0-9]$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?[.])+[a-z][a-z0-9-]*$")]
    private static partial Regex HostPattern();

    /// <summary>
    /// Validação do formulário "Provisionar tenant" com erro por campo (FND-03b CA 3): chave e host únicos e válidos,
    /// CNPJ com dígito verificador e e-mail do proprietário. O serviço da FND-03a valida de novo ao rodar.
    /// </summary>
    public async Task<Error?> ValidateNewAsync(ProvisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fields = new Dictionary<string, string[]>();
        var key = request.Key?.Trim() ?? string.Empty;
        var host = string.IsNullOrWhiteSpace(request.Host) ? string.Empty : TenantDomain.Normalize(request.Host);

        if (!KeyPattern().IsMatch(key))
        {
            fields["key"] = ["Use de 3 a 31 letras minúsculas, números ou hífen, começando por letra."];
        }
        else if (await db.Tenants.AnyAsync(t => t.Key == key, cancellationToken))
        {
            fields["key"] = ["Já existe um tenant com essa chave."];
        }

        if (!HostPattern().IsMatch(host))
        {
            fields["host"] = ["Informe um endereço válido, como empresa.bemaquiapp.com.br."];
        }
        else if (await db.TenantDomains.AnyAsync(d => d.Host == host, cancellationToken))
        {
            fields["host"] = ["Esse endereço já está em uso por outro tenant."];
        }

        if (string.IsNullOrWhiteSpace(request.LegalName))
        {
            fields["legalName"] = ["Informe a razão social."];
        }

        if (!string.IsNullOrWhiteSpace(request.Document) && !BrazilianDocuments.IsValidCnpj(request.Document))
        {
            fields["document"] = ["CNPJ inválido. Confira os dígitos."];
        }

        if (string.IsNullOrWhiteSpace(request.OwnerEmail) || !MailAddress.TryCreate(request.OwnerEmail.Trim(), out _))
        {
            fields["ownerEmail"] = ["Informe um e-mail válido para o proprietário."];
        }

        return fields.Count == 0 ? null : Error.Validation(Error.Codes.Validation, "Confira os campos destacados.", fields);
    }
}
