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
/// O espelho e o "Li e confirmo" entram quando a RH-01c existir.
/// </summary>
[PersonalDataCovers(typeof(Employee), typeof(EmployeeDependent), typeof(EmployeeHistoryEntry), typeof(TimeEntry), typeof(TimeAdjustment),
    typeof(Payslip), typeof(PayslipLine), typeof(PayrollForecast), typeof(PayrollRunCharge), typeof(PayrollRun), typeof(JobPosition))]
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
            (true, true) => "Espelho de ponto e \"Li e confirmo\": ainda não existem no sistema (RH-01c).",
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
            ("Dependente no IR", PersonalDataMask.YesNo(d.IrDependent)), ("Salário-família", PersonalDataMask.YesNo(d.FamilyAllowance)))).ToList());
    }

    private async Task<IEnumerable<PersonalDataBlock>> TimeClockAsync(Guid id, bool sensitive, bool health, CancellationToken ct)
    {
        var entries = await db.TimeEntries.AsNoTracking().Where(t => t.EmployeeId == id).OrderBy(t => t.At).ToListAsync(ct);
        var consents = await db.TimeClockGeoConsents.AsNoTracking().Where(c => c.EmployeeId == id).OrderBy(c => c.AcceptedAt).ToListAsync(ct);
        var adjustments = await db.TimeAdjustments.AsNoTracking().Where(a => a.EmployeeId == id).OrderBy(a => a.RequestedAt).ToListAsync(ct);
        return
        [
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

    private async Task<PersonalDataBlock> PayslipsAsync(Guid id, CancellationToken ct)
    {
        var slips = await (from p in db.Payslips.AsNoTracking()
                           join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                           where p.EmployeeId == id && (r.Status == PayrollRunStatus.Closed || r.Status == PayrollRunStatus.Paid)
                           orderby r.Period, r.Type
                           select new { p.Id, r.Period, r.Type, p.Gross, p.Deductions, p.Net, p.FgtsAmount, p.CreatedAt }).ToListAsync(ct);
        var ids = slips.Select(s => s.Id).ToList();
        var lines = (await db.PayslipLines.AsNoTracking().Where(l => ids.Contains(l.PayslipId)).OrderBy(l => l.Sort).ToListAsync(ct)).ToLookup(l => l.PayslipId);
        return new PersonalDataBlock("Holerites", "hr.payslips", slips.Select(s => PersonalDataRecord.Of(s.CreatedAt,
            ("Competência", PayrollRules.PeriodLabel(s.Period)), ("Tipo", PayrollRules.TypeLabel(s.Type)), ("Proventos", PersonalDataMask.Money(s.Gross)),
            ("Descontos", PersonalDataMask.Money(s.Deductions)), ("Líquido", PersonalDataMask.Money(s.Net)), ("FGTS do mês", PersonalDataMask.Money(s.FgtsAmount)),
            ("Linhas", string.Join("; ", lines[s.Id].Select(l => $"{l.Code} {l.Description}: {PersonalDataMask.Money(l.Amount)}" + (l.Reference is null ? string.Empty : $" ({l.Reference})")))))).ToList());
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
        var files = await attachments.CountAsync(EmployeeComplementService.DocumentOwner, [id], ct)
            + await attachments.CountAsync(TimeAdjustmentService.DocumentOwner, adjustmentIds, ct)
            + await attachments.CountAsync(PayslipDocumentRules.OwnerType, payslips, ct);
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

                await attachments.RemoveContentAsync(EmployeeComplementService.DocumentOwner, [id], marker, ct);
                await attachments.RemoveContentAsync(TimeAdjustmentService.DocumentOwner, adjustmentIds, marker, ct);
                await attachments.RemoveContentAsync(PayslipDocumentRules.OwnerType, payslips, marker, ct);
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
            .Fields(labor, "Documentos, atestados e holerites em PDF (o arquivo é apagado)", "shared.attachments", files)
            .Keep("Folhas e holerites: mantidos, com os valores", "hr.payslips", payslips.Count)
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
