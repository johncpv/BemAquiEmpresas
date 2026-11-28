using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>% padrão de markup ou margem (COM-01a CA 12, <c>pricing.default_pct</c>): vazio no seed; editado com <c>settings.edit</c>.</summary>
public sealed class CommercialParametersService(
    ICommercialDb db, ITenantParameters tenantParameters, IPermissionChecker permissions, IUserDirectory users, ICurrentUser currentUser, IStepUpTwoFactor stepUp,
    INotificationService notifications, TimeProvider clock) : ICommercialParameters
{
    public const string DefaultPct = "pricing.default_pct";
    public const string ValidityDays = "quotes.validity_days";

    /// <summary>EXE-01a CA 14: segregação entre quem registra a entrega e quem registra o aceite (1 ligada; ausente ou 0, desligada).</summary>
    public const string AcceptanceSegregation = "projects.acceptance_segregation";

    /// <summary>
    /// EXE-03b CA 5: autoaprovação da etapa (quem concluiu pode aprovar), com a codificação da FIN-10a (0 Nunca, 1 Só o Proprietário, 2 Qualquer
    /// aprovador); ausente, Só o Proprietário (decisão do mantenedor de 28/11/2026).
    /// </summary>
    public const string StageSelfApproval = "projects.stage_self_approval";

    public const string StageSelfApprovalQuestion = "Quem concluiu a etapa pode aprová-la?";

    /// <summary>EXE-01a CA 5: marca por tenant do ajuste automático do tipo do projeto; presente, o job termina sem ler nada.</summary>
    public const string ExecutionKindBackfillDone = "backfill.execution_kind_done";

    public async Task<bool> AcceptanceSegregationAsync(CancellationToken cancellationToken = default) =>
        await db.CommercialParameters.AsNoTracking().Where(p => p.Key == AcceptanceSegregation).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken) is > 0m;

    /// <summary>Modo gravado; ausente, Só o Proprietário; valor desconhecido, Nunca (falha fechada, como a FIN-10a).</summary>
    public async Task<SelfApprovalMode> StageSelfApprovalAsync(CancellationToken cancellationToken = default) =>
        StageModeOf(await db.CommercialParameters.AsNoTracking().Where(p => p.Key == StageSelfApproval).Select(p => (decimal?)(p.Value ?? -1m)).FirstOrDefaultAsync(cancellationToken));

    public static SelfApprovalMode StageModeOf(decimal? stored) => stored switch
    {
        null => SelfApprovalMode.OwnerOnly,
        0m => SelfApprovalMode.Never,
        1m => SelfApprovalMode.OwnerOnly,
        2m => SelfApprovalMode.AnyApprover,
        _ => SelfApprovalMode.Never,
    };

    public static string StageModeLabel(SelfApprovalMode mode) => mode switch
    {
        SelfApprovalMode.OwnerOnly => "Só o Proprietário",
        SelfApprovalMode.AnyApprover => "Qualquer aprovador",
        _ => "Nunca",
    };

    public async Task<int> ValidityDaysAsync(CancellationToken cancellationToken = default) =>
        (int)(await db.CommercialParameters.AsNoTracking().Where(p => p.Key == ValidityDays).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken) ?? 15m);

    public async Task<decimal?> DefaultPricingPctAsync(CancellationToken cancellationToken = default) =>
        await db.CommercialParameters.AsNoTracking().Where(p => p.Key == DefaultPct).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken);

    public async Task<CommercialParametersView> GetAsync(CancellationToken cancellationToken = default) =>
        new(await DefaultPricingPctAsync(cancellationToken), (await tenantParameters.PricingModeAsync(cancellationToken)).ToString(), await ValidityDaysAsync(cancellationToken),
            await AcceptanceSegregationAsync(cancellationToken), await StageSelfApprovalAsync(cancellationToken));

    public async Task<Result<CommercialParametersView>> UpdateAsync(CommercialParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (input.DefaultPct is < 0 or > 1000)
        {
            return QuoteService.Invalid("defaultPct", "Informe um percentual de 0% a 1.000%.");
        }

        if (input.DefaultPct is >= 100 && await tenantParameters.PricingModeAsync(cancellationToken) == PricingMode.Margin)
        {
            return QuoteService.Invalid("defaultPct", "A margem precisa ser menor que 100%.");
        }

        if (input.ValidityDays is < 1 or > 365)
        {
            return QuoteService.Invalid("validityDays", "Informe de 1 a 365 dias.");
        }

        if (input.StageSelfApprovalMode is { } asked && !Enum.IsDefined(asked))
        {
            return QuoteService.Invalid("stageSelfApprovalMode", "Escolha uma das opções.");
        }

        var stored = await StageSelfApprovalAsync(cancellationToken);
        var modeChanged = input.StageSelfApprovalMode is { } wanted && wanted != stored;
        if (modeChanged && await ModeChangeErrorAsync(input, stored, cancellationToken) is { } refused)
        {
            return refused;
        }

        await SetAsync(ValidityDays, input.ValidityDays, cancellationToken);
        if (modeChanged)
        {
            var mode = input.StageSelfApprovalMode!.Value;
            await SetAsync(StageSelfApproval, (int)mode, cancellationToken);
            var me = currentUser.UserId;
            var who = me is { } id ? await users.DisplayNameAsync(id, cancellationToken) ?? "Um usuário" : "O sistema";
            var title = $"{who} mudou \"{StageSelfApprovalQuestion}\" de {StageModeLabel(stored)} para {StageModeLabel(mode)}.";
            foreach (var owner in (await users.OwnerIdsAsync(cancellationToken)).Where(o => o != me))
            {
                notifications.Notify(owner, "commercial.parameters", title, "/app/configuracoes/parametros");
            }
        }

        if (input.AcceptanceSegregation is { } segregation)
        {
            await SetAsync(AcceptanceSegregation, segregation ? 1m : 0m, cancellationToken);
        }

        var parameter = await db.CommercialParameters.FirstOrDefaultAsync(p => p.Key == DefaultPct, cancellationToken);
        if (parameter is null)
        {
            db.CommercialParameters.Add(new CommercialParameter { Key = DefaultPct, Value = input.DefaultPct, UpdatedAt = clock.GetUtcNow() });
        }
        else
        {
            parameter.Value = input.DefaultPct is { } v ? Math.Round(v, 4, MidpointRounding.AwayFromZero) : null;
            parameter.UpdatedAt = clock.GetUtcNow();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (modeChanged && (ex is DbUpdateConcurrencyException || DbErrors.IsUniqueViolation(ex)))
        {
            return Error.Conflict(ChangedCode, ChangedMessage);
        }

        return await GetAsync(cancellationToken);
    }

    public const string ChangedCode = "parameters_changed";
    public const string ChangedMessage = "Outra pessoa mudou os parâmetros. Recarregue e tente de novo.";

    /// <summary>
    /// Trocar o modo da etapa (EXE-03b CA 5), com as regras da FIN-10a nesta ordem: é do Proprietário; o modo visto ainda é o gravado; tem 2FA ativo;
    /// mandou o código; o código vale e não foi usado.
    /// </summary>
    private async Task<Error?> ModeChangeErrorAsync(CommercialParametersInput input, SelfApprovalMode stored, CancellationToken ct)
    {
        if (currentUser.UserId is not { } me || !(await users.OwnerIdsAsync(ct)).Contains(me))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só o Proprietário muda esta regra.");
        }

        if (input.StageSelfApprovalModeSeen is { } seen && seen != stored)
        {
            return Error.Conflict(ChangedCode, ChangedMessage);
        }

        if (!await stepUp.IsEnrolledAsync(me, ct))
        {
            return Error.Forbidden("two_factor_required", "Para continuar, ative a verificação em duas etapas.") with { Data = new Dictionary<string, object?> { ["enrolled"] = false } };
        }

        if (string.IsNullOrWhiteSpace(input.Code))
        {
            return Error.Forbidden("two_factor_required", "Confirme com o código do autenticador.") with { Data = new Dictionary<string, object?> { ["enrolled"] = true } };
        }

        return await stepUp.VerifyAsync(me, input.Code, ct) ? null : Error.Forbidden("two_factor_invalid", "Código inválido ou já usado. Peça um novo.");
    }

    private async Task SetAsync(string key, decimal? value, CancellationToken ct)
    {
        var parameter = await db.CommercialParameters.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (parameter is null)
        {
            db.CommercialParameters.Add(new CommercialParameter { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != value)
        {
            parameter.Value = value;
            parameter.UpdatedAt = clock.GetUtcNow();
        }
    }
}
