using System.Text.Json.Serialization;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>Tipo do parâmetro da manutenção (PAT-03b CA 5): conta do plano ou centro de custo, escolhidos pelo código.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetParameterKind>))]
public enum AssetParameterKind
{
    Account,
    CostCenter,
}

/// <summary>Um parâmetro da manutenção com o selo "A confirmar com a contadora" e quem conferiu e quando (ADR 0042).</summary>
public sealed record AssetParameterItem(
    string Key, string Label, string Description, AssetParameterKind Kind, string Code, string? CodeName, bool Confirmed, string? ConfirmedByName, DateTimeOffset? ConfirmedAt, Guid? ParameterId);

/// <summary>Opção de conta ou centro de custo (código e nome).</summary>
public sealed record AssetCodeOption(string Code, string Name);

/// <summary>
/// Configurações → Parâmetros → Patrimônio: o tamanho da etiqueta (PAT-01b) e as contas e os centros de custo da manutenção (PAT-03b CA 5),
/// com as opções do plano de contas e quantos ainda estão "A confirmar com a contadora".
/// </summary>
public sealed record AssetParametersView(
    int LabelWidthMm,
    int LabelHeightMm,
    Guid? LabelSizeId,
    IReadOnlyList<AssetParameterItem> Maintenance,
    IReadOnlyList<AssetCodeOption> Accounts,
    IReadOnlyList<AssetCodeOption> CostCenters,
    int PendingCount);

/// <summary>Troca de um parâmetro da manutenção: a chave e o código novo (conta ou centro de custo).</summary>
public sealed record AssetParameterChange(string Key, string? Code);

public sealed record AssetMaintenanceParametersInput(IReadOnlyList<AssetParameterChange>? Changes);

/// <summary>"Marcar como conferido" (ADR 0042 §1).</summary>
public sealed record AssetParameterConfirmInput(string Key);

/// <summary>Conta do plano e centro de custo da manutenção de um bem (CA 5): pelo centro de trabalho do bem.</summary>
public sealed record MaintenanceCostTarget(Guid AccountId, string AccountCode, Guid? CostCenterId, string? CostCenterCode);

