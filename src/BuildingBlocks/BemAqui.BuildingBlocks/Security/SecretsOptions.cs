namespace BemAqui.BuildingBlocks.Security;

/// <summary>Seção <c>DataProtection</c> da configuração.</summary>
public sealed class SecretsOptions
{
    public const string Section = "DataProtection";

    /// <summary>Arquivo .pfx que protege o key ring (secret do Docker, fora do banco e da imagem).</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Senha do .pfx (outro secret).</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Permite criar o key ring quando ele está vazio. Ligar só no primeiro provisionamento:
    /// com <c>false</c>, um key ring vazio (ex.: restauração sem as chaves) faz o Host recusar subir.
    /// </summary>
    public bool AllowKeyRingCreation { get; set; }

    /// <summary>Aplica a migration do key ring na subida (desenvolvimento e testes; ADR 0006).</summary>
    public bool AutoMigrate { get; set; }
}
