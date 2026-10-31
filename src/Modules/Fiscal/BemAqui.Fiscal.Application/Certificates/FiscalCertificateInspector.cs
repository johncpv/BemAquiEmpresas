using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Certificates;

/// <summary>Regras do certificado A1 (FIS-02a CA 11 e 14).</summary>
public static class FiscalCertificateRules
{
    /// <summary>Tamanho máximo do <c>.pfx</c> aceito pela tela.</summary>
    public const int MaxBytes = 50 * 1024;

    /// <summary>A partir de quantos dias o certificado aparece "Vence em {n} dias" (alerta).</summary>
    public const int WarnDays = 30;

    /// <summary>Limiares do aviso ao Proprietário (30, 15 e 7 dias), do maior para o menor.</summary>
    public static readonly int[] Thresholds = [30, 15, 7];

    /// <summary>Política ICP-Brasil do e-CNPJ A1 (DOC-ICP-04): <c>2.16.76.1.2.1.*</c>; A3 e A4 começam por <c>2.16.76.1.2.3</c> e <c>.4</c>.</summary>
    public const string A1PolicyPrefix = "2.16.76.1.2.1.";

    public const string IcpPolicyPrefix = "2.16.76.1.2.";

    /// <summary>CNPJ do titular no <c>otherName</c> da extensão SubjectAltName (DOC-ICP-04).</summary>
    public const string CnpjOtherNameOid = "2.16.76.1.3.3";

    /// <summary>Menor limiar alcançado pelos dias restantes (ex.: 20 dias → 30; 10 → 15; 3 → 7); acima de 30, nenhum.</summary>
    public static int? ThresholdFor(int daysLeft) => Thresholds.Where(t => daysLeft <= t).Select(t => (int?)t).LastOrDefault();

    /// <summary>CNPJ com o meio escondido (mensagem de outro CNPJ, CA 11): 11.222.***/****-81.</summary>
    public static string Mask(string cnpj) => cnpj.Length == 14 ? $"{cnpj[..2]}.{cnpj[2..5]}.***/****-{cnpj[12..]}" : "***";
}

/// <summary>O que o BemAqui guarda do certificado (D12): titular, CNPJ, thumbprint e validade.</summary>
public sealed record FiscalCertificateInfo(string Subject, string Document, string Thumbprint, DateTimeOffset NotBefore, DateTimeOffset NotAfter);

/// <summary>
/// Abre o <c>.pfx</c> só em memória, com <see cref="X509KeyStorageFlags.EphemeralKeySet"/> (a chave nunca vai para disco nem para o
/// repositório de chaves do sistema), e confere o que a FIS-02a CA 11 pede: chave privada, tipo A1, validade e o CNPJ do titular. Os
/// certificados abertos são descartados aqui; nada do arquivo nem da senha sai daqui.
/// </summary>
public static class FiscalCertificateInspector
{
    public const string OpenFailed = "Não foi possível abrir o certificado. Confira o arquivo .pfx e a senha.";
    public const string NoPrivateKey = "O arquivo não tem a chave privada. Exporte o certificado A1 com a chave privada (arquivo .pfx).";
    public const string NotA1 = "O certificado não é um e-CNPJ A1 da ICP-Brasil.";
    public const string NoDocument = "Não foi possível ler o CNPJ do certificado.";