/// <summary>
/// Parâmetros do Patrimônio (PAT-01b CA 1, PAT-03b CA 5) em <c>assets.parameters</c>: o tamanho da etiqueta (<c>assets.label_size</c>, seed 50 × 25 mm
/// até a Konebras informar a impressora, P-13) e as contas e os centros de custo da manutenção (2.6 "Outros custos" e o centro "Produção" para
/// bem com centro de trabalho; 3.3 "Administrativas" e "Administrativo" para os demais), "A confirmar com a contadora" até alguém conferir (ADR
/// 0042). Ler com <c>assets.view</c>; gravar e conferir com <c>settings.edit</c>, auditado (dono <c>settings</c>).
/// </summary>
public sealed class AssetParametersService(IAssetsDb db, IPermissionChecker permissions, IChartOfAccounts chart, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const string AccountMachine = "assets.maintenance_account_machine";
    public const string AccountOther = "assets.maintenance_account_other";
    public const string CostCenterMachine = "assets.maintenance_cost_center_machine";
    public const string CostCenterOther = "assets.maintenance_cost_center_other";
    public const string AccountMissingCode = "maintenance_account_missing";

    private sealed record Definition(string Key, string Label, string Description, AssetParameterKind Kind, string DefaultCode);

    private static readonly Definition[] Definitions =
    [
        new(AccountMachine, "Conta da manutenção de máquinas", "Conta do plano de contas do título do prestador e do custo da manutenção de bem com centro de trabalho (máquina da produção).", AssetParameterKind.Account, "2.6"),
        new(CostCenterMachine, "Centro de custo da manutenção de máquinas", "Centro de custo da manutenção de bem com centro de trabalho.", AssetParameterKind.CostCenter, "3"),
        new(AccountOther, "Conta da manutenção dos demais bens", "Conta do título e do custo da manutenção de bem sem centro de trabalho (escritório, veículo, TI).", AssetParameterKind.Account, "3.3"),
        new(CostCenterOther, "Centro de custo da manutenção dos demais bens", "Centro de custo da manutenção de bem sem centro de trabalho.", AssetParameterKind.CostCenter, "1"),
    ];

    public async Task<LabelSize> LabelSizeAsync(CancellationToken cancellationToken = default) =>
        AssetLabelRules.ParseOrDefault(await db.AssetParameters.AsNoTracking().Where(p => p.Key == AssetLabelRules.SizeKey).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken));

    public async Task<AssetParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var size = await LabelSizeAsync(cancellationToken);
        var id = await db.AssetParameters.AsNoTracking().Where(p => p.Key == AssetLabelRules.SizeKey).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        var keys = Definitions.Select(d => d.Key).ToList();
        var stored = await db.AssetParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        var accounts = (await chart.PostableAsync(AccountType.Expense, cancellationToken)).Select(a => new AssetCodeOption(a.Code, a.Name)).ToList();
        var centers = (await chart.CostCentersAsync(cancellationToken)).Where(c => c.IsActive).Select(c => new AssetCodeOption(c.Code, c.Name)).ToList();
        var reviewers = new Dictionary<Guid, string?>();
        foreach (var by in stored.Values.Select(p => p.ConfirmedBy).OfType<Guid>().Distinct())
        {
            reviewers[by] = await users.DisplayNameAsync(by, cancellationToken);
        }

        var items = Definitions.Select(d =>
        {
            var p = stored.GetValueOrDefault(d.Key);
            var code = p?.Value ?? d.DefaultCode;
            var name = (d.Kind == AssetParameterKind.Account ? accounts : centers).FirstOrDefault(o => o.Code == code)?.Name;
            return new AssetParameterItem(d.Key, d.Label, d.Description, d.Kind, code, name, p?.ConfirmedAt is not null, p?.ConfirmedBy is { } by ? reviewers.GetValueOrDefault(by) : null,
                p?.ConfirmedAt, p?.Id);
        }).ToList();
        return new AssetParametersView(size.WidthMm, size.HeightMm, id, items, accounts, centers, items.Count(i => !i.Confirmed));
    }

    public async Task<Result<AssetParametersView>> UpdateAsync(AssetParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ForbiddenAsync(cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        if (AssetLabelRules.SizeError(input.LabelWidthMm, input.LabelHeightMm) is { } message)
        {
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["labelWidthMm"] = [message], ["labelHeightMm"] = [message] });
        }

        var value = new LabelSize(input.LabelWidthMm, input.LabelHeightMm).ToString();
        var parameter = await db.AssetParameters.FirstOrDefaultAsync(p => p.Key == AssetLabelRules.SizeKey, cancellationToken);
        if (parameter is null)
        {
            db.AssetParameters.Add(new AssetParameter { Key = AssetLabelRules.SizeKey, Value = value, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != value)
        {
            parameter.Value = value;
            parameter.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>
    /// Contas e centros da manutenção (CA 5): só o que veio muda; o código precisa ser de conta de despesa analítica ativa ou de centro ativo;
    /// o valor trocado volta para "A confirmar com a contadora" (ADR 0042 §1).
    /// </summary>
    public async Task<Result<AssetParametersView>> UpdateMaintenanceAsync(AssetMaintenanceParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ForbiddenAsync(cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        var accounts = await chart.PostableAsync(AccountType.Expense, cancellationToken);
        var centers = await chart.CostCentersAsync(cancellationToken);
        foreach (var change in input.Changes ?? [])
        {
            if (Definitions.FirstOrDefault(d => d.Key == change.Key) is not { } definition)
            {
                return Error.NotFound("parameter_not_found", "Parâmetro não encontrado.");
            }

            var code = change.Code?.Trim();
            var valid = definition.Kind == AssetParameterKind.Account ? accounts.Any(a => a.Code == code) : centers.Any(c => c.Code == code && c.IsActive);
            if (string.IsNullOrEmpty(code) || !valid)
            {
                var message = definition.Kind == AssetParameterKind.Account ? "Escolha uma conta de despesa ativa do plano de contas." : "Escolha um centro de custo ativo.";
                return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [change.Key] = [message] });
            }

            var parameter = await db.AssetParameters.FirstOrDefaultAsync(p => p.Key == change.Key, cancellationToken);
            if (parameter is null)
            {
                db.AssetParameters.Add(new AssetParameter { Key = change.Key, Value = code, UpdatedAt = clock.GetUtcNow() });
            }
            else if ((parameter.Value ?? definition.DefaultCode) != code)
            {
                parameter.Value = code;
                parameter.UpdatedAt = clock.GetUtcNow();
                parameter.ResetConfirmation();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>"Marcar como conferido" (ADR 0042): grava o valor atual (o padrão, se ainda não gravado) com quem conferiu e quando.</summary>
    public async Task<Result<AssetParametersView>> ConfirmAsync(AssetParameterConfirmInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ForbiddenAsync(cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        if (Definitions.FirstOrDefault(d => d.Key == input.Key) is not { } definition)
        {
            return Error.NotFound("parameter_not_found", "Parâmetro não encontrado.");
        }

        var parameter = await db.AssetParameters.FirstOrDefaultAsync(p => p.Key == input.Key, cancellationToken);
        if (parameter is null)
        {
            parameter = new AssetParameter { Key = definition.Key, Value = definition.DefaultCode, UpdatedAt = clock.GetUtcNow() };
            db.AssetParameters.Add(parameter);
        }

        parameter.Confirm(currentUser.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>
    /// CA 5: a conta e o centro de custo da manutenção do bem: com centro de trabalho, os "de máquinas"; senão, os "dos demais bens". A conta
    /// precisa estar ativa (409 com o caminho do ajuste); o centro inativo ou apagado fica vazio (o título sai sem centro).
    /// </summary>
    public async Task<Result<MaintenanceCostTarget>> CostTargetAsync(bool hasWorkCenter, CancellationToken cancellationToken = default)
    {
        var (accountKey, centerKey) = hasWorkCenter ? (AccountMachine, CostCenterMachine) : (AccountOther, CostCenterOther);
        var stored = await db.AssetParameters.AsNoTracking().Where(p => p.Key == accountKey || p.Key == centerKey).ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
        var accountCode = stored.GetValueOrDefault(accountKey) ?? Definitions.First(d => d.Key == accountKey).DefaultCode;
        var centerCode = stored.GetValueOrDefault(centerKey) ?? Definitions.First(d => d.Key == centerKey).DefaultCode;
        var account = (await chart.PostableAsync(AccountType.Expense, cancellationToken)).FirstOrDefault(a => a.Code == accountCode);
        if (account is null)
        {
            return Error.Conflict(AccountMissingCode, $"A conta {accountCode} da manutenção não está ativa no plano de contas. Ajuste em Configurações → Parâmetros → Patrimônio.");
        }

        var center = (await chart.CostCentersAsync(cancellationToken)).FirstOrDefault(c => c.Code == centerCode && c.IsActive);
        return new MaintenanceCostTarget(account.Id, account.Code, center?.Id, center?.Code);
    }

    private async Task<Error?> ForbiddenAsync(CancellationToken ct) =>
        await permissions.HasPermissionAsync("settings", StandardActions.Edit, ct) ? null : Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
}
