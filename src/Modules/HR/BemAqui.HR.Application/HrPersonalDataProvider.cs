using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Titular funcionário (LGPD-01a CA 9, ADR 0037 §1): o RH identifica o titular, responde ao seletor e exporta o cadastro, os dados pessoais, o
/// contrato e a linha do tempo, os dependentes (como o titular informou, com o CPF mascarado), a conta de salário, os afastamentos, as
/// marcações de ponto (com a localização) e a ciência da geolocalização, os ajustes e abonos, os holerites e os documentos anexados (só os
/// <c>Clean</c>, na pasta <c>anexos/</c>). Dado sensível segue a tela: documentos, endereço, salário, conta e localização só com
/// <c>hr.view_sensitive</c>; tipo e motivo de afastamento e de abono de saúde, e o documento do abono, só com <c>hr.view_health</c>.
/// O espelho (RH-01c) entra com os totais e o "Li e confirmo", e o abonado por motivo só com <c>hr.view_health</c>; as revisões do dia entram
/// com o comentário como a tela do espelho mostra a quem tem <c>hr.view</c>.
/// </summary>
[PersonalDataCovers(typeof(Employee), typeof(EmployeeDependent), typeof(EmployeeHistoryEntry), typeof(TimeEntry), typeof(TimeAdjustment), typeof(Timesheet), typeof(TimesheetDayReview),
    typeof(Payslip), typeof(PayslipLine), typeof(PayrollForecast), typeof(PayrollRunCharge), typeof(PayrollRun), typeof(JobPosition), typeof(PayrollCalculationLog),
    typeof(VacationPeriod), typeof(VacationSchedule))]
public sealed class HrPersonalDataProvider(IHrDb db, AttachmentService attachments, TimeProvider clock) : IPersonalDataProvider
{
    private const string Sensitive = "hr." + PositionService.SensitiveAction;
    private const string Health = "hr." + EmployeeService.HealthAction;

    public string Module => "hr";

    public string Title => "RH";

