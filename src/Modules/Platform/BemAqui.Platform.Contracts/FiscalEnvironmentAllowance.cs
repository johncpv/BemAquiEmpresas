namespace BemAqui.Platform.Contracts;

/// <summary>
/// Até que ambiente da SEFAZ o tenant pode emitir (<c>catalog.tenants.fiscal_environment_allowed</c>, FIS-01 CA 9, ADR 0027 §6 e
/// ADR 0034): <see cref="None"/> por padrão (o <c>konebras</c> até a GOL-01b), <see cref="Homologation"/> no <c>demo</c> (notas sem valor
/// fiscal) e <see cref="Production"/> só ligada no checklist da GOL-01b. Produção inclui homologação.
/// </summary>
public enum FiscalEnvironmentAllowance
{
    None,
    Homologation,
    Production,
}
