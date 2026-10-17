namespace BemAqui.BuildingBlocks.Security;

/// <summary>Seção <c>DataProtection</c> da configuração.</summary>
public sealed class SecretsOptions
{
    public const string Section = "DataProtection";

    /// <summary>Arquivo .pfx que protege o key ring (secret do Docker, fora do banco e da imagem).</summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Arquivo com a senha do .pfx (secret do Docker, ex.: <c>/run/secrets/dp-cert-password</c>). Preferível a
    /// <see cref="CertificatePassword"/>, que acaba em variável de ambiente visível em <c>docker inspect</c>.
    /// </summary>
    public string? CertificatePasswordFile { get; set; }

    /// <summary>Senha do .pfx em texto (só desenvolvimento e testes).</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Permite criar o key ring quando ele está vazio. Ligar só no primeiro provisionamento: com <c>false</c>,
    /// um key ring vazio (ex.: restauração sem as chaves) faz o Host recusar subir. Ligada, toda subida avisa no log.
    /// </summary>
    public bool AllowKeyRingCreation { get; set; }

    /// <summary>Aplica a migration do key ring na subida (desenvolvimento e testes; ADR 0006).</summary>
    public bool AutoMigrate { get; set; }

    /// <summary>
    /// Gera um certificado autoassinado local quando não há <see cref="CertificatePath"/>. Opt-in, só em
    /// desenvolvimento e nunca dentro de container (evita key ring novo em silêncio por ambiente errado).
    /// </summary>
    public bool GenerateDevelopmentCertificate { get; set; }
}
