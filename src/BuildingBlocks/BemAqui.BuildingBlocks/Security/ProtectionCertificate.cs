using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>Certificado X.509 que protege o key ring (ADR 0007).</summary>
public static class ProtectionCertificate
{
    public const string DevelopmentFile = ".local/dataprotection-dev.pfx";
    private const string DevelopmentPassword = "bemaqui-dev";

    /// <summary>
    /// Carrega o certificado configurado. Sem configuração: em desenvolvimento gera um autoassinado
    /// local (fora do git); em qualquer outro ambiente falha com mensagem clara (fail fast).
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

            return LoadFile(options.CertificatePath, options.CertificatePassword);
        }

        if (environment.IsDevelopment())
        {
            var path = Path.Combine(environment.ContentRootPath, DevelopmentFile);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, CreateSelfSigned("CN=BemAqui Empresas DataProtection (dev)", DevelopmentPassword));
            }

            return LoadFile(path, DevelopmentPassword);
        }

        throw new InvalidOperationException(
            "DataProtection:CertificatePath não configurado. Fora de desenvolvimento o key ring exige o certificado de proteção " +
            "(ADR 0007); veja docs/operacao/segredos-e-chaves.md.");
    }

    /// <summary>Gera um .pfx autoassinado (RSA 3072, 10 anos). Usado em desenvolvimento, testes e no runbook.</summary>
    public static byte[] CreateSelfSigned(string subject, string password)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return certificate.Export(X509ContentType.Pfx, password);
    }

    public static X509Certificate2 LoadFile(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, KeyStorageFlags());

    public static X509Certificate2 LoadBytes(byte[] pfx, string? password) =>
        X509CertificateLoader.LoadPkcs12(pfx, password, KeyStorageFlags());

    // Em container Linux a chave fica só em memória; no Windows o CNG precisa do conjunto do usuário.
    private static X509KeyStorageFlags KeyStorageFlags() =>
        OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet;
}
