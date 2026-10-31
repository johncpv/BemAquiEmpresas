using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.TaxRules;

/// <summary>
/// "Simular" (FIS-02b CA 7 a 11): monta a nota com a regra de cada item (resolvida como na emissão, ou a versão aberta na tela) e devolve
/// bases, impostos, totais, o que entra no custo do projeto e os bloqueios (DIFAL), sem gravar nada e sem chamar o EmissorFiscal. Política
/// de rate limit <see cref="RateLimitPolicy"/> (30 por minuto por usuário).
/// </summary>
public sealed class TaxSimulationService(IFiscalDb db, IFiscalSettings settings, ICompanyFiscalProfile company, TaxRuleResolver resolver, TimeProvider clock)
{
    public const string RateLimitPolicy = "fiscal-simulate";
    public const int MaxItems = 100;

    public async Task<Result<TaxSimulationView>> SimulateAsync(SimulateTaxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = request.Items ?? [];
        if (items.Count is 0 or > MaxItems)
        {
            return Error.Validation(TaxCalculator.InvalidItemCode, "Informe de 1 a 100 itens.", new Dictionary<string, string[]> { ["items"] = ["Informe de 1 a 100 itens."] });
        }

        if (!Enum.IsDefined(request.Operation) || !Enum.IsDefined(request.RecipientType))
        {
            return Error.Validation(Error.Codes.Validation, "Escolha a operação e o tipo de destinatário.");
        }

        var current = await settings.CurrentAsync(cancellationToken);
        var crt = FiscalSettings.CrtOf(current.Regime);
        var profile = await company.GetFiscalProfileAsync(cancellationToken);
        var issuerState = (profile.State ?? string.Empty).Trim().ToUpperInvariant();
        var recipient = new TaxRecipient((request.RecipientState ?? string.Empty).Trim().ToUpperInvariant(), request.RecipientType, request.FinalConsumer || request.RecipientType != RecipientType.Contributor);
        var normalized = items.Select(i => i with { Ncm = string.IsNullOrWhiteSpace(i.Ncm) ? null : FiscalSettingsRules.Digits(i.Ncm) }).ToList();
        var date = request.IssueDate ?? BusinessTime.Today(clock);
        var warnings = new List<string>();

        IReadOnlyList<TaxRule> rules;
        if (request.RuleVersionId is { } versionId)
        {
            var version = await db.TaxRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == versionId, cancellationToken);
            if (version is null)
            {
                return Error.NotFound(TaxRuleService.NotFoundCode, "Regra fiscal não encontrada.");
            }

            if (version.Operation != request.Operation)
            {
                warnings.Add($"A regra é de {TaxRuleRules.Label(version.Operation)}; a simulação usa a operação da regra.");
            }

            if (normalized.Any(i => !version.Conditions.Matches(recipient.State, recipient.Type, i.Ncm)) || !version.AppliesOn(date))
            {
                warnings.Add("As condições ou a vigência da regra não valem para este destinatário ou item. Na emissão, outra regra seria procurada.");
            }

            if (version.Status != TaxRuleStatus.Active)
            {
                warnings.Add("Simulação com a versão em rascunho: ela ainda não vale para a emissão.");
            }

            rules = [.. normalized.Select(_ => version)];
        }
        else
        {
            var resolved = await resolver.ResolveAsync(request.Operation, crt, recipient, normalized, date, current.Environment == FiscalEnvironment.Production, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Error!;
            }

            rules = resolved.Value;
        }

        foreach (var rule in rules.Where(r => !r.IsConfirmed).DistinctBy(r => r.Id))
        {
            warnings.Add($"A regra {rule.Name} ainda está a confirmar com a contadora.");
        }

        var calculation = TaxCalculator.Calculate(new TaxIssuer(issuerState, crt), recipient, normalized, rules);
        return calculation.IsFailure ? calculation.Error! : new TaxSimulationView(calculation.Value, issuerState, crt, warnings);
    }
}
