namespace BemAqui.BuildingBlocks.Security;

/// <summary>Dados públicos do certificado carregado (para avisos de vencimento; nunca a chave).</summary>
public sealed record ProtectionCertificateInfo(string Subject, string Thumbprint, DateTimeOffset NotAfter);