    public static Result<FiscalCertificateInfo> Inspect(ReadOnlySpan<byte> pfx, Secret password, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(password);
        X509Certificate2Collection collection;
        try
        {
            collection = X509CertificateLoader.LoadPkcs12Collection(pfx, password.Reveal(), X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            return Invalid("password", OpenFailed);
        }

        try
        {
            var certificate = collection.FirstOrDefault(c => c.HasPrivateKey);
            if (certificate is null)
            {
                return Invalid("pfx", NoPrivateKey);
            }

            if (!IsA1(certificate))
            {
                return Invalid("pfx", NotA1);
            }

            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
            if (notAfter <= now)
            {
                return Invalid("pfx", $"O certificado venceu em {BusinessTime.ToBusiness(notAfter).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.");
            }

            if (notBefore > now)
            {
                return Invalid("pfx", "O certificado ainda não começou a valer.");
            }

            var document = CnpjOf(certificate);
            if (document is null)
            {
                return Invalid("pfx", NoDocument);
            }

            return new FiscalCertificateInfo(certificate.GetNameInfo(X509NameType.SimpleName, false), document, certificate.Thumbprint, notBefore, notAfter);
        }
        finally
        {
            foreach (var c in collection)
            {
                c.Dispose();
            }
        }
    }

    /// <summary>Tem a política ICP-Brasil do A1 (e nenhuma de A3/A4).</summary>
    public static bool IsA1(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var policies = Policies(certificate);
        return policies.Any(p => p.StartsWith(FiscalCertificateRules.A1PolicyPrefix, StringComparison.Ordinal))
            && !policies.Any(p => p.StartsWith(FiscalCertificateRules.IcpPolicyPrefix, StringComparison.Ordinal) && !p.StartsWith(FiscalCertificateRules.A1PolicyPrefix, StringComparison.Ordinal));
    }

    /// <summary>CNPJ do titular: o <c>otherName</c> 2.16.76.1.3.3 da SubjectAltName; na falta, o fim do CN ("RAZÃO:CNPJ").</summary>
    public static string? CnpjOf(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return FromAltName(certificate) ?? FromCommonName(certificate);
    }

    private static List<string> Policies(X509Certificate2 certificate)
    {
        var list = new List<string>();
        if (certificate.Extensions["2.5.29.32"] is not { } extension)
        {
            return list;
        }

        try
        {
            var infos = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (infos.HasData)
            {
                var info = infos.ReadSequence();
                list.Add(info.ReadObjectIdentifier());
            }
        }
        catch (AsnContentException)
        {
            list.Clear(); // extensão malformada: sem política conhecida
        }

        return list;
    }

    private static string? FromAltName(X509Certificate2 certificate)
    {
        if (certificate.Extensions["2.5.29.17"] is not { } extension)
        {
            return null;
        }

        var otherNameTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
        try
        {
            var names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (names.HasData)
            {
                if (!names.PeekTag().HasSameClassAndValue(otherNameTag))
                {
                    names.ReadEncodedValue();
                    continue;
                }

                var otherName = names.ReadSequence(otherNameTag);
                var type = otherName.ReadObjectIdentifier();
                var explicitValue = otherName.ReadSequence(otherNameTag);
                if (type != FiscalCertificateRules.CnpjOtherNameOid)
                {
                    continue;
                }

                var digits = Digits(TextOf(explicitValue));
                return digits.Length == 14 ? digits : null;
            }
        }
        catch (AsnContentException)
        {
            return null;
        }

        return null;
    }

    private static string TextOf(AsnReader value)
    {
        var tag = value.PeekTag();
        if (tag.TagClass != TagClass.Universal)
        {
            return string.Empty;
        }

        return (UniversalTagNumber)tag.TagValue switch
        {
            UniversalTagNumber.OctetString => Encoding.ASCII.GetString(value.ReadOctetString()),
            UniversalTagNumber.PrintableString or UniversalTagNumber.UTF8String or UniversalTagNumber.IA5String => value.ReadCharacterString((UniversalTagNumber)tag.TagValue),
            _ => string.Empty,
        };
    }

    private static string? FromCommonName(X509Certificate2 certificate)
    {
        var cn = certificate.GetNameInfo(X509NameType.SimpleName, false) ?? string.Empty;
        var colon = cn.LastIndexOf(':');
        var digits = colon < 0 ? string.Empty : Digits(cn[(colon + 1)..]);
        return digits.Length == 14 ? digits : null;
    }

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
