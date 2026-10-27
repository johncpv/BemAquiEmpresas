using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// Certificados gerados no próprio teste (FIS-02a CA 12): nunca há arquivo <c>.pfx</c> no repositório (a CI recusa). Imitam um e-CNPJ
/// ICP-Brasil: CN "RAZÃO:CNPJ", o <c>otherName</c> 2.16.76.1.3.3 com o CNPJ e a política do A1 (2.16.76.1.2.1.n) ou do A3.
/// </summary>
public static class TestCertificates
{
    public const string Password = "senha-canario-pfx-7319";
    public const string A1Policy = "2.16.76.1.2.1.51";
    public const string A3Policy = "2.16.76.1.2.3.8";

    public static byte[] Pfx(
        string cnpj = "47225005000186",
        string name = "EMPRESA A LTDA",
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        string? policy = A1Policy,
        bool privateKey = true,
        bool altName = true,
        string password = Password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}:{cnpj}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (altName)
        {
            request.CertificateExtensions.Add(new X509Extension("2.5.29.17", AltName(cnpj), false));
        }

        if (policy is not null)
        {
            request.CertificateExtensions.Add(new X509Extension("2.5.29.32", Policies(policy), false));
        }

        using var certificate = request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-30), notAfter ?? DateTimeOffset.UtcNow.AddYears(1));
        if (privateKey)
        {
            return certificate.Export(X509ContentType.Pkcs12, password);
        }

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pkcs12, password);
    }

    public static string Thumbprint(byte[] pfx, string password = Password)
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.EphemeralKeySet);
        return certificate.Thumbprint;
    }

    private static byte[] AltName(string cnpj)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            var otherName = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            using (writer.PushSequence(otherName))
            {
                writer.WriteObjectIdentifier("2.16.76.1.3.3");
                using (writer.PushSequence(otherName))
                {
                    writer.WriteOctetString(Encoding.ASCII.GetBytes(cnpj));
                }
            }
        }

        return writer.Encode();
    }

    private static byte[] Policies(string policy)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(policy);
            }
        }

        return writer.Encode();
    }
}
