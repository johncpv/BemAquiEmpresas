using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application.Inter;

/// <summary>Arquivos do certificado como vieram do formulário (nunca gravados em disco, anexos ou <c>IFileStorage</c>, CA 4).</summary>
public sealed record InterCertificateUpload(byte[]? Certificate, byte[]? PrivateKey, byte[]? Pfx, string? PfxPassword)
{
    public bool HasAny => Certificate is { Length: > 0 } || PrivateKey is { Length: > 0 } || Pfx is { Length: > 0 };

    public override string ToString() => "InterCertificateUpload(***)";
}

/// <summary>
/// Certificado conferido (CA 4): o retrato público e o material a cifrar (PEM do certificado e da chave, ou o <c>.pfx</c> em base64
/// com a senha). O <see cref="Certificate"/> traz a chave só em memória e é descartado por quem usa.
/// </summary>
public sealed class ValidatedInterCertificate(BankCertificateInfo info, X509Certificate2 certificate, string storedCertificate, string? storedPrivateKey, string? password) : IDisposable
{
    public BankCertificateInfo Info { get; } = info;

    public X509Certificate2 Certificate { get; } = certificate;

    public string StoredCertificate { get; } = storedCertificate;

    public string? StoredPrivateKey { get; } = storedPrivateKey;

    public string? Password { get; } = password;

    public override string ToString() => "ValidatedInterCertificate(***)";

    public void Dispose() => Certificate.Dispose();
}

/// <summary>
/// Validação do certificado do mTLS do Inter antes de gravar (BNK-01a CA 4): o arquivo abre (<c>.pfx</c> com
/// <see cref="X509KeyStorageFlags.EphemeralKeySet"/>), a chave privada é a do certificado, não venceu, tem pelo menos 2048 bits e,
/// quando o assunto traz um CNPJ, é o do tenant. Recusa com 422 em PT-BR.
/// </summary>
public static partial class InterCertificateValidator
{
    public const int MaxFileBytes = 20 * 1024;
    public const int MinRsaBits = 2048;
    public const int MinEcBits = 256;

    public const string MissingFiles = "Envie o certificado (.crt) e a chave privada (.key), ou o arquivo .pfx com a senha.";
    public const string TooLarge = "Cada arquivo do certificado pode ter até 20 KB.";
    public const string UnreadableCertificate = "Não foi possível ler o certificado. Envie o arquivo .crt (ou .pem) que o Inter gerou.";
    public const string UnreadableKey = "Não foi possível ler a chave privada. Envie o arquivo .key em PEM, sem senha.";
    public const string EncryptedKey = "A chave privada está protegida por senha. Envie a chave sem senha ou o .pfx com a senha.";
    public const string KeyMismatch = "A chave privada não corresponde a este certificado.";
    public const string PfxUnreadable = "Não foi possível abrir o .pfx. Confira o arquivo e a senha.";
    public const string PfxWithoutKey = "O arquivo .pfx não traz a chave privada.";
    public const string WeakKey = "A chave do certificado tem menos de 2048 bits. Gere um certificado novo no Inter.";

    [GeneratedRegex(@"(?<!\d)(\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})(?!\d)")]
    private static partial Regex CnpjInText();

    public static Result<ValidatedInterCertificate> Validate(InterCertificateUpload upload, string? tenantCnpj, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var pem = upload.Certificate is { Length: > 0 } || upload.PrivateKey is { Length: > 0 };
        var pfx = upload.Pfx is { Length: > 0 };
        if (pem == pfx || (pem && (upload.Certificate is not { Length: > 0 } || upload.PrivateKey is not { Length: > 0 })))
        {
            return Invalid("certificate", MissingFiles);
        }

        if (new[] { upload.Certificate, upload.PrivateKey, upload.Pfx }.Any(f => f is { Length: > MaxFileBytes }))
        {
            return Invalid("certificate", TooLarge);
        }

        var loaded = pfx ? LoadPfx(upload.Pfx!, upload.PfxPassword) : LoadPem(upload.Certificate!, upload.PrivateKey!);
        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var (certificate, storedCertificate, storedKey) = loaded.Value;
        var refused = Check(certificate, tenantCnpj, now);
        if (refused is not null)
        {
            certificate.Dispose();
            return refused;
        }

        var info = new BankCertificateInfo(pfx ? BankCertificateFormat.Pfx : BankCertificateFormat.Pem, NotAfter(certificate), Thumbprint(certificate), certificate.Subject);
        return new ValidatedInterCertificate(info, certificate, storedCertificate, storedKey, pfx ? upload.PfxPassword : null);
    }

