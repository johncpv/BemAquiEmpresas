using System.Xml.Linq;
using BemAqui.BuildingBlocks.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace BemAqui.Host.Cli.Reencryption;

/// <summary>
/// Key ring antigo (o da homologação), aberto só dentro do comando de recifração (ADR 0034 §2): as chaves vêm de um export
/// (<c>security.data_protection_keys</c>, uma chave por linha ou um <c>.xml</c> por chave) e o certificado antigo decifra cada uma. Nada
/// é gravado: a chave que o Data Protection criar, se não houver uma ativa, fica só na memória, e o antigo nunca cifra nada.
/// </summary>
internal sealed class OldKeyRing : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _certificate;

    private OldKeyRing(ServiceProvider services, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, IReadOnlySet<Guid> keyIds)
    {
        _services = services;
        _certificate = certificate;
        KeyIds = keyIds;
        Protector = new SecretProtector(services.GetRequiredService<IDataProtectionProvider>());
    }

    public ISecretProtector Protector { get; }

    public IReadOnlySet<Guid> KeyIds { get; }

    public string CertificateThumbprint => _certificate.Thumbprint;

    public static OldKeyRing Open(string keysPath, string certificatePath, string passwordFile)
    {
        var elements = ReadElements(keysPath);
        var keyIds = KeyIdsOf(elements);
        if (keyIds.Count == 0)
        {
            throw new InvalidOperationException("Nenhuma chave do key ring antigo em '" + keysPath + "'. Exporte security.data_protection_keys do catálogo antigo (docs/operacao/go-live-scripts.md).");
        }

        if (!File.Exists(passwordFile))
        {
            throw new InvalidOperationException("Arquivo da senha do certificado antigo não encontrado: '" + passwordFile + "'.");
        }

        var certificate = ProtectionCertificate.LoadFile(certificatePath, File.ReadAllText(passwordFile).Trim());
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddDataProtection()
            .SetApplicationName(SecretsServiceCollectionExtensions.ApplicationName)
            .UnprotectKeysWithAnyCertificate(certificate);
        services.Configure<KeyManagementOptions>(o => o.XmlRepository = new MemoryOnlyRepository(elements));
        return new OldKeyRing(services.BuildServiceProvider(), certificate, keyIds);
    }

    /// <summary>Ids das chaves (<c>&lt;key id="..."&gt;</c>) de um conjunto de elementos do key ring.</summary>
    public static HashSet<Guid> KeyIdsOf(IEnumerable<XElement> elements) =>
        elements.Where(e => e.Name.LocalName == "key" && Guid.TryParse((string?)e.Attribute("id"), out _))
            .Select(e => Guid.Parse((string)e.Attribute("id")!))
            .ToHashSet();

    public void Dispose()
    {
        _services.Dispose();
        _certificate.Dispose();
    }

    private static List<XElement> ReadElements(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.GetFiles(path, "*.xml").Order(StringComparer.Ordinal).Select(f => XElement.Load(f)).ToList();
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Export do key ring antigo não encontrado: '" + path + "'.");
        }

        // Saída de psql -At: um elemento XML por linha.
        return File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => XElement.Parse(l)).ToList();
    }

    /// <summary>Repositório só em memória: lê o export e guarda aqui o que o Data Protection quiser gravar (nunca vai para disco nem banco).</summary>
    private sealed class MemoryOnlyRepository(IReadOnlyList<XElement> source) : IXmlRepository
    {
        private readonly List<XElement> _added = [];

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_added)
            {
                return [.. source.Select(e => new XElement(e)), .. _added.Select(e => new XElement(e))];
            }
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            lock (_added)
            {
                _added.Add(new XElement(element));
            }
        }
    }
}
