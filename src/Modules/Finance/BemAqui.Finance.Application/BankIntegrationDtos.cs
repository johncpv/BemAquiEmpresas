using BemAqui.Finance.Application.Inter;

namespace BemAqui.Finance.Application;

/// <summary>Situação da conta na lista (CA da tela): Não conectada, Sandbox, Produção, Vence em breve, Vencido.</summary>
public static class BankIntegrationSituations
{
    public const string NotConnected = "NotConnected";
    public const string Sandbox = "Sandbox";
    public const string Production = "Production";
    public const string ExpiringSoon = "ExpiringSoon";
    public const string Expired = "Expired";

    /// <summary>Vence em até 30 dias: alerta na lista e "Trocar credenciais" direto (CA 15).</summary>
    public const int ExpiringSoonDays = 30;
}

/// <param name="IsInter">Banco 077: a conta pode ligar o Inter (aparece primeiro na lista).</param>
public sealed record BankIntegrationRow(Guid BankAccountId, string Name, string? BankCode, string? Agency, string? Account, bool IsInter, string Situation, string? Environment, DateTimeOffset? CertificateNotAfter);

public sealed record BankIntegrationList(IReadOnlyList<BankIntegrationRow> Items, bool ProductionAllowed);

/// <summary>Certificado sem a chave (CA 5): validade, titular, impressão digital SHA-256 e formato enviado.</summary>
public sealed record BankCertificateView(DateTimeOffset NotAfter, string Subject, string Thumbprint, string Format, int DaysToExpire);

public sealed record BankTestView(DateTimeOffset At, string Outcome, string? Message);

/// <summary>
/// Detalhe da integração da conta (CA 5): nunca traz client secret, certificado, chave ou senha; só "configurado em … por …", o client
/// id mascarado e o retrato do certificado. <see cref="Configured"/> falso: primeira configuração (vem com Sandbox e a conta sugerida).
/// </summary>
public sealed record BankIntegrationDetail(
    Guid BankAccountId,
    string AccountName,
    string? Agency,
    string? Account,
    bool Configured,
    string Situation,
    string Environment,
    string? ClientIdMasked,
    BankCertificateView? Certificate,
    string? AccountNumber,
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<string> MissingScopes,
    IReadOnlyList<string> RequiredScopes,
    DateTimeOffset? ConfiguredAt,
    string? ConfiguredByName,
    DateTimeOffset? RotatedAt,
    string? RotatedByName,
    BankTestView? LastTest,
    bool ProductionAllowed,
    Guid? IntegrationId,
    uint? Version);

/// <summary>Resultado do "Testar conexão" (CA 7): funcionando, ou o motivo e a ação; os escopos concedidos e os que faltam.</summary>
public sealed record BankConnectionTest(bool Ok, string Outcome, string Message, string? Action, IReadOnlyList<string> GrantedScopes, IReadOnlyList<string> MissingScopes, DateTimeOffset TestedAt);

/// <summary>
/// Campos do formulário "Salvar integração bancária" e "Trocar credenciais" (CA 3 e 14), já lidos do <c>multipart/form-data</c>.
/// Os segredos chegam aqui e saem cifrados: este registro não tem <c>ToString</c> com valores.
/// </summary>
public sealed record BankIntegrationCommand(
    string? Environment,
    string? ClientId,
    string? ClientSecret,
    string? AccountNumber,
    InterCertificateUpload Certificate,
    bool ConfirmProduction,
    uint? Version)
{
    public override string ToString() => "BankIntegrationCommand(***)";
}