    /// <summary>Abre o certificado gravado (decifrado), só em memória, para montar o handler (CA 9).</summary>
    public static X509Certificate2 Load(BankCertificateFormat format, string storedCertificate, string? storedPrivateKey, string? password) =>
        format == BankCertificateFormat.Pfx
            ? X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(storedCertificate), password, X509KeyStorageFlags.EphemeralKeySet)
            : X509Certificate2.CreateFromPem(storedCertificate, storedPrivateKey ?? throw new InvalidOperationException("Integração PEM sem chave privada."));

    /// <summary>Impressão digital SHA-256 em hex minúsculo (a SHA-1 do <c>Thumbprint</c> não serve de chave).</summary>
    public static string Thumbprint(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
    }

    public static DateTimeOffset NotAfter(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
    }

    public static string Date(DateTimeOffset at) => BusinessTime.ToBusiness(at).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static Error? Check(X509Certificate2 certificate, string? tenantCnpj, DateTimeOffset now)
    {
        var notAfter = NotAfter(certificate);
        if (notAfter <= now)
        {
            return Invalid("certificate", $"O certificado venceu em {Date(notAfter)}.");
        }

        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
        if (notBefore > now)
        {
            return Invalid("certificate", $"O certificado só vale a partir de {Date(notBefore)}.");
        }

        var bits = certificate.GetRSAPublicKey()?.KeySize ?? certificate.GetECDsaPublicKey()?.KeySize ?? 0;
        var minimum = certificate.GetRSAPublicKey() is not null ? MinRsaBits : MinEcBits;
        if (bits < minimum)
        {
            return Invalid("certificate", WeakKey);
        }

        var tenant = Digits(tenantCnpj);
        if (tenant.Length == 14)
        {
            var found = CnpjInText().Matches(certificate.Subject).Select(m => Digits(m.Value)).Where(d => d.Length == 14).Distinct(StringComparer.Ordinal).ToList();
            if (found.Count > 0 && !found.Contains(tenant, StringComparer.Ordinal))
            {
                return Invalid("certificate", $"O certificado é de outro CNPJ ({Format(found[0])}). Use o certificado da conta da empresa.");
            }
        }

        return null;
    }

    private static Result<(X509Certificate2 Certificate, string StoredCertificate, string? StoredKey)> LoadPfx(byte[] pfx, string? password)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            return Invalid("pfx", PfxUnreadable);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            return Invalid("pfx", PfxWithoutKey);
        }

        return (certificate, Convert.ToBase64String(pfx), null);
    }

    private static Result<(X509Certificate2 Certificate, string StoredCertificate, string? StoredKey)> LoadPem(byte[] certificateBytes, byte[] keyBytes)
    {
        X509Certificate2 publicOnly;
        try
        {
            var text = Encoding.ASCII.GetString(certificateBytes);
            publicOnly = text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(text)
                : X509CertificateLoader.LoadCertificate(certificateBytes);
        }
        catch (CryptographicException)
        {
            return Invalid("certificate", UnreadableCertificate);
        }

        using (publicOnly)
        {
            var keyText = Encoding.ASCII.GetString(keyBytes);
            if (keyText.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal) || keyText.Contains("Proc-Type: 4,ENCRYPTED", StringComparison.Ordinal))
            {
                return Invalid("privateKey", EncryptedKey);
            }

            AsymmetricAlgorithm key;
            try
            {
                key = ImportKey(keyText);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                return Invalid("privateKey", UnreadableKey);
            }

            using (key)
            {
                var keyPublic = key switch
                {
                    RSA rsa => rsa.ExportSubjectPublicKeyInfo(),
                    ECDsa ec => ec.ExportSubjectPublicKeyInfo(),
                    _ => [],
                };
                if (!CryptographicOperations.FixedTimeEquals(publicOnly.PublicKey.ExportSubjectPublicKeyInfo(), keyPublic))
                {
                    return Invalid("privateKey", KeyMismatch);
                }

                X509Certificate2 withKey;
                try
                {
                    withKey = key is RSA r ? publicOnly.CopyWithPrivateKey(r) : publicOnly.CopyWithPrivateKey((ECDsa)key);
                }
                catch (CryptographicException)
                {
                    return Invalid("privateKey", KeyMismatch);
                }

                var keyPem = key is RSA rk ? rk.ExportPkcs8PrivateKeyPem() : ((ECDsa)key).ExportPkcs8PrivateKeyPem();
                return (withKey, publicOnly.ExportCertificatePem(), keyPem);
            }
        }
    }

    private static AsymmetricAlgorithm ImportKey(string pem)
    {
        if (pem.Contains("EC PRIVATE KEY", StringComparison.Ordinal))
        {
            var ec = ECDsa.Create();
            ec.ImportFromPem(pem);
            return ec;
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (CryptographicException)
        {
            rsa.Dispose();
            var ec = ECDsa.Create();
            try
            {
                ec.ImportFromPem(pem);
                return ec;
            }
            catch
            {
                ec.Dispose();
                throw;
            }
        }
    }

    private static string Digits(string? text) => new((text ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static string Format(string cnpj) => $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

    private static Error Invalid(string field, string message) =>
        Error.Validation("bank_certificate_invalid", message, new Dictionary<string, string[]> { [field] = [message] });
}