    public async Task<PersonalDataSubject?> ResolveAsync(PersonalDataSubjectKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        if (kind != PersonalDataSubjectKind.Employee)
        {
            return null;
        }

        var e = await db.Employees.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
        {
            return null;
        }

        var emails = new[] { e.Email, e.PreviousEmail }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim().ToLowerInvariant()).Distinct().ToList();
        return new PersonalDataSubject(kind, id, e.Name, emails, e.Cpf, EmployeeId: id);
    }

    public async Task<IReadOnlyList<PersonalDataSubjectMatch>> SearchAsync(PersonalDataSubjectKind kind, string term, CancellationToken cancellationToken = default)
    {
        if (kind != PersonalDataSubjectKind.Employee || string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return [];
        }

        var like = "%" + term.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var digits = BrazilianDocuments.Normalize(term);
        var list = await db.Employees.AsNoTracking()
            .Where(e => EF.Functions.ILike(e.Name, like) || (e.Registration != null && EF.Functions.ILike(e.Registration, like)) || (digits.Length >= 6 && e.Cpf.Contains(digits)))
            .OrderBy(e => e.Name).Take(20).Select(e => new { e.Id, e.Name, e.Registration, e.Cpf }).ToListAsync(cancellationToken);
        return list.Select(e => new PersonalDataSubjectMatch(kind, e.Id, e.Name, (e.Registration is null ? string.Empty : "Matrícula " + e.Registration + " · ") + PersonalDataMask.Cpf(e.Cpf))).ToList();
    }

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Subject.EmployeeId is not { } id)
        {
            return null;
        }

        var e = await db.Employees.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
        {
            return null;
        }

        var sensitive = context.Can(Sensitive);
        var health = context.Can(Health);
        var position = await db.JobPositions.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
        var blocks = new List<PersonalDataBlock>
        {
            new("Cadastro e contrato", "hr.employees", [PersonalDataRecord.Of(e.CreatedAt,
                ("Nome", e.Name), ("Matrícula", e.Registration), ("Cargo", position), ("Admissão", PersonalDataMask.Date(e.AdmissionDate)),
                ("Desligamento", PersonalDataMask.Date(e.TerminationDate)), ("Situação", e.Status == EmployeeStatus.Terminated ? "Desligado" : "Ativo"))]),
        };

        if (sensitive)
        {
            blocks.Add(new PersonalDataBlock("Dados pessoais", "hr.employees", [PersonalDataRecord.Of(
                ("CPF", BrazilianDocuments.Format(e.Cpf)), ("Nascimento", PersonalDataMask.Date(e.BirthDate)), ("RG", e.Rg), ("PIS", e.Pis), ("CTPS", Join(e.CtpsNumber, e.CtpsSeries)),
                ("Título de eleitor", e.VoterId), ("CNH", Join(e.CnhNumber, e.CnhCategory)), ("Validade da CNH", PersonalDataMask.Date(e.CnhExpiry)), ("Reservista", e.MilitaryCertificate),
                ("CEP", e.ZipCode), ("Logradouro", e.Street), ("Número", e.Number), ("Complemento", e.Complement), ("Bairro", e.District), ("Cidade", e.City), ("UF", e.State),
                ("Telefone", e.Phone), ("E-mail", e.Email), ("E-mail anterior", e.PreviousEmail), ("Salário base", PersonalDataMask.Money(e.BaseSalary)),
                ("Insalubridade (%)", e.InsalubrityPct?.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Periculosidade", PersonalDataMask.YesNo(e.HazardPay)),
                ("Vale-transporte", PersonalDataMask.YesNo(e.UsesTransportVoucher)))]));
            if (e.BankAccountVersion > 0)
            {
                var ownCpf = BrazilianDocuments.Normalize(e.BankHolderCpf) == BrazilianDocuments.Normalize(e.Cpf);
                blocks.Add(new PersonalDataBlock("Conta de salário", "hr.employees", [PersonalDataRecord.Of(e.BankChangedAt,
                    ("Banco", e.BankCode), ("Agência", e.BankAgency), ("Conta", e.BankAccount), ("Tipo", e.BankAccountType?.ToString()),
                    ("Titular", ownCpf ? e.BankHolderName : PersonalDataMask.Name(e.BankHolderName)), ("CPF do titular", ownCpf ? BrazilianDocuments.Format(e.BankHolderCpf) : PersonalDataMask.Cpf(e.BankHolderCpf)),
                    ("Tipo da chave Pix", e.PixKeyType?.ToString()), ("Chave Pix", e.PixKey), ("Registrada ou trocada em", PersonalDataMask.At(e.BankChangedAt)))]));
            }
        }

        blocks.Add(await HistoryAsync(id, sensitive, health, cancellationToken));
        blocks.Add(await DependentsAsync(id, cancellationToken));
        blocks.AddRange(await TimeClockAsync(id, sensitive, health, cancellationToken));
        blocks.AddRange(await VacationsAsync(id, cancellationToken));
        if (sensitive)
        {
            blocks.Add(await PayslipsAsync(id, cancellationToken));
        }

        var files = new List<PersonalDataFile>();
        var documents = await attachments.ListCheckedAsync(EmployeeComplementService.DocumentOwner, id, cancellationToken);
        if (sensitive)
        {
            blocks.Add(new PersonalDataBlock("Documentos anexados", "shared.attachments", documents.Select(d => PersonalDataRecord.Of(d.CreatedAt, ("Arquivo", d.FileName), ("Enviado em", PersonalDataMask.At(d.CreatedAt)))).ToList()));
            files.AddRange(documents.Where(d => d.ScanStatus == ScanStatus.Clean).Select(d => new PersonalDataFile(d.Id, EmployeeComplementService.DocumentOwner, id, d.FileName)));
        }

        if (health)
        {
            var absences = await db.TimeAdjustments.AsNoTracking().Where(a => a.EmployeeId == id && a.Kind == TimeAdjustmentKind.Absence).Select(a => a.Id).ToListAsync(cancellationToken);
            foreach (var absence in absences)
            {
                files.AddRange((await attachments.ListCheckedAsync(TimeAdjustmentService.DocumentOwner, absence, cancellationToken))
                    .Where(d => d.ScanStatus == ScanStatus.Clean).Select(d => new PersonalDataFile(d.Id, TimeAdjustmentService.DocumentOwner, absence, d.FileName)));
            }
        }

        var note = (sensitive, health) switch
        {
            (true, true) => null,
            (true, false) => "Tipo e motivo de afastamento e de abono de saúde não incluídos: quem pediu não tem hr.view_health.",
            _ => "Dados pessoais, conta, salário, localização e holerites não incluídos: quem pediu não tem hr.view_sensitive.",
        };
        return new PersonalDataSection(Module, Title, blocks, files, note);
    }

    private async Task<PersonalDataBlock> HistoryAsync(Guid id, bool sensitive, bool health, CancellationToken ct)
    {
        var entries = await db.EmployeeHistory.AsNoTracking().Where(h => h.EmployeeId == id).OrderBy(h => h.EffectiveDate).ThenBy(h => h.CreatedAt).ToListAsync(ct);
        var records = entries.Select(h => h.Kind switch
        {
            EmployeeHistoryKind.Salary => PersonalDataRecord.Of(h.CreatedAt, ("Tipo", "Reajuste"), ("Vigência", PersonalDataMask.Date(h.EffectiveDate)),
                ("Antes", sensitive ? h.OldValue : "protegido"), ("Depois", sensitive ? h.NewValue : "protegido"), ("Motivo", sensitive ? h.Reason : "protegido")),
            EmployeeHistoryKind.Position => PersonalDataRecord.Of(h.CreatedAt, ("Tipo", "Troca de cargo"), ("Vigência", PersonalDataMask.Date(h.EffectiveDate)), ("Motivo", sensitive ? h.Reason : "protegido")),
            EmployeeHistoryKind.Leave => PersonalDataRecord.Of(h.CreatedAt, ("Tipo", "Afastamento"), ("Início", PersonalDataMask.Date(h.EffectiveDate)),
                ("Fim previsto", PersonalDataMask.Date(h.LeaveEndDate)), ("Motivo do afastamento", health && h.LeaveType is { } t ? AbsenceReasons.Label(t) : "protegido"),
                ("Observação", health ? h.Reason : "protegido"), ("Cancelado em", PersonalDataMask.At(h.CancelledAt))),
            _ => PersonalDataRecord.Of(h.CreatedAt, ("Tipo", "Mudança de afastamento"), ("Data", PersonalDataMask.Date(h.EffectiveDate)), ("Motivo", health ? h.Reason : "protegido")),
        }).ToList();
        return new PersonalDataBlock("Linha do tempo e afastamentos", "hr.employee_history", records);
    }

    private async Task<PersonalDataBlock> DependentsAsync(Guid id, CancellationToken ct)
    {
        var list = await db.EmployeeDependents.AsNoTracking().Where(d => d.EmployeeId == id).OrderBy(d => d.Name).ToListAsync(ct);
        return new PersonalDataBlock("Dependentes (como o titular informou)", "hr.employee_dependents", list.Select(d => PersonalDataRecord.Of(d.CreatedAt,
            ("Nome", d.Name), ("Parentesco", Kinship(d.Kinship)), ("Nascimento", PersonalDataMask.Date(d.BirthDate)), ("CPF", PersonalDataMask.Cpf(d.Cpf)),
            ("Dependente no IR", PersonalDataMask.YesNo(d.IrDependent)), ("Salário-família", PersonalDataMask.YesNo(d.FamilyAllowance)),
            ("Documentos do salário-família entregues", PersonalDataMask.YesNo(d.FamilyAllowanceDocsOk)))).ToList());
    }

    private async Task<IEnumerable<PersonalDataBlock>> TimeClockAsync(Guid id, bool sensitive, bool health, CancellationToken ct)
    {
        var entries = await db.TimeEntries.AsNoTracking().Where(t => t.EmployeeId == id).OrderBy(t => t.At).ToListAsync(ct);
        var consents = await db.TimeClockGeoConsents.AsNoTracking().Where(c => c.EmployeeId == id).OrderBy(c => c.AcceptedAt).ToListAsync(ct);
        var adjustments = await db.TimeAdjustments.AsNoTracking().Where(a => a.EmployeeId == id).OrderBy(a => a.RequestedAt).ToListAsync(ct);
        var timesheets = await db.Timesheets.AsNoTracking().Where(t => t.EmployeeId == id).OrderBy(t => t.Period).ToListAsync(ct);
        var reviews = await db.TimesheetDayReviews.AsNoTracking().Where(r => r.EmployeeId == id).OrderBy(r => r.Day).ToListAsync(ct);
        static string Hours(int? minutes) => minutes is { } m ? $"{(m < 0 ? "-" : string.Empty)}{Math.Abs(m) / 60} h {Math.Abs(m) % 60:00} min" : "-";
        return
        [
            // Espelho (RH-01c): os totais do período; o abono por motivo pode revelar saúde, então só com hr.view_health.
            new PersonalDataBlock("Espelhos de ponto", "hr.timesheets", timesheets.Select(t => PersonalDataRecord.Of(t.CreatedAt,
                ("Período", $"{PersonalDataMask.Date(t.PeriodStart)} a {PersonalDataMask.Date(t.PeriodEnd)}"), ("Situação", t.Status == TimesheetStatus.Closed ? "Fechado" : "Em aberto"),
                ("Esperado", Hours(t.ExpectedMinutes)), ("Trabalhado", Hours(t.WorkedMinutes)), ("Extras", Hours(t.OvertimeMinutes)),
                ("Faltas", Hours(t.AbsenceMinutes)), ("Atrasos", Hours(t.LateMinutes)), ("Abonado", Hours(t.JustifiedMinutes)),
                ("Abonado por motivo", health ? t.JustifiedByReason : "protegido"), ("Banco de horas", Hours(t.BankBalanceMinutes)),
                ("Banco de horas baixado pela folha", Hours(t.BankPayoutMinutes == 0 ? null : t.BankPayoutMinutes)),
                ("Fechado em", PersonalDataMask.At(t.ClosedAt)), ("Ciência do funcionário", PersonalDataMask.At(t.EmployeeAckAt)),
                ("Resumo confirmado", t.EmployeeAckSummary))).ToList()),
            // "Marcar como revisado" do RH: o dia e o comentário, como a tela do espelho mostra.
            new PersonalDataBlock("Revisões do espelho", "hr.timesheet_day_reviews", reviews.Select(r => PersonalDataRecord.Of(r.ReviewedAt,
                ("Dia", PersonalDataMask.Date(r.Day)), ("Comentário", r.Comment), ("Revisado em", PersonalDataMask.At(r.ReviewedAt)))).ToList()),
            new PersonalDataBlock("Marcações de ponto", "hr.time_entries", entries.Select(t => PersonalDataRecord.Of(t.At,
                ("Data e hora", PersonalDataMask.At(t.At)), ("Tipo", TimeClockRules.Label(t.Type)), ("Origem", t.Source.ToString()), ("Situação", t.Status.ToString()),
                ("Latitude", sensitive ? t.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) : "protegido"),
                ("Longitude", sensitive ? t.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) : "protegido"),
                ("Precisão (m)", sensitive ? t.AccuracyM?.ToString(System.Globalization.CultureInfo.InvariantCulture) : "protegido"))).ToList()),
            new PersonalDataBlock("Ciência da geolocalização", "hr.time_clock_geo_consents", consents.Select(c => PersonalDataRecord.Of(c.AcceptedAt,
                ("Ciente em", PersonalDataMask.At(c.AcceptedAt)), ("Versão do texto", c.TextVersion))).ToList()),
            new PersonalDataBlock("Ajustes e abonos", "hr.time_adjustments", adjustments.Select(a =>
            {
                var hidden = a.AbsenceReason is { } r && AbsenceReasons.IsHealth(r) && !health;
                return PersonalDataRecord.Of(a.RequestedAt,
                    ("Tipo", a.Kind switch { TimeAdjustmentKind.AddEntry => "Lançar marcação", TimeAdjustmentKind.VoidEntry => "Desconsiderar marcação", _ => "Abono de ausência" }),
                    ("Dia", PersonalDataMask.Date(a.Day)), ("Motivo", a.AbsenceReason is { } reason ? hidden ? "protegido" : AbsenceReasons.Label(reason) : null),
                    ("Justificativa", hidden ? "protegido" : a.Justification), ("Situação", TimeAdjustmentRules.StatusLabel(a.Status)),
                    ("Comentário da decisão", hidden ? "protegido" : a.DecisionComment), ("Decidido em", PersonalDataMask.At(a.DecidedAt)));
            }).ToList()),
        ];
    }

    /// <summary>
    /// Férias (FLH-03): os períodos aquisitivos (datas, faltas, dias e saldo, o motivo do reinício) e as programações (datas, abono, situação e
    /// o motivo do cancelamento), como a aba "Férias" mostra a quem tem <c>hr.view</c>; os valores ficam nos holerites de férias.
    /// </summary>
    private async Task<IEnumerable<PersonalDataBlock>> VacationsAsync(Guid id, CancellationToken ct)
    {
        var periods = await db.VacationPeriods.AsNoTracking().Where(p => p.EmployeeId == id).OrderBy(p => p.Sequence).ToListAsync(ct);
        var schedules = await db.VacationSchedules.AsNoTracking().Where(s => s.EmployeeId == id).OrderBy(s => s.StartsOn).ToListAsync(ct);
        return
        [
            new PersonalDataBlock("Períodos aquisitivos de férias", "hr.vacation_periods", periods.Select(p => PersonalDataRecord.Of(p.CreatedAt,
                ("Período", $"{PersonalDataMask.Date(p.StartsOn)} a {PersonalDataMask.Date(p.EndsOn)}"), ("Faltas injustificadas", p.Absences.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Dias de direito", p.EntitledDays.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Dias gozados", p.TakenDays.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Dias vendidos", p.SoldDays.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Fim do período concessivo", PersonalDataMask.Date(p.ConcessionEndsOn)),
                ("Reinício", p.RestartedReason))).ToList()),
            new PersonalDataBlock("Férias programadas", "hr.vacation_schedules", schedules.Select(s => PersonalDataRecord.Of(s.CreatedAt,
                ("Gozo", $"{PersonalDataMask.Date(s.StartsOn)} a {PersonalDataMask.Date(s.EndsOn)}"), ("Dias", s.Days.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Dias vendidos (abono)", s.SoldDays.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Adiantamento do 13º", PersonalDataMask.YesNo(s.Advance13th)),
                ("Pagamento até", PersonalDataMask.Date(s.PaymentDueOn)), ("Situação", VacationPolicy.ScheduleStatusLabel(s.Status)), ("Motivo do cancelamento", s.CancelReason))).ToList()),
        ];
    }

    private async Task<PersonalDataBlock> PayslipsAsync(Guid id, CancellationToken ct)
    {
        var slips = await (from p in db.Payslips.AsNoTracking()
                           join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                           where p.EmployeeId == id && (r.Status == PayrollRunStatus.Closed || r.Status == PayrollRunStatus.Paid)
                           orderby r.Period, r.Type
                           select new { p.Id, r.Period, r.Type, p.Gross, p.Deductions, p.Net, p.FgtsAmount, p.CreatedAt, p.HourBankDecisionMinutes, p.HourBankDecisionNote }).ToListAsync(ct);
        var ids = slips.Select(s => s.Id).ToList();
        var lines = (await db.PayslipLines.AsNoTracking().Where(l => ids.Contains(l.PayslipId)).OrderBy(l => l.Sort).ToListAsync(ct)).ToLookup(l => l.PayslipId);
        return new PersonalDataBlock("Holerites", "hr.payslips", slips.Select(s => PersonalDataRecord.Of(s.CreatedAt,
            ("Competência", PayrollRules.PeriodLabel(s.Period)), ("Tipo", PayrollRules.TypeLabel(s.Type)), ("Proventos", PersonalDataMask.Money(s.Gross)),
            ("Descontos", PersonalDataMask.Money(s.Deductions)), ("Líquido", PersonalDataMask.Money(s.Net)), ("FGTS do mês", PersonalDataMask.Money(s.FgtsAmount)),
            // FLH-02b: a decisão do RH sobre o banco de horas na folha (horas pagas ou descontadas) e o motivo.
            ("Banco de horas na folha", s.HourBankDecisionMinutes == 0 ? "-" : (s.HourBankDecisionMinutes > 0 ? "Pagas " : "Descontadas ") + TimesheetRules.Duration(Math.Abs(s.HourBankDecisionMinutes))),
            ("Motivo do banco de horas", s.HourBankDecisionNote),
            ("Linhas", string.Join("; ", lines[s.Id].Select(l => $"{l.Code} {l.Description}: {PersonalDataMask.Money(l.Amount)}" + (l.Reference is null ? string.Empty : $" ({l.Reference})") + (l.Note is null ? string.Empty : $" — motivo: {l.Note}")))))).ToList());
    }

    /// <summary>
    /// Datas base do funcionário (LGPD-01b CA 3): o desligamento e, do desligado, o último pagamento (a maior data de pagamento das folhas
    /// fechadas em que ele aparece, ou o próprio desligamento). Funcionário ativo não tem nenhuma das duas: nada trabalhista nem bancário sai.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> RetentionAnchorsAsync(PersonalDataSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var anchors = new Dictionary<string, DateOnly>();
        if (subject.Kind != PersonalDataSubjectKind.Employee || subject.EmployeeId is not { } id)
        {
            return anchors;
        }

        var e = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Status, x.TerminationDate }).FirstOrDefaultAsync(cancellationToken);
        if (e is not { Status: EmployeeStatus.Terminated, TerminationDate: { } terminated })
        {
            return anchors;
        }

        var paid = await (from p in db.Payslips.AsNoTracking()
                          join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                          where p.EmployeeId == id && r.PaymentDate != null && (r.Status == PayrollRunStatus.Closed || r.Status == PayrollRunStatus.Paid)
                          select r.PaymentDate).MaxAsync(cancellationToken);
        anchors[RetentionAnchors.Termination] = terminated;
        anchors[RetentionAnchors.LastPayment] = paid is { } last && last > terminated ? last : terminated;
        return anchors;
    }

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 2 a 4 e 7, pela tabela de retenção: a qualquer momento saem o telefone e os e-mails, a localização do ponto e os dependentes
    /// sem obrigação legal (fora do IR e do salário-família); a conta e a chave Pix, depois do último pagamento; o endereço e os dados
    /// trabalhistas e previdenciários (nome, CPF, documentos, textos livres do ponto e da linha do tempo, holerites em PDF, documentos e atestados
    /// anexados), 5 anos depois do desligamento. Sem desligamento, nada trabalhista nem bancário sai, e o cadastro continua em uso (sem a marca).
    /// Na folha calculada (FLH-02a), o retrato das entradas, os avisos, a memória de cálculo e o motivo dos avulsos seguem a retenção trabalhista.
    /// O desligado fica marcado "Anonimizado" (só leitura). Folhas, holerites (valores) e marcações (data e hora) ficam: são documentos com
    /// movimento. Anexo não retido tem o conteúdo apagado do storage e a linha fica com o marcador.
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Subject.Kind != PersonalDataSubjectKind.Employee || context.Subject.EmployeeId is not { } id)
        {
            return null;
        }

        var e = await db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
        {
            return null;
        }

        var marker = context.Marker;
        var plan = new PersonalDataPlanBuilder(Module, Title);
        var contact = context.Decide(RetentionGroups.Contact);
        var geo = context.Decide(RetentionGroups.Geolocation);
        var dependentsFree = context.Decide(RetentionGroups.Dependents);
        var bank = context.Decide(RetentionGroups.Bank);
        var address = context.Decide(RetentionGroups.Address);
        var labor = context.Decide(RetentionGroups.Labor);

        var entries = await db.TimeEntries.Where(t => t.EmployeeId == id && (t.Latitude != null || t.Longitude != null || t.AccuracyM != null)).ToListAsync(ct);
        var dependents = await db.EmployeeDependents.IgnoreQueryFilters().Where(d => d.EmployeeId == id).ToListAsync(ct);
        var withoutObligation = dependents.Where(d => !d.IrDependent && !d.FamilyAllowance).ToList();
        var withObligation = dependents.Except(withoutObligation).ToList();
        var history = await db.EmployeeHistory.Where(h => h.EmployeeId == id).ToListAsync(ct);
        var adjustments = await db.TimeAdjustments.Where(a => a.EmployeeId == id).ToListAsync(ct);
        var payslips = await db.Payslips.Where(p => p.EmployeeId == id).Select(p => p.Id).ToListAsync(ct);
        var adjustmentIds = adjustments.Select(a => a.Id).ToList();
        var timesheets = await db.Timesheets.Where(t => t.EmployeeId == id).ToListAsync(ct);
        var timesheetIds = timesheets.Select(t => t.Id).ToList();
        var reviews = await db.TimesheetDayReviews.Where(r => r.EmployeeId == id).ToListAsync(ct);
        // FLH-02a: o retrato das entradas (salário, dependentes, afastamentos com o tipo), os avisos e a memória dos encargos do holerite
        // calculado, e a memória e o motivo dos avulsos nas linhas; os valores do holerite ficam (documento com movimento).
        var calculated = await db.Payslips.Where(p => p.EmployeeId == id && (p.InputsSnapshot != null || p.CalculationIssues != null || p.EmployerChargesMemory != null)).ToListAsync(ct);
        var calculatedLines = await db.PayslipLines.Where(l => payslips.Contains(l.PayslipId) && (l.Memory != null || l.Note != null)).ToListAsync(ct);
        // FLH-03: o motivo do reinício do período (fala do afastamento), o motivo do cancelamento e a divisão do INSS das férias.
        var vacationPeriods = await db.VacationPeriods.Where(p => p.EmployeeId == id && p.RestartedReason != null).ToListAsync(ct);
        var vacationSchedules = await db.VacationSchedules.Where(s => s.EmployeeId == id && (s.CancelReason != null || s.CalculatedSplit != null)).ToListAsync(ct);
        var files = await attachments.CountAsync(EmployeeComplementService.DocumentOwner, [id], ct)
            + await attachments.CountAsync(TimeAdjustmentService.DocumentOwner, adjustmentIds, ct)
            + await attachments.CountAsync(PayslipDocumentRules.OwnerType, payslips, ct)
            + await attachments.CountAsync(TimesheetPdfService.OwnerType, timesheetIds, ct);
        var timesheetTexts = timesheets.Count(t => t.DaysSnapshot != null || t.JustifiedByReason != null || t.ReopenReason != null) + reviews.Count
            + (e.HourBankAgreementOn is null ? 0 : 1);
        var hasContact = e.Phone != null || e.Email != null || e.PreviousEmail != null;
        var hasBank = e.BankCode != null || e.BankAccount != null || e.PixKey != null || e.BankHolderName != null;
        var hasAddress = e.ZipCode != null || e.Street != null || e.City != null;
        var terminated = e.Status == EmployeeStatus.Terminated;

        if (apply)
        {
            if (!contact.Retained)
            {
                e.Phone = null;
                e.Email = null;
                e.PreviousEmail = null;
            }

            if (!geo.Retained)
            {
                foreach (var t in entries)
                {
                    var entry = db.Entry(t);
                    entry.Property(x => x.Latitude).CurrentValue = null;
                    entry.Property(x => x.Longitude).CurrentValue = null;
                    entry.Property(x => x.AccuracyM).CurrentValue = null;
                }
            }

            if (!dependentsFree.Retained)
            {
                withoutObligation.ForEach(d => AnonymizeDependent(d, marker));
            }

            if (!bank.Retained)
            {
                e.BankCode = null;
                e.BankAgency = null;
                e.BankAccount = null;
                e.BankAccountType = null;
                e.BankHolderName = null;
                e.BankHolderCpf = null;
                e.PixKeyType = null;
                e.PixKey = null;
                e.BankMismatchReason = null;
                e.BankReviewNote = null;
            }

            if (!address.Retained)
            {
                e.ZipCode = null;
                e.Street = null;
                e.Number = null;
                e.Complement = null;
                e.District = null;
                e.City = null;
                e.State = null;
            }

            if (!labor.Retained)
            {
                AnonymizeLabor(e, marker);
                withObligation.ForEach(d => AnonymizeDependent(d, marker));
                foreach (var h in history.Where(h => h.Reason.Length > 0 && h.Reason != marker))
                {
                    h.Reason = marker;
                }

                foreach (var a in adjustments)
                {
                    var entry = db.Entry(a);
                    if (a.Justification.Length > 0)
                    {
                        entry.Property(x => x.Justification).CurrentValue = marker;
                    }

                    a.DecisionComment = a.DecisionComment is null ? null : marker;
                }

                // RH-01c: no espelho ficam os totais congelados (obrigação trabalhista, documento com movimento); os motivos de saúde dos dias
                // e do abono por motivo viram "Falta justificada" (as horas abonadas continuam batendo), o tipo do afastamento sai, e os
                // textos livres (motivo da reabertura, comentário da revisão do dia) viram o marcador, como os outros textos livres do RH.
                foreach (var t in timesheets)
                {
                    t.DaysSnapshot = WithoutHealth(t.DaysSnapshot);
                    t.JustifiedByReason = WithoutHealthByReason(t.JustifiedByReason);
                    t.ReopenReason = t.ReopenReason is null ? null : marker;
                    t.PdfAttachmentId = null;
                }

                foreach (var r in reviews.Where(r => r.Comment != marker))
                {
                    db.Entry(r).Property(x => x.Comment).CurrentValue = marker;
                }

                // FLH-02a: o retrato, os avisos e a memória saem (o cálculo deixa de ser refeito para quem saiu); o motivo do avulso vira o
                // marcador, como os outros textos livres do RH. Linhas, valores, bases e o FGTS do holerite ficam.
                foreach (var p in calculated)
                {
                    p.InputsSnapshot = null;
                    p.CalculationIssues = null;
                    p.EmployerChargesMemory = null;
                }

                foreach (var l in calculatedLines)
                {
                    var entry = db.Entry(l);
                    entry.Property(x => x.Memory).CurrentValue = null;
                    entry.Property(x => x.Note).CurrentValue = l.Note is null ? null : marker;
                }

                // FLH-03: datas e dias de férias ficam (obrigação trabalhista); os textos livres viram o marcador e a divisão do INSS sai.
                foreach (var p in vacationPeriods)
                {
                    p.RestartedReason = marker;
                }

                foreach (var s in vacationSchedules)
                {
                    s.CancelReason = s.CancelReason is null ? null : marker;
                    s.CalculatedSplit = null;
                }

                await attachments.RemoveContentAsync(EmployeeComplementService.DocumentOwner, [id], marker, ct);
                await attachments.RemoveContentAsync(TimeAdjustmentService.DocumentOwner, adjustmentIds, marker, ct);
                await attachments.RemoveContentAsync(PayslipDocumentRules.OwnerType, payslips, marker, ct);
                await attachments.RemoveContentAsync(TimesheetPdfService.OwnerType, timesheetIds, marker, ct);
            }

            if (terminated)
            {
                e.AnonymizedAt ??= clock.GetUtcNow();
                e.AnonymizationRequest ??= context.RequestNumber;
            }

            await db.SaveChangesAsync(ct);
        }

        plan.Fields(contact, "Telefone e e-mails pessoais", "hr.employees", hasContact ? 1 : 0)
            .Fields(geo, "Localização das marcações de ponto", "hr.time_entries", entries.Count)
            .Fields(dependentsFree, "Dependentes sem obrigação legal", "hr.employee_dependents", withoutObligation.Count)
            .Fields(bank, "Conta de salário e chave Pix", "hr.employees", hasBank ? 1 : 0)
            .Fields(address, "Endereço", "hr.employees", hasAddress ? 1 : 0)
            .Fields(labor, "Nome, CPF, documentos pessoais e nascimento", "hr.employees", 1)
            .Fields(labor, "Dependentes do IR e do salário-família", "hr.employee_dependents", withObligation.Count)
            .Fields(labor, "Motivos da linha do tempo e justificativas do ponto", "hr.employee_history, hr.time_adjustments", history.Count + adjustments.Count)
            .Fields(labor, "Motivos de saúde, afastamentos e comentários do espelho de ponto, e o acordo de banco de horas", "hr.timesheets, hr.timesheet_day_reviews, hr.employees", timesheetTexts)
            .Fields(labor, "Retrato das entradas, memória de cálculo e motivos dos avulsos da folha calculada", "hr.payslips, hr.payslip_lines", calculated.Count + calculatedLines.Count)
            .Fields(labor, "Motivos do reinício e do cancelamento das férias e a divisão do INSS das férias", "hr.vacation_periods, hr.vacation_schedules", vacationPeriods.Count + vacationSchedules.Count)
            .Fields(labor, "Documentos, atestados, holerites e espelhos em PDF (o arquivo é apagado)", "shared.attachments", files)
            .Keep("Folhas e holerites: mantidos, com os valores", "hr.payslips", payslips.Count)
            .Keep("Espelhos de ponto: mantidos, com os totais do fechamento", "hr.timesheets", timesheets.Count)
            .Keep("Marcações de ponto: mantidas, com data e hora", "hr.time_entries", await db.TimeEntries.CountAsync(t => t.EmployeeId == id, ct))
            .Keep(terminated ? "Cadastro do funcionário: marcado \"Anonimizado\", só leitura" : "Funcionário ativo: o cadastro continua em uso", "hr.employees", 1);
        return plan.Build();
    }

    private static void AnonymizeDependent(EmployeeDependent d, string marker)
    {
        d.Name = marker;
        d.Cpf = null;
        d.BirthDate = PersonalDataMarker.NoDate;
    }

    private static void AnonymizeLabor(Employee e, string marker)
    {
        e.Name = marker;
        e.Cpf = PersonalDataMarker.Document(e.Id);
        e.BirthDate = PersonalDataMarker.NoDate;
        e.Rg = null;
        e.Pis = null;
        e.CtpsNumber = null;
        e.CtpsSeries = null;
        e.VoterId = null;
        e.CnhNumber = null;
        e.CnhCategory = null;
        e.CnhExpiry = null;
        e.MilitaryCertificate = null;
        e.HourBankAgreementOn = null;
    }

    /// <summary>Dias congelados do espelho sem dado de saúde: abono de saúde vira "Falta justificada" e o afastamento fica sem o tipo.</summary>
    private static string? WithoutHealth(string? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var days = JsonSerializer.Deserialize<List<TimesheetDayResult>>(snapshot, TimesheetService.SnapshotJson) ?? [];
        var clean = days.Select(d => d with
        {
            Absences = d.Absences.Select(a => AbsenceReasons.IsHealth(a.Reason) ? a with { Reason = AbsenceReason.JustifiedAbsence } : a).ToList(),
            Leave = d.Leave is null ? null : d.Leave with { Reason = null },
        }).ToList();
        return JsonSerializer.Serialize(clean, TimesheetService.SnapshotJson);
    }

    /// <summary>Abono por motivo do <c>ponto.csv</c> sem dado de saúde: os minutos de saúde somam em "Falta justificada".</summary>
    private static string? WithoutHealthByReason(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var merged = new Dictionary<AbsenceReason, int>();
        foreach (var (reason, minutes) in TimesheetRules.ParseByReason(text))
        {
            var key = AbsenceReasons.IsHealth(reason) ? AbsenceReason.JustifiedAbsence : reason;
            merged[key] = merged.GetValueOrDefault(key) + minutes;
        }

        return TimesheetRules.FormatByReason(merged);
    }

    private static string? Join(string? a, string? b) => a is null ? b : b is null ? a : a + " / " + b;

    private static string Kinship(DependentKinship k) => k switch
    {
        DependentKinship.Child => "Filho(a)",
        DependentKinship.Stepchild => "Enteado(a)",
        DependentKinship.Spouse => "Cônjuge",
        DependentKinship.Partner => "Companheiro(a)",
        DependentKinship.Parent => "Pai ou mãe",
        DependentKinship.Ward => "Tutelado(a)",
        _ => "Outro",
    };
}
