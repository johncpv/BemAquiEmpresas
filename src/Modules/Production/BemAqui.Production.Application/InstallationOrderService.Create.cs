using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class InstallationOrderService
{
    private static readonly string[] OpenProjectStatuses = ["Planned", "InProgress", "Fabricated", "Installing"];

    /// <summary>
    /// Criar OS de instalação (CA 2 a 5): itens do projeto (padrão: todos os livres), instalador, valor e datas; etapas e
    /// checklist copiados do modelo; título a pagar previsto do instalador na mesma transação, com a data estimada.
    /// </summary>
    public async Task<Result<InstallationDetail>> CreateAsync(InstallationCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var project = await projects.FindAsync(input.ProjectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound("project_not_found", "Projeto não encontrado.");
        }

        if (!OpenProjectStatuses.Contains(project.Status))
        {
            return Error.Conflict("project_not_open", "Só projeto planejado, em produção, fabricado ou em instalação recebe OS de instalação.");
        }

        var all = await projects.ItemsAsync(project.Id, cancellationToken);
        var coverage = await CoverageAsync(project.Id, cancellationToken);
        var chosen = input.ProjectItemIds is { Count: > 0 } ids ? all.Where(i => ids.Contains(i.Id)).ToList() : all.Where(i => !coverage.ContainsKey(i.Id)).ToList();

        // Sem escolha e com todos os itens cobertos, diz em qual OS está o primeiro (CA 5).
        if (chosen.Count == 0 && input.ProjectItemIds is not { Count: > 0 } && all.FirstOrDefault(i => coverage.ContainsKey(i.Id)) is { } covered)
        {
            return Invalid("projectItemIds", $"O item {covered.Description} já está na {coverage[covered.Id]}.");
        }

        if (chosen.Count == 0 || (input.ProjectItemIds is { Count: > 0 } asked && chosen.Count != asked.Count))
        {
            return Invalid("projectItemIds", "Escolha itens deste projeto.");
        }

        if (chosen.FirstOrDefault(i => coverage.ContainsKey(i.Id)) is { } taken)
        {
            return Invalid("projectItemIds", $"O item {taken.Description} já está na {coverage[taken.Id]}.");
        }

        if (!await parties.HasRoleAsync(input.InstallerId, PartyRoleKind.Installer, cancellationToken) || await parties.FindAsync(input.InstallerId, cancellationToken) is not { IsActive: true, IsDeleted: false } installer)
        {
            return Invalid("installerId", "Escolha um terceiro com o papel Instalador.");
        }

        if (input.ContractValue is <= 0 or > 9_999_999_999_999_999.99m)
        {
            return Invalid("contractValue", "Informe um valor combinado maior que zero.");
        }

        if (input.PlannedEnd < input.PlannedStart)
        {
            return Invalid("plannedEnd", "O fim previsto não pode ser antes do início.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = new ProductionOrder
        {
            Number = await numbers.NextAsync(DocType, cancellationToken), Type = ProductionOrderType.Installation, ProjectId = project.Id, ContractId = project.ContractId,
            InstallerId = installer.Id, ContractValue = input.ContractValue, PlannedStart = input.PlannedStart, PlannedEnd = input.PlannedEnd, CreatedAt = clock.GetUtcNow(),
            CreatedBy = currentUser.UserId,
        };
        db.ProductionOrders.Add(order);
        var model = await template.GetAsync(cancellationToken);
        for (var i = 0; i < model.Steps.Count; i++)
        {
            db.ProductionOrderSteps.Add(new ProductionOrderStep { OrderId = order.Id, Sequence = i + 1, Name = model.Steps[i] });
        }

        for (var i = 0; i < model.Checklist.Count; i++)
        {
            db.ProductionOrderChecklist.Add(new ProductionOrderChecklistItem { OrderId = order.Id, Sequence = i + 1, Text = model.Checklist[i] });
        }

        db.ProductionOrderProjectItems.AddRange(chosen.Select(i => new ProductionOrderProjectItem { OrderId = order.Id, ProjectItemId = i.Id }));
        var due = await InstallerDueAsync(installer, input.ContractValue, input.PlannedEnd, cancellationToken);
        var payable = await payables.CreateFromOriginAsync(DocumentOrigin.Installer, order.Id, installer.Id, project.Id, input.ContractValue, due, true,
            $"Instalação {order.Number} — {project.Number}", planned: true, cancellationToken);
        if (payable.IsFailure)
        {
            return payable.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(order.Id, cancellationToken);
    }

    /// <summary>Vencimento do instalador: a primeira parcela da condição padrão dele a partir da data base; sem condição, a própria data.</summary>
    private async Task<DateOnly> InstallerDueAsync(PartySummary installer, decimal amount, DateOnly baseDate, CancellationToken ct) =>
        installer.DefaultPaymentTermId is { } term && await terms.SplitAsync(term, amount, baseDate, ct) is { Count: > 0 } split ? split[0].DueDate : baseDate;

    private static Error Invalid(string field, string message) => InstallationTemplateService.Invalid(field, message);

    private DateOnly Today => BusinessTime.Today(clock);
}
