using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BemAqui.BuildingBlocks.Security;

namespace BemAqui.Integration.Tests;

/// <summary>Certificados de teste para proteger o key ring, em arquivos temporários apagados ao fim do processo.</summary>
public static class TestCertificates
{
    public const string Password = "teste";

    private static readonly List<string> Files = [];

    static TestCertificates() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Files.ForEach(f => File.Delete(f));

    public static string Primary { get; } = Write("primary", ProtectionCertificate.CreateSelfSigned("CN=BemAqui teste primary", Password));

    public static string Other { get; } = Write("other", ProtectionCertificate.CreateSelfSigned("CN=BemAqui teste other", Password));

    /// <summary>Só o certificado público, sem chave privada: não consegue decifrar o key ring.</summary>
    public static string WithoutPrivateKey { get; } = Write("nokey", PublicOnly());

    /// <summary>RSA 2048, abaixo do mínimo de 3072.</summary>
    public static string Weak { get; } = Write("weak", WeakPfx());

    public static string PasswordFile { get; } = WriteText("password", Password + "\n");

    private static byte[] PublicOnly()
    {
        using var full = X509CertificateLoader.LoadPkcs12(ProtectionCertificate.CreateSelfSigned("CN=BemAqui teste nokey", Password), Password);
        using var publicOnly = X509CertificateLoader.LoadCertificate(full.Export(X509ContentType.Cert));
        return publicOnly.Export(X509ContentType.Pfx, Password);
    }

    private static byte[] WeakPfx()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=BemAqui teste weak", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return certificate.Export(X509ContentType.Pfx, Password);
    }

    private static string Write(string name, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bemaqui-dp-{name}-{Environment.ProcessId}.pfx");
        File.WriteAllBytes(path, content);
        Files.Add(path);
        return path;
    }

    private static string WriteText(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bemaqui-dp-{name}-{Environment.ProcessId}.txt");
        File.WriteAllText(path, content);
        Files.Add(path);
        return path;
    }
}
