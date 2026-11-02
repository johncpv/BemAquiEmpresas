using System.Reflection;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// CAD-01a CA 24 (fail-closed): toda propriedade de entidade auditável do Parties é [PersonalData] ou está na lista de
/// não sensíveis. Propriedade nova sem decisão quebra o teste: assim nenhum dado pessoal entra na auditoria em claro.
/// </summary>
public class PersonalDataTests
{
    private static readonly Dictionary<string, string[]> NonSensitive = new(StringComparer.Ordinal)
    {
        ["Party"] = ["Id", "Kind", "LegalName", "TradeName", "SearchText", "StateRegistration", "MunicipalRegistration", "TaxRegime", "IsActive", "DefaultPaymentTermId", "ImportSource", "CreatedAt", "IsDeleted", "DeletedAt", "Roles", "Addresses", "Contacts",
            // LGPD-01b: a data e o número do pedido da anonimização são marcas técnicas, sem dado do titular.
            "AnonymizedAt", "AnonymizationRequest"],
        ["PartyRole"] = ["Id", "PartyId", "Role"],
        ["PartyAddress"] = ["Id", "PartyId", "Kind", "ZipCode", "Street", "Number", "Complement", "District", "City", "IbgeCode", "State", "IsDefault"],
        ["PartyContact"] = ["Id", "PartyId", "Role", "ReceivesBilling"],
    };

