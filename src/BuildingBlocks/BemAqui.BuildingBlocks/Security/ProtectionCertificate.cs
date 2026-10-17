using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>Certificado X.509 que protege o key ring (ADR 0007).</summary>
public static class ProtectionCertificate
{
    public const string DevelopmentFile = ".local/dataprotection-dev.pfx";
    public const int MinimumKeySize = 3072;
    private const string DevelopmentPassword = "bemaqui-dev";

    /// <summary>
    /// Carrega e valida o certificado configurado. Sem configuração, só gera um autoassinado local com opt-in
    /// explícito, em desenvolvimento e fora de container; em qualquer outro caso falha com mensagem clara.
    /// </summary>
    public static X509Certificate2 Load(SecretsOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            if (!File.Exists(options.CertificatePath))
            {
                throw new InvalidOperationException(
                    $"Certificado de proteção do key ring não encontrado em '{options.CertificatePath}' (DataProtection:CertificatePath). " +
                    "Monte o secret do certificado; veja docs/operacao/segredos-e-chaves.md.");
            }

            return Validate(LoadFile(options.CertificatePath, Password(options)), options.CertificatePath);
        }

        var inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
        if (environment.IsDevelopment() && options.GenerateDevelopmentCertificate && !inContainer)
        {
            var path = Path.Combine(environment.ContentRootPath, DevelopmentFile);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, CreateSelfSigned("CN=BemAqui Empresas DataProtection (dev)", DevelopmentPassword));
            }

            return Validate(LoadFile(path, DevelopmentPassword), path);
        }

        throw new InvalidOperationException(
            "DataProtection:CertificatePath não configurado. O key ring exige o certificado de proteção (ADR 0007); " +
            "em desenvolvimento local ligue DataProtection:GenerateDevelopmentCertificate. Veja docs/operacao/segredos-e-chaves.md.");
    }

    /// <summary>Gera um .pfx autoassinado (RSA 3072, 10 anos, sem CA). Usado em desenvolvimento, testes e no runbook.</summary>
    public static byte[] CreateSelfSigned(string subject, string password)
    {
        using var rsa = RSA.Create(MinimumKeySize);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return certificate.Export(X509ContentType.Pfx, password);
    }

    public static X509Certificate2 LoadFile(string path, string? password)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, password, KeyStorageFlags());
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException($"Não foi possível abrir o certificado '{path}': senha errada ou arquivo inválido.", ex);
        }
    }

    public static X509Certificate2 LoadBytes(byte[] pfx, string? password) =>
        X509CertificateLoader.LoadPkcs12(pfx, password, KeyStorageFlags());

    /// <summary>O certificado precisa ter chave privada RSA de pelo menos 3072 bits (sem ela, as chaves não decifram).</summary>
    internal static X509Certificate2 Validate(X509Certificate2 certificate, string source)
    {
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"O certificado '{source}' não tem chave privada RSA: ele não consegue decifrar o key ring.");
        }

        if (rsa.KeySize < MinimumKeySize)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"O certificado '{source}' usa RSA {rsa.KeySize}; o mínimo é {MinimumKeySize} bits.");
        }

        return certificate;
    }

    private static string? Password(SecretsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePasswordFile))
        {
            return options.CertificatePassword;
        }

        if (!File.Exists(options.CertificatePasswordFile))
        {
            throw new InvalidOperationException($"Arquivo da senha do certificado não encontrado em '{options.CertificatePasswordFile}' (DataProtection:CertificatePasswordFile).");
        }

        return File.ReadAllText(options.CertificatePasswordFile).Trim();
    }

    // Em container Linux a chave fica só em memória; no Windows o CNG precisa do conjunto do usuário.
    private static X509KeyStorageFlags KeyStorageFlags() =>
        OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet;
}