    public static IReadOnlyList<string> Violations(IEnumerable<Type> auditable, IReadOnlyDictionary<string, string[]> allowed) =>
        auditable.SelectMany(type => allowed.TryGetValue(type.Name, out var safe)
                ? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is null && !safe.Contains(p.Name))
                    .Select(p => $"{type.Name}.{p.Name}: marque [PersonalData] ou inclua na lista de não sensíveis")
                : [$"{type.Name}: entidade auditável sem lista de não sensíveis"])
            .ToList();

    [Fact]
    public void Entidades_auditaveis_do_Parties_so_tem_dado_pessoal_marcado()
    {
        var domain = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.Parties.Domain");
        var auditable = domain.GetTypes().Where(t => t.IsClass && typeof(IAuditable).IsAssignableFrom(t)).ToList();

        auditable.Should().NotBeEmpty();
        Violations(auditable, NonSensitive).Should().BeEmpty();
    }

    /// <summary>CAD-04a CA 14: o mesmo fail-closed no HR, e os DTOs de lista e resumo sem nenhuma propriedade de salário.</summary>
    private static readonly Dictionary<string, string[]> HrNonSensitive = new(StringComparer.Ordinal)
    {
        ["JobPosition"] = ["Id", "Name", "Cbo", "DefaultWorkCenterId", "WorkScheduleId", "MonthlyHours", "HourlyCost", "HourlyCostManual", "IsActive", "CreatedAt", "IsDeleted", "DeletedAt"],
        ["WorkSchedule"] = ["Id", "Name", "Days", "WeeklyHours", "IsActive", "SeedKey", "CreatedAt", "IsDeleted", "DeletedAt"],
        ["LaborUnion"] = ["Id", "Name", "Document", "BaseMonth", "IsActive", "CreatedAt", "IsDeleted", "DeletedAt"],
        // CAD-04c2: banco, Pix, titular, motivo da divergência, adicionais, vale-transporte, CNH, reservista e o e-mail anterior são
        // [PersonalData]; só as marcas da conta (troca, quarentena, revisão e versão, sem dado bancário, expostas no IEmployees) e a
        // data da troca do e-mail ficam fora.
        ["Employee"] = ["Id", "Name", "Registration", "PositionId", "WorkScheduleId", "UnionId", "AdmissionDate", "TerminationDate", "Status", "CreatedAt", "Version", "IsDeleted", "DeletedAt",
            "EmailChangedAt", "BankOwnerMismatch", "BankChangedAt", "BankChangedBy", "BankQuarantineUntil", "BankReviewedAt", "BankReviewedBy", "BankSelfReviewed", "BankAccountVersion",
            // LGPD-01b: a data e o número do pedido da anonimização são marcas técnicas, sem dado do titular.
            "AnonymizedAt", "AnonymizationRequest"],
        // CAD-04c2: dependente (pode ser menor) é todo dado pessoal; só as chaves, a criação e as marcas técnicas ficam fora.
        ["EmployeeDependent"] = ["Id", "EmployeeId", "CreatedAt", "Version", "IsDeleted", "DeletedAt"],
        // CAD-04c1: tipo, fim previsto e motivo do afastamento são [PersonalData] (LGPD art. 11); só as marcas de publicação ficam fora.
        // CAD-04c1b: o vínculo da mudança com o afastamento, o que mudou e a marca do cancelamento não são dado pessoal; o tipo
        // anterior é.
        ["EmployeeHistoryEntry"] = ["Id", "EmployeeId", "Kind", "EffectiveDate", "UserId", "CreatedAt", "StartPublishedAt", "EndPublishedAt", "LeaveId", "LeaveChange", "CancelledAt"],
        // RH-02a: a rubrica, a folha (competência, tipo, situação, quem fechou) e o parâmetro não são dado pessoal; no holerite, na linha e
        // na guia, todo valor, base, referência e o FGTS do mês são [PersonalData] (CA 13); só as chaves, a rubrica e as datas ficam fora.
        ["PayrollItem"] = ["Id", "Code", "Name", "Kind", "InssBase", "FgtsBase", "IrrfBase", "IsActive", "SeedKey", "ConfirmedAt", "ConfirmedBy", "CreatedAt", "Version", "IsDeleted", "DeletedAt", "TimesheetSource",
            "Calculation", "DeductsIrrfBase"],
        // RH-02c: a data de pagamento, quantos títulos, o pago e a reabertura não são dado pessoal; os totais congelados são (ClosedNet, ClosedCharges).
        ["PayrollRun"] = ["Id", "Period", "Type", "Mode", "Status", "Number", "CreatedAt", "CreatedBy", "ClosedAt", "ClosedBy", "Version",
            "PaymentDate", "ClosedPayables", "PaidAt", "ReopenedAt", "ReopenedBy",
            // FLH-02a: quando e com qual versão do motor calculou, a simulação e a retificação não são dado pessoal.
            "CalculatedAt", "CalculatedBy", "CalculatorVersion", "CalculatingSince", "IsSimulation", "RectifiesRunId"],
        // RH-02c: na previsão, só o valor é dado da folha.
        ["PayrollForecast"] = ["Id", "Period", "Kind", "EmployeeId", "ChargeKind", "DueDate", "SourceRunId", "CreatedAt", "UpdatedAt", "CancelledAt", "Key"],
        // RH-02b: o PDF guardado (id do anexo), o aviso (quando, id do e-mail), a primeira leitura e a marca "alterado" não são dado pessoal.
        // FLH-02a: a versão do motor, quando e os ids das tabelas e dos parâmetros usados não são dado pessoal; o retrato, os avisos e os
        // encargos são [PersonalData].
        ["Payslip"] = ["Id", "PayrollRunId", "EmployeeId", "CreatedAt", "UpdatedAt", "Version", "PdfAttachmentId", "NotifiedAt", "NoticeEmailLogId", "FirstViewedAt", "ChangedAfterNotice",
            "CalculatedAt", "CalculatorVersion", "TaxTableIds", "ParameterIds"],
        ["PayslipLine"] = ["Id", "PayslipId", "PayrollItemId", "Code", "Description", "Type", "Sort", "Calculated"],
        ["PayrollRunCharge"] = ["Id", "PayrollRunId", "Kind", "Description", "DueDate", "PayeePartyId", "CreatedAt", "Version", "Calculated"],
        ["HrParameter"] = ["Id", "Key", "Value", "Text", "ConfirmedAt", "ConfirmedBy", "UpdatedAt"],
        // RH-01a: a marcação (quem, quando, tipo, origem, aparelho e se tem localização) é dado de jornada, não sensível; a latitude, a
        // longitude e a precisão são [PersonalData] (LGPD: localização). A ciência da geolocalização não tem dado pessoal.
        // RH-01b: quem aprovou, o pedido que criou ou desconsiderou a marcação e quando não são dado pessoal; no pedido de ajuste, a
        // justificativa, o motivo da ausência (pode ser de saúde) e o comentário da decisão são [PersonalData].
        ["TimeEntry"] = ["Id", "EmployeeId", "At", "Day", "Type", "Source", "Status", "RecordedByUserId", "DeviceId", "Location", "CreatedAt", "Version",
            "ApprovedBy", "AdjustmentId", "VoidAdjustmentId", "VoidedBy", "VoidedAt"],
        ["TimeAdjustment"] = ["Id", "EmployeeId", "Kind", "Day", "EntryId", "At", "EntryType", "AbsenceMinutes", "Source", "RequestedBy", "RequestedAt", "Status",
            "DecidedBy", "DecidedAt", "SelfApproved", "CreatedEntryId", "Version"],
        ["TimeClockGeoConsent"] = ["Id", "EmployeeId", "UserId", "AcceptedAt", "TextVersion"],
        // RH-01c: os totais do espelho (minutos) e o fechamento não são dado pessoal; os dias congelados e o abono por motivo (podem trazer o
        // tipo do afastamento e o motivo de saúde), a data do acordo de banco e o comentário da revisão são [PersonalData].
        ["Timesheet"] = ["Id", "EmployeeId", "Period", "PeriodStart", "PeriodEnd", "Status", "ExpectedMinutes", "WorkedMinutes", "OvertimeMinutes", "AbsenceMinutes", "LateMinutes",
            "JustifiedMinutes", "BankMonthMinutes", "BankBalanceMinutes", "BankPayoutMinutes", "WorkedDays", "AbsenceDays", "ClosedAt", "ClosedBy", "ReopenedAt", "ReopenedBy", "ReopenReason",
            "EmployeeAckAt", "EmployeeAckBy", "EmployeeAckSummary", "PdfAttachmentId", "CreatedAt", "Version"],
        ["TimesheetDayReview"] = ["Id", "EmployeeId", "Day", "ReviewedBy", "ReviewedAt"],

        // FLH-01: tabela legal da folha não é dado pessoal (CA 14); o dataset da contadora são casos "base → esperado" montados ou
        // anonimizados, sem funcionário.
        ["PayrollTaxTable"] = ["Id", "Kind", "ValidFrom", "Status", "RectifiesId", "RectifyReason", "Content", "SchemaVersion", "Rounding", "Source", "SeedPackage",
            "CreatedBy", "CreatedAt", "EditedBy", "EditedAt", "CheckedBy", "CheckedAt", "CheckedNote", "CheckedWithDatasetId", "SupersededAt", "Version", "IsCurrent"],
        ["PayrollTaxTableDataset"] = ["Id", "TableId", "Cases", "CaseCount", "CreatedBy", "CreatedAt"],

        // FLH-06: a conferência (competência, tipo, folhas, situação, contagens, quem aceitou, a contadora responsável e o termo) não é dado pessoal;
        // os totais do líquido são. Na diferença, a chave, a rubrica e a classificação ficam fora; valores, justificativa e o caso de correção são
        // [PersonalData]. O de-para e a virada (o motivo é sobre a folha, não sobre um funcionário) não têm dado pessoal.
        ["PayrollParallelRun"] = ["Id", "Period", "RunType", "PayrollType", "EnteredRunId", "CalculatedRunId", "Status", "ComparedAt", "ComparedBy", "Differences", "Employees",
            "AcceptedBy", "AcceptedAt", "AccountantName", "AccountantCrc", "SignedTermAttachmentId", "CreatedAt", "Version"],
        ["PayrollParallelDifference"] = ["Id", "ParallelRunId", "EmployeeId", "PayrollItemCode", "ItemName", "Kind", "Classification", "ClassifiedBy", "ClassifiedAt"],
        ["PayrollItemMapping"] = ["Id", "EnteredCode", "CalculatedCode", "CreatedAt", "UpdatedAt"],
        ["PayrollModeChange"] = ["Id", "RunType", "Mode", "FromPeriod", "ChangedAt", "ChangedBy", "Reason", "ParallelRunIds", "AccountantName", "AccountantCrc"],

        // FLH-03: datas, dias e situação das férias são dado de jornada (saem com hr.view, sem valor); o motivo do reinício (fala do
        // afastamento), a divisão do INSS e o motivo do cancelamento são [PersonalData].
        ["VacationPeriod"] = ["Id", "EmployeeId", "Sequence", "StartsOn", "EndsOn", "Absences", "EntitledDays", "TakenDays", "SoldDays", "ConcessionEndsOn", "Status", "RestartedOn",
            "CreatedAt", "UpdatedAt", "Balance"],
        ["VacationSchedule"] = ["Id", "EmployeeId", "PeriodId", "StartsOn", "Days", "SoldDays", "Advance13th", "PaymentDueOn", "Status", "PayrollRunId", "CreatedAt", "CreatedBy",
            "CancelledAt", "CancelledBy", "Version", "EndsOn"],
    };

    /// <summary>
    /// RH-02a CA 12 e 13: valores de linha, bases, líquido e FGTS do mês são <c>[PersonalData]</c>, e os DTOs de resumo e de lista da folha
    /// (que saem com <c>hr.view</c>) não têm nenhuma propriedade de valor.
    /// </summary>
    [Fact]
    public void Folha_com_valores_marcados_e_resumo_sem_valor()
    {
        foreach (var (type, props) in new (Type, string[])[]
        {
            (typeof(BemAqui.HR.Domain.Payslip), ["Gross", "Deductions", "Net", "InssBase", "FgtsBase", "IrrfBase", "FgtsAmount", "InputsSnapshot", "EmployerCharges", "CalculationIssues", "HourBankDecisionMinutes", "HourBankDecisionNote"]),
            (typeof(BemAqui.HR.Domain.PayslipLine), ["Amount", "Reference", "Memory", "Note"]),
            (typeof(BemAqui.HR.Domain.PayrollCalculationLog), ["GrossBefore", "NetBefore", "GrossAfter", "NetAfter"]),
            (typeof(BemAqui.HR.Domain.EmployeeDependent), ["FamilyAllowanceDocsOk"]),
            (typeof(BemAqui.HR.Domain.PayrollRunCharge), ["Amount"]),
            (typeof(BemAqui.HR.Domain.PayrollRun), ["ClosedNet", "ClosedCharges"]),
            (typeof(BemAqui.HR.Domain.PayrollForecast), ["Amount"]),
            (typeof(BemAqui.HR.Domain.PayrollParallelDifference), ["EnteredAmount", "CalculatedAmount", "Difference", "DifferencePct", "Justification", "CorrectionCase"]),
            (typeof(BemAqui.HR.Domain.PayrollParallelRun), ["EnteredNet", "CalculatedNet"]),
        })
        {
            foreach (var name in props)
            {
                type.GetProperty(name)!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull($"{type.Name}.{name} é dado da folha (CA 13)");
            }
        }

        var values = new[] { typeof(BemAqui.HR.Domain.Payslip), typeof(BemAqui.HR.Domain.PayslipLine), typeof(BemAqui.HR.Domain.PayrollRunCharge) }
            .SelectMany(t => t.GetProperties().Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null).Select(p => p.Name)).Append("Total").ToHashSet(StringComparer.Ordinal);
        var application = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Application");
        foreach (var dto in new[] { "PayrollRunSummary", "PayrollEligibleEmployee", "PayrollItemDetail" })
        {
            var type = application.GetTypes().Single(t => t.Name == dto);
            type.GetProperties().Select(p => p.Name).Where(n => dto != "PayrollItemDetail" || !n.EndsWith("Base", StringComparison.Ordinal))
                .Should().NotIntersectWith(values, dto + " sai com hr.view e não pode ter valor da folha (CA 12)");
        }

        // RH-02b CA 1, 8 e 12: o aviso, o lote e o argumento do job não levam valor; o funcionário no PDF não tem CPF completo, endereço,
        // dados bancários nem dependentes.
        foreach (var dto in new[] { "PayslipNoticeRow", "PayslipNotices", "PayslipNoticePerson", "PayslipNotifyResult", "PayslipBatchResult", "PayslipBatchArgs", "MyEmployeeLink",
            "PayrollCalculationArgs", "PayrollCalculationStatus", "PayrollCalculationEntry", "PayrollCalcEmployeeIssue",
            "PayrollParallelOverview", "PayrollParallelMonthRow", "PayrollParallelPending", "PayrollParallelHistoryEntry" })
        {
            application.GetTypes().Single(t => t.Name == dto).GetProperties().Select(p => p.Name).Should().NotIntersectWith(values, dto + " não leva valor do holerite");
        }

        application.GetTypes().Single(t => t.Name == "PayslipPdfEmployee").GetProperties().Select(p => p.Name).Should()
            .NotContain(["Cpf", "Address", "Street", "BankAccount", "PixKey", "Dependents", "Email", "Phone", "BirthDate"], "o PDF só leva o CPF mascarado 3+2 (CA 1)");
    }

    [Fact]
    public void Entidades_auditaveis_do_HR_so_tem_dado_pessoal_marcado_e_listas_sem_salario()
    {
        var domain = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Domain");
        var auditable = domain.GetTypes().Where(t => t.IsClass && typeof(IAuditable).IsAssignableFrom(t)).ToList();
        auditable.Should().NotBeEmpty();
        Violations(auditable, HrNonSensitive).Should().BeEmpty();
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("Reason")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o motivo do afastamento é sensível (CAD-04c1 CA 9)");
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("LeaveType")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o tipo do afastamento é sensível (CAD-04c1 CA 9)");
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("PreviousLeaveType")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o tipo anterior do afastamento é sensível (CAD-04c1b)");
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("OldValue")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o período antes da mudança do afastamento é sensível (CAD-04c1b)");

        var application = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Application");
        // O nome do dependente é dado pessoal, mas o nome do funcionário e do cargo não: a lista e o resumo são conferidos contra o
        // funcionário e o cargo.
        var sensitive = auditable.Where(t => t != typeof(BemAqui.HR.Domain.EmployeeDependent)).SelectMany(t => t.GetProperties().Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null).Select(p => p.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var dto in new[] { "PositionDetail", "PositionSummary", "EmployeeDetail" })
        {
            var type = application.GetTypes().Single(t => t.Name == dto);
            type.GetProperties().Select(p => p.Name).Should().NotIntersectWith(sensitive, dto + " é de lista ou resumo e não pode ter salário");
        }
    }

    /// <summary>CAD-04c1 CA 3 e 6: o resumo para os outros módulos e os eventos de afastamento levam só id, datas e situação.</summary>
    [Fact]
    public void Resumo_e_eventos_de_afastamento_nao_levam_tipo_nem_motivo()
    {
        var contracts = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Contracts");
        foreach (var name in new[] { "EmployeeSummary", "EmployeeLeaveStarted", "EmployeeLeaveEnded" })
        {
            contracts.GetTypes().Single(t => t.Name == name).GetProperties().Select(p => p.Name)
                .Should().NotContain(["LeaveType", "Type", "Reason"], name + " não pode expor o tipo nem o motivo do afastamento");
        }

        contracts.GetTypes().Single(t => t.Name == "EmployeeLeaveStarted").GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["EmployeeId", "StartDate", "ExpectedEndDate"]);
        contracts.GetTypes().Single(t => t.Name == "EmployeeLeaveEnded").GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["EmployeeId", "StartDate", "EndDate"]);
    }

    /// <summary>
    /// CAD-04c2 CA 7 e 11 (ADR 0019 §7 adendo): a situação da conta no <c>IEmployees</c> não leva dado bancário; o dono de anexo
    /// <c>EmployeeDocument</c> lê e grava com <c>hr.view_sensitive</c>, pdf, png e jpg até 10 MB, confere o dono e audita o download.
    /// </summary>
    [Fact]
    public void Situacao_da_conta_sem_dado_bancario_e_dono_EmployeeDocument_com_as_regras_da_ADR()
    {
        var contracts = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Contracts");
        contracts.GetTypes().Single(t => t.Name == "EmployeeBankAccountStatus").GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["EmployeeId", "IsFirstAccount", "ChangedAt", "QuarantineUntil", "InQuarantine", "ReviewedAt", "SelfReviewed", "Version"]);
        var application = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Application");
        application.GetTypes().Single(t => t.Name == "EmployeeBankStatusView").GetProperties().Select(p => p.Name)
            .Should().NotContain(["BankCode", "Agency", "Account", "HolderName", "HolderCpf", "PixKey"], "o chip do cabeçalho não lê dado bancário");

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        BemAqui.HR.Infrastructure.HrInfrastructure.AddHrInfrastructure(services);
        var rules = services.Select(d => d.ImplementationInstance).OfType<BemAqui.BuildingBlocks.Attachments.AttachmentOwnerRegistration>().Single(r => r.OwnerType == "EmployeeDocument").Rules;
        rules.Module.Should().Be("hr");
        rules.ReadAction.Should().Be("view_sensitive");
        rules.WriteAction.Should().Be("view_sensitive");
        rules.AlternateWriteAction.Should().BeNull();
        rules.MaxSize.Should().Be(10 * 1024 * 1024);
        rules.ContentTypes.Should().BeEquivalentTo(["application/pdf", "image/png", "image/jpeg"]);
        rules.OwnerExists.Should().NotBeNull();
        rules.AuditReads.Should().BeTrue("cada download de documento do funcionário é leitura sensível (CA 12)");
    }

    /// <summary>
    /// RH-02c CA 2, 7 e 8: o resumo da conta de salário para o Finance não leva dado bancário em claro; os eventos da folha só levam id e
    /// competência; o retrato da OP não tem campo de agência, conta inteira, titular ou chave do funcionário.
    /// </summary>
    [Fact]
    public void Folha_no_financeiro_sem_dado_bancario_nem_valor_nos_contratos()
    {
        var contracts = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Contracts");
        contracts.GetTypes().Single(t => t.Name == "EmployeeBankAccountSummary").GetProperties().Select(p => p.Name)
            .Should().NotIntersectWith(["BankCode", "Agency", "Account", "HolderName", "HolderCpf", "PixKey", "Cpf", "BaseSalary"]);
        contracts.GetTypes().Single(t => t.Name == "PayrollClosed").GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["PayrollRunId", "Period"]);
        contracts.GetTypes().Single(t => t.Name == "PayrollReopened").GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["PayrollRunId"]);
        var finance = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.Finance.Application");
        finance.GetTypes().Single(t => t.Name == "PayeeSnapshot").GetProperties().Select(p => p.Name)
            .Should().NotIntersectWith(["Agency", "HolderCpf", "PixKey", "BaseSalary", "Cpf"]);
    }

    /// <summary>
    /// PAT-02: o mesmo fail-closed no Patrimônio. Ids de funcionário, terceiro e usuário, datas, valores e a observação do bem não são dado
    /// pessoal; o motivo do movimento (texto livre, pode citar pessoas, ex.: "Responsável desligado") é <c>[PersonalData]</c> e entra no
    /// arquivo do titular pelo <c>AssetsPersonalDataProvider</c>.
    /// </summary>
    private static readonly Dictionary<string, string[]> AssetsNonSensitive = new(StringComparer.Ordinal)
    {
        ["Asset"] = ["Id", "Tag", "Name", "Type", "Brand", "Model", "SerialNumber", "AcquisitionDate", "AcquisitionValue", "IncomingInvoiceId", "InvoiceRef", "SupplierPartyId", "SiteId",
            "ResponsibleEmployeeId", "WorkCenterId", "Status", "Notes", "CreatedAt", "IsDeleted", "DeletedAt", "Version"],
        ["AssetSite"] = ["Id", "Name", "IsActive", "SeedKey", "CreatedAt", "IsDeleted", "DeletedAt"],
        ["AssetParameter"] = ["Id", "Key", "Value", "UpdatedAt", "ConfirmedAt", "ConfirmedBy"],
        ["AssetMovement"] = ["Id", "AssetId", "Kind", "FromSiteId", "ToSiteId", "FromEmployeeId", "ToEmployeeId", "FromWorkCenterId", "ToWorkCenterId", "MovedOn", "DisposalReason",
            "DisposalValue", "BuyerPartyId", "CreatedBy", "CreatedAt"],
        // PAT-03a: plano, checklist e horímetro são dados da máquina; na OS, o problema, o que foi feito e o motivo do cancelamento (texto livre,
        // pode citar pessoas) são [PersonalData] e entram no arquivo do titular (quem escreveu); ids, datas e situação não.
        ["MaintenancePlan"] = ["Id", "AssetId", "Description", "FrequencyType", "FrequencyValue", "LeadDays", "DefaultSupplierPartyId", "Checklist", "LastDoneAt", "LastDoneMeter",
            "NextDueAt", "NextDueMeter", "IsActive", "CreatedAt", "Version"],
        ["AssetMeterReading"] = ["Id", "AssetId", "ReadOn", "Value", "CreatedBy", "CreatedAt"],
        ["MaintenanceOrder"] = ["Id", "Number", "AssetId", "PlanId", "DueKey", "Type", "Status", "MachineStopped", "ScheduledOn", "SupplierPartyId", "StartedAt", "StartedBy",
            "DoneAt", "DoneBy", "CancelledAt", "CancelledBy", "CreatedBy", "CreatedAt", "Version",
            // PAT-03b: valor do contrato, horas e período da parada, custo e o título a pagar (ids, valores e datas).
            "ContractValue", "ExpectedHours", "HoursPerDay", "DowntimeFrom", "DowntimeTo", "DowntimeRevision", "Cost", "PayableId"],
        // PAT-03b: a linha de custo guarda só o id do funcionário (mantida, ver AssetsPersonalDataProvider); o custo-hora é o do cargo e só sai
        // com hr.view_sensitive; a descrição é o rótulo da linha, como a do plano.
        ["MaintenanceOrderCost"] = ["Id", "OrderId", "CostType", "Description", "Amount", "Hours", "HourlyCostSnapshot", "EmployeeId", "PayableId", "CreatedBy", "CreatedAt"],
        ["MaintenanceOrderChecklistItem"] = ["Id", "OrderId", "Sequence", "Text", "Result", "MarkedAt", "MarkedBy"],
    };

    [Fact]
    public void Entidades_auditaveis_do_Patrimonio_so_tem_dado_pessoal_marcado()
    {
        var domain = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.Assets.Domain");
        var auditable = domain.GetTypes().Where(t => t.IsClass && typeof(IAuditable).IsAssignableFrom(t)).ToList();
        auditable.Should().NotBeEmpty();
        Violations(auditable, AssetsNonSensitive).Should().BeEmpty();
    }

    private sealed class Leaky : IAuditable
    {
        public string Cpf { get; set; } = string.Empty;

        [PersonalData]
        public string Email { get; set; } = string.Empty;
    }

    [Fact]
    public void Regra_acusa_campo_sem_decisao_e_entidade_sem_lista()
    {
        Violations([typeof(Leaky)], new Dictionary<string, string[]> { ["Leaky"] = [] }).Should().ContainSingle().Which.Should().StartWith("Leaky.Cpf");
        Violations([typeof(Leaky)], new Dictionary<string, string[]>()).Should().ContainSingle().Which.Should().Contain("sem lista");
    }
}
