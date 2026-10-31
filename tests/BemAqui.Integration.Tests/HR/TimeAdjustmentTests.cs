using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-01b: lançamento manual do RH com aprovação e <c>Idempotency-Key</c>, o dia pela jornada, as datas, o pedido do funcionário (IDOR,
/// corpo sem <c>employeeId</c>, cancelar), o abono com o documento protegido por <c>hr.view_health</c>, aprovar (incluir, desconsiderar,
/// abono) e reprovar, a segregação, a concorrência, o lote parcial, o período fechado, a notificação e a pauta, as três camadas e o histórico.
/// Banco desta classe, com mais de um aprovador (o Proprietário único fica em <see cref="TimeAdjustmentSoleOwnerTests"/>).
/// </summary>
public class TimeAdjustmentTests(TenancyFixture tenancy) : TimeAdjustmentTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private static MultipartFormDataContent File(string name, byte[] bytes, string type) => new() { { new ByteArrayContent(bytes) { Headers = { ContentType = new(type) } }, "file", name } };

    private static readonly byte[] Pdf = "%PDF-1.4\n%%EOF"u8.ToArray();

    /// <summary>CA 2 e 17: o lançamento do RH nasce pendente (uma vez por chave), nada muda no ponto e quem aprova é avisado sem a justificativa.</summary>
    [Fact]
    public async Task Lancamento_do_RH_nasce_pendente_uma_vez_por_chave_e_avisa_quem_aprova()
    {
        var owner = await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Paula Dona");
        var hr = await PersonAsync("hr:view,create", employee: false, name: "Rita RH");
        var employee = await EmployeeAsync("Bruno Lima");
        var day = LastWorkday();
        var key = Guid.NewGuid();

        var first = await hr.Client.SendAsync(Post(Of(employee), new { kind = "AddEntry", day, time = "17:50", entryType = "Out", justification = Canary }, key));
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var again = await hr.Client.SendAsync(Post(Of(employee), new { kind = "AddEntry", day, time = "17:50", entryType = "Out", justification = Canary }, key));
        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await hr.Client.PostAsJsonAsync(Of(employee), new { kind = "AddEntry", day, time = "17:50", entryType = "Out", justification = Canary })).IsSuccessStatusCode.Should().BeFalse("exige Idempotency-Key");

        var view = await JsonAsync(first);
        view.GetProperty("status").GetString().Should().Be("Pending");
        view.GetProperty("source").GetString().Should().Be("HR");
        view.GetProperty("requestedBy").GetGuid().Should().Be(hr.UserId);
        view.GetProperty("summary").GetString().Should().Be("Incluir saída às 17:50");
        view.GetProperty("canCancel").GetBoolean().Should().BeTrue();
        var id = view.GetProperty("id").GetGuid();
        (await DbAsync(db => db.TimeAdjustments.CountAsync(a => a.EmployeeId == employee))).Should().Be(1, "a mesma chave não duplica");
        (await DbAsync(db => db.TimeEntries.CountAsync(e => e.EmployeeId == employee))).Should().Be(0, "o lançamento também passa por aprovação");

        var notices = await DbAsync(db => db.Notifications.AsNoTracking().Where(n => n.Link.EndsWith(id.ToString())).ToListAsync());
        notices.Should().ContainSingle(n => n.UserId == owner.UserId);
        notices.Should().NotContain(n => n.UserId == hr.UserId);
        var notice = notices.Single(n => n.UserId == owner.UserId);
        notice.Title.Should().Be("Ajuste de ponto de Bruno Lima para aprovar");
        notice.DedupKey.Should().Be("time-adjustment:" + id);
        notice.Link.Should().Be("/app/rh/ponto/ajustes?pedido=" + id);

        // Pauta (DSH-04): o Proprietário vê o pedido para decidir; quem pediu não tem hr.approve.
        var agenda = await JsonAsync(await owner.Client.GetAsync("/api/v1/me/agenda"));
        var item = agenda.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("category").GetString() == "hr.time_adjustments_pending");
        var entries = item.GetProperty("entries").EnumerateArray().ToList();
        (entries.Any(e => e.GetProperty("link").GetString() == "/app/rh/ponto/ajustes?pedido=" + id) || item.GetProperty("count").GetInt32() > entries.Count)
            .Should().BeTrue("o pedido entra na pauta (as linhas mostram só os primeiros da classe)");
        item.GetRawText().Should().NotContain(Canary);
        LogText().Should().NotContain(Canary, "a justificativa nunca vai para o log");
        (await AuditAsync(nameof(TimeAdjustment), id.ToString())).Should().ContainSingle(a => a.Action == AuditActions.Created && !a.Changes.Contains(Canary), "auditoria com a justificativa mascarada");
    }

    /// <summary>CA 3 e 4: o dia pela jornada vem preenchido e cria um pedido por marcação; futuro e fora do período em aberto, 422.</summary>
    [Fact]
    public async Task Dia_pela_jornada_cria_um_pedido_por_marcacao_e_datas_fora_sao_recusadas()
    {
        var hr = await PersonAsync("hr:view,create", employee: false);
        var employee = await EmployeeAsync();
        var day = LastWorkday();

        var proposal = await JsonAsync(await hr.Client.GetAsync(Of(employee) + "/schedule-day?day=" + day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        proposal.GetProperty("schedule").GetString().Should().Be("08:00–17:48, intervalo 1 h");
        proposal.GetProperty("existingEntries").GetInt32().Should().Be(0);
        var entries = proposal.GetProperty("entries").EnumerateArray().Select(e => (e.GetProperty("type").GetString(), e.GetProperty("time").GetString())).ToList();
        entries.Should().Equal(("In", "08:00"), ("BreakStart", "12:00"), ("BreakEnd", "13:00"), ("Out", "17:48"));

        var created = await hr.Client.SendAsync(Post(Of(employee) + "/day", new
        {
            day, justification = "Crachá esquecido em casa",
            entries = new[] { new { type = "In", time = "08:05" }, new { type = "BreakStart", time = "12:00" }, new { type = "BreakEnd", time = "13:00" }, new { type = "Out", time = "17:48" } },
        }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await JsonAsync(created)).EnumerateArray().Select(a => a.GetProperty("summary").GetString()).Should().Equal(
            "Incluir entrada às 08:05", "Incluir saída para o intervalo às 12:00", "Incluir volta do intervalo às 13:00", "Incluir saída às 17:48");

        var future = await hr.Client.SendAsync(Post(Of(employee), new { kind = "AddEntry", day = BusinessTime.Today(TimeProvider.System).AddDays(1), time = "08:00", entryType = "In", justification = "Teste de data futura" }));
        future.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await future.Content.ReadAsStringAsync()).Should().Contain("Não é possível lançar marcação no futuro.");

        Periods.OldestOpen = day;
        try
        {
            var old = await hr.Client.SendAsync(Post(Of(employee), new { kind = "AddEntry", day = day.AddDays(-1), time = "08:00", entryType = "In", justification = "Teste de período antigo" }));
            old.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await old.Content.ReadAsStringAsync()).Should().Contain("Data fora do período em aberto.");
        }
        finally
        {
            Periods.OldestOpen = null;
        }

        var shortText = await hr.Client.SendAsync(Post(Of(employee), new { kind = "AddEntry", day, time = "08:00", entryType = "In", justification = "curta" }));
        shortText.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>CA 5 e 6: o funcionário pede pela sessão (corpo sem employeeId), só desconsidera marcação dele (404 para a de outro) e cancela.</summary>
    [Fact]
    public async Task Funcionario_pede_pela_sessao_so_nas_proprias_marcacoes_e_cancela()
    {
        var ana = await PersonAsync(name: "Ana Souza");
        var other = await PersonAsync(name: "Carla Dias");
        var day = LastWorkday();
        var mine = await EntryAsync(ana.EmployeeId, day, 7, 12, TimeEntryType.In);
        var theirs = await EntryAsync(other.EmployeeId, day, 7, 30, TimeEntryType.In);

        (await ana.Client.SendAsync(Post(Mine, new { kind = "AddEntry", day, time = "17:50", entryType = "Out", justification = "Esqueci a saída", employeeId = other.EmployeeId })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "o funcionário vem da sessão (ADR 0035)");
        var idor = await ana.Client.SendAsync(Post(Mine, new { kind = "VoidEntry", entryId = theirs, justification = "Não fui eu que marquei" }));
        idor.StatusCode.Should().Be(HttpStatusCode.NotFound, "marcação de outro funcionário (IDOR)");

        var voided = await ana.Client.SendAsync(Post(Mine, new { kind = "VoidEntry", entryId = mine, justification = "Marquei entrada duas vezes" }));
        voided.StatusCode.Should().Be(HttpStatusCode.Created, await voided.Content.ReadAsStringAsync());
        var request = await JsonAsync(voided);
        request.GetProperty("summary").GetString().Should().Be("Desconsiderar entrada às 07:12");
        request.GetProperty("source").GetString().Should().Be("Employee");
        (await ana.Client.SendAsync(Post(Mine, new { kind = "VoidEntry", entryId = mine, justification = "Marquei entrada duas vezes" }))).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "um pedido pendente por marcação");

        var list = await JsonAsync(await ana.Client.GetAsync(Mine));
        list.EnumerateArray().Should().ContainSingle(a => a.GetProperty("id").GetGuid() == request.GetProperty("id").GetGuid() && a.GetProperty("statusLabel").GetString() == "Aguardando aprovação");
        (await JsonAsync(await other.Client.GetAsync(Mine))).GetArrayLength().Should().Be(0);

        var id = request.GetProperty("id").GetGuid();
        (await other.Client.PostAsJsonAsync($"{Mine}/{id}/cancel", new { version = request.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cancelled = await ana.Client.PostAsJsonAsync($"{Mine}/{id}/cancel", new { version = request.GetProperty("version").GetUInt32() });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await JsonAsync(cancelled)).GetProperty("status").GetString().Should().Be("Cancelled");
        (await DbAsync(db => db.TimeEntries.SingleAsync(e => e.Id == mine))).Status.Should().Be(TimeEntryStatus.Valid);
    }

    /// <summary>
    /// CA 7 a 9: abono com motivo de saúde e o atestado: o funcionário anexa e vê o que enviou; sem hr.view_health, só "Ausência justificada"
    /// e "Documento anexado", sem motivo, justificativa nem arquivo no JSON; com ela, tudo, e o download grava SensitiveRead; o anexo precisa
    /// ser do pedido da rota; a rota genérica não grava; nada no log.
    /// </summary>
    [Fact]
    public async Task Abono_de_saude_com_atestado_protegido_por_view_health()
    {
        const string Saude = "Atestado da Dra Saude 88";
        var ana = await PersonAsync(name: "Ana Souza");
        var viewer = await PersonAsync("hr:view,create", employee: false);
        var health = await PersonAsync("hr:view,create,view_health", employee: false);
        var day = LastWorkday();

        (await viewer.Client.SendAsync(Post(Of(ana.EmployeeId), new { kind = "Absence", day, absenceReason = "MedicalCertificate", absenceMinutes = 240, justification = "Consulta no período da tarde" })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "motivo de saúde só com hr.view_health");
        (await ana.Client.SendAsync(Post(Mine, new { kind = "Absence", day, absenceReason = "Maternity", justification = "Licença registrada no ponto" })))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Maternidade é só afastamento");

        var created = await ana.Client.SendAsync(Post(Mine, new { kind = "Absence", day, absenceReason = "MedicalCertificate", absenceMinutes = 240, justification = Saude }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var request = await JsonAsync(created);
        var id = request.GetProperty("id").GetGuid();
        request.GetProperty("summary").GetString().Should().Be("Abonar 4 h: Atestado médico");

        Guid attachmentId;
        using (var content = File("atestado.pdf", Pdf, "application/pdf"))
        {
            var uploaded = await ana.Client.PostAsync($"{Mine}/{id}/documents", content);
            uploaded.StatusCode.Should().Be(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
            attachmentId = (await JsonAsync(uploaded)).GetProperty("id").GetGuid();
        }

        using (var generic = File("atestado.pdf", Pdf, "application/pdf"))
        {
            (await health.Client.PostAsync("/api/v1/attachments/TimeAdjustmentDocument/" + id, generic)).IsSuccessStatusCode.Should().BeFalse("só a rota do pedido grava");
        }

        var stored = await DbAsync(db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId));
        stored.StoragePath.Should().StartWith(Tenancy.TenantA.Id.ToString("N") + "/", "storage com o prefixo do tenant");
        (await ana.Client.GetAsync($"{Mine}/{id}/documents/{attachmentId}")).StatusCode.Should().Be(HttpStatusCode.OK, "o funcionário vê o que enviou");

        var hidden = await viewer.Client.GetAsync($"{Of(ana.EmployeeId)}/{id}");
        var hiddenText = await hidden.Content.ReadAsStringAsync();
        hidden.StatusCode.Should().Be(HttpStatusCode.OK, hiddenText);
        hiddenText.Should().NotContain(Saude).And.NotContain("MedicalCertificate").And.NotContain("Atestado médico").And.NotContain("atestado.pdf");
        var adjustment = (await JsonAsync(hidden)).GetProperty("adjustment");
        adjustment.GetProperty("protected").GetBoolean().Should().BeTrue();
        adjustment.GetProperty("hasDocument").GetBoolean().Should().BeTrue();
        adjustment.GetProperty("absenceReasonLabel").GetString().Should().Be("Ausência justificada");
        adjustment.GetProperty("summary").GetString().Should().Be("Abonar 4 h: Ausência justificada");
        (await viewer.Client.GetAsync($"{Of(ana.EmployeeId)}/{id}/documents/{attachmentId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await viewer.Client.GetAsync(Adjustments + "?status=Pending")).Content.ReadAsStringAsync()).Should().NotContain(Saude);

        var shown = (await JsonAsync(await health.Client.GetAsync($"{Of(ana.EmployeeId)}/{id}"))).GetProperty("adjustment");
        shown.GetProperty("absenceReason").GetString().Should().Be("MedicalCertificate");
        shown.GetProperty("justification").GetString().Should().Be(Saude);
        shown.GetProperty("documents").EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(attachmentId);
        var download = await health.Client.GetAsync($"{Of(ana.EmployeeId)}/{id}/documents/{attachmentId}");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuditAsync("TimeAdjustmentDocument", id.ToString())).Should().Contain(a => a.Action == AuditActions.SensitiveRead && a.UserId == health.UserId);

        // Autorização pelo registro: o anexo de um pedido pela rota de outro pedido (ou de outro funcionário), 404.
        var second = await JsonAsync(await ana.Client.SendAsync(Post(Mine, new { kind = "Absence", day = day.AddDays(-7), absenceReason = "AttendanceNote", justification = "Declaração do cartório" })));
        (await health.Client.GetAsync($"{Of(ana.EmployeeId)}/{second.GetProperty("id").GetGuid()}/documents/{attachmentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await health.Client.GetAsync($"{Of(await EmployeeAsync())}/{id}/documents/{attachmentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "pedido de outro funcionário");
        LogText().Should().NotContain(Saude);
    }

    /// <summary>
    /// CA 11, 12 e 19: aprovar inclui a marcação Manual com quem aprovou e o pedido, desconsidera a original (Voided, não apagada, com o texto
    /// para quem tem hr.view) e faz valer o abono; reprovar exige comentário e não mexe nas marcações; o solicitante é avisado; histórico.
    /// </summary>
    [Fact]
    public async Task Aprovar_inclui_desconsidera_e_abona_e_reprovar_nao_muda_nada()
    {
        var approver = await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Paula Dona");
        await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Outro Dono");
        var ana = await PersonAsync(name: "Ana Souza");
        var day = LastWorkday();
        var original = await EntryAsync(ana.EmployeeId, day, 7, 12, TimeEntryType.In);
        await EntryAsync(ana.EmployeeId, day, 8, 0, TimeEntryType.In);

        var add = await JsonAsync(await ana.Client.SendAsync(Post(Mine, new { kind = "AddEntry", day, time = "17:50", entryType = "Out", justification = "Esqueci de marcar a saída" })));
        var drop = await JsonAsync(await ana.Client.SendAsync(Post(Mine, new { kind = "VoidEntry", entryId = original, justification = "Entrada marcada em dobro" })));
        var absence = await JsonAsync(await ana.Client.SendAsync(Post(Mine, new { kind = "Absence", day = day.AddDays(-7), absenceReason = "JustifiedAbsence", justification = "Audiência na justiça" })));
        var rejected = await JsonAsync(await ana.Client.SendAsync(Post(Mine, new { kind = "AddEntry", day, time = "12:00", entryType = "BreakStart", justification = "Saída para o almoço" })));

        var detail = await JsonAsync(await approver.Client.GetAsync($"{Adjustments}/{add.GetProperty("id").GetGuid()}"));
        detail.GetProperty("adjustment").GetProperty("canDecide").GetBoolean().Should().BeTrue();
        var impact = detail.GetProperty("impact");
        impact.GetProperty("before").GetArrayLength().Should().Be(2);
        impact.GetProperty("after").GetArrayLength().Should().Be(3);
        impact.GetProperty("workedMinutesAfter").GetInt32().Should().Be(9 * 60 + 50, "entrada 08:00 até a saída 17:50 (o par fechado)");

        var approvedAdd = await ApproveAsync(approver.Client, add);
        approvedAdd.StatusCode.Should().Be(HttpStatusCode.OK, await approvedAdd.Content.ReadAsStringAsync());
        (await JsonAsync(approvedAdd)).GetProperty("status").GetString().Should().Be("Approved");
        var manual = await DbAsync(db => db.TimeEntries.AsNoTracking().SingleAsync(e => e.AdjustmentId == add.GetProperty("id").GetGuid()));
        manual.Source.Should().Be(TimeEntrySource.Manual);
        manual.ApprovedBy.Should().Be(approver.UserId);
        manual.Type.Should().Be(TimeEntryType.Out);
        BusinessTime.ToBusiness(manual.At).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture).Should().Be("17:50");

        (await ApproveAsync(approver.Client, drop)).StatusCode.Should().Be(HttpStatusCode.OK);
        var voided = await DbAsync(db => db.TimeEntries.AsNoTracking().SingleAsync(e => e.Id == original));
        voided.Status.Should().Be(TimeEntryStatus.Voided, "nada é apagado");
        voided.VoidAdjustmentId.Should().Be(drop.GetProperty("id").GetGuid());
        var rows = await JsonAsync(await approver.Client.GetAsync($"/api/v1/hr/employees/{ana.EmployeeId}/time-entries?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"));
        rows.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == original).GetProperty("voidedNote").GetString()
            .Should().Be($"Desconsiderada por Paula Dona em {BusinessTime.Today(TimeProvider.System):dd/MM/yyyy}: Entrada marcada em dobro");

        (await ApproveAsync(approver.Client, absence)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DbAsync(db => db.TimeAdjustments.SingleAsync(a => a.Id == absence.GetProperty("id").GetGuid()))).Status.Should().Be(TimeAdjustmentStatus.Approved);

        var rejectUrl = $"{Adjustments}/{rejected.GetProperty("id").GetGuid()}/reject";
        (await approver.Client.PostAsJsonAsync(rejectUrl, new { version = rejected.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "comentário obrigatório");
        var rejection = await approver.Client.PostAsJsonAsync(rejectUrl, new { version = rejected.GetProperty("version").GetUInt32(), comment = "Intervalo já está no app" });
        rejection.StatusCode.Should().Be(HttpStatusCode.OK, await rejection.Content.ReadAsStringAsync());
        (await JsonAsync(rejection)).GetProperty("status").GetString().Should().Be("Rejected");
        (await DbAsync(db => db.TimeEntries.CountAsync(e => e.EmployeeId == ana.EmployeeId))).Should().Be(3, "duas originais e a incluída; a reprovada não cria");

        var notices = await DbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == ana.UserId).Select(n => n.Title).ToListAsync());
        notices.Should().Contain($"Seu ajuste de {day:dd/MM/yyyy} foi aprovado.").And.Contain($"Seu ajuste de {day:dd/MM/yyyy} foi reprovado: Intervalo já está no app");
        (await ApproveAsync(approver.Client, add)).StatusCode.Should().Be(HttpStatusCode.Conflict, "já decidido");
        var history = await AuditAsync(nameof(TimeAdjustment), drop.GetProperty("id").GetGuid().ToString());
        history.Should().Contain(a => a.Action == AuditActions.Updated && a.UserId == approver.UserId && a.Changes.Contains("Approved"));
        (await AuditAsync(nameof(TimeEntry), original.ToString())).Should().Contain(a => a.Action == AuditActions.Updated && a.Changes.Contains("Voided"));
    }

    /// <summary>CA 13 e 14: quem pediu e o dono das marcações não aprovam (403); duas decisões simultâneas, só uma grava (409).</summary>
    [Fact]
    public async Task Segregacao_e_concorrencia_na_decisao()
    {
        var paula = await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Paula Dona");
        var marcos = await PersonAsync(Group.OwnerSeedKey, name: "Marcos Dono");
        var employee = await EmployeeAsync();
        var day = LastWorkday();

        var own = await LaunchAsync(paula.Client, employee, day, "08:00", "In");
        var ownResponse = await ApproveAsync(paula.Client, own);
        ownResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ownResponse.Content.ReadAsStringAsync()).Should().Contain("Você não pode aprovar um ajuste seu.");

        // O ajuste das próprias marcações, pedido por outra pessoa: o dono das marcações também não aprova.
        var onMarcos = await LaunchAsync(paula.Client, marcos.EmployeeId, day, "08:00", "In");
        (await ApproveAsync(marcos.Client, onMarcos)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var shared = await LaunchAsync(marcos.Client, employee, day, "17:48", "Out");
        var third = await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Tereza Dona");
        var decisions = await Task.WhenAll(ApproveAsync(paula.Client, shared), ApproveAsync(third.Client, shared));
        decisions.Select(d => d.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await DbAsync(db => db.TimeEntries.CountAsync(e => e.AdjustmentId == shared.GetProperty("id").GetGuid()))).Should().Be(1, "só uma decisão grava");
    }

    /// <summary>CA 15 e 16: o lote decide cada um na sua transação (o próprio e o já decidido voltam com o motivo); período fechado, 409.</summary>
    [Fact]
    public async Task Lote_parcial_e_periodo_fechado()
    {
        var paula = await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Paula Dona");
        await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Outra Dona");
        var hr = await PersonAsync("hr:view,create", employee: false);
        var employee = await EmployeeAsync();
        var day = LastWorkday();

        var good = await LaunchAsync(hr.Client, employee, day, "08:00", "In");
        var own = await LaunchAsync(paula.Client, employee, day, "12:00", "BreakStart");
        var decided = await LaunchAsync(hr.Client, employee, day, "13:00", "BreakEnd");
        (await hr.Client.PostAsJsonAsync($"{Adjustments}/{decided.GetProperty("id").GetGuid()}/cancel", new { version = decided.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.OK);

        var batch = await paula.Client.PostAsJsonAsync(Adjustments + "/approve-batch", new
        {
            items = new[] { good, own, decided }.Select(a => new { id = a.GetProperty("id").GetGuid(), version = a.GetProperty("version").GetUInt32() }).ToArray(),
        });
        batch.StatusCode.Should().Be(HttpStatusCode.OK, await batch.Content.ReadAsStringAsync());
        var result = await JsonAsync(batch);
        result.GetProperty("approved").GetInt32().Should().Be(1);
        result.GetProperty("failed").GetInt32().Should().Be(2);
        var items = result.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid());
        items[good.GetProperty("id").GetGuid()].GetProperty("approved").GetBoolean().Should().BeTrue();
        items[own.GetProperty("id").GetGuid()].GetProperty("code").GetString().Should().Be("own_time_adjustment");
        items[decided.GetProperty("id").GetGuid()].GetProperty("code").GetString().Should().Be("time_adjustment_decided");

        var pending = await LaunchAsync(hr.Client, employee, day, "17:48", "Out");
        Periods.ClosedMonth = day;
        try
        {
            var closed = await ApproveAsync(paula.Client, pending);
            closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await closed.Content.ReadAsStringAsync()).Should().Contain($"O período {day:MM/yyyy} está fechado. Peça a reabertura ao RH.");
            var launch = await hr.Client.SendAsync(Post(Of(employee), new { kind = "AddEntry", day, time = "18:00", entryType = "Out", justification = "Hora extra não marcada" }));
            launch.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeAsync(launch)).Should().Be("timesheet_period_closed");
            (await paula.Client.PostAsJsonAsync($"{Adjustments}/{pending.GetProperty("id").GetGuid()}/reject", new { version = pending.GetProperty("version").GetUInt32(), comment = "Não" }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            Periods.ClosedMonth = null;
        }
    }

    /// <summary>CA 18: módulo não liberado, 403 module_not_released; lançar sem hr.create, aprovar sem hr.approve, 403; IDOR na rota do funcionário.</summary>
    [Fact]
    public async Task Tres_camadas_e_IDOR_na_rota_do_funcionario()
    {
        var viewer = await PersonAsync("hr:view", employee: false);
        var hr = await PersonAsync("hr:view,create", employee: false);
        var ana = await PersonAsync();
        var employee = await EmployeeAsync();
        var day = LastWorkday();
        var launched = await LaunchAsync(hr.Client, employee, day, "08:00", "In");
        var id = launched.GetProperty("id").GetGuid();

        (await viewer.Client.SendAsync(Post(Of(employee), new { kind = "AddEntry", day, time = "08:00", entryType = "In", justification = "Sem permissão de criar" }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var forbidden = await ApproveAsync(hr.Client, launched);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(forbidden)).Should().Be("forbidden");
        (await ana.Client.GetAsync(Adjustments)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "ver os pedidos dos outros exige hr.view");
        (await viewer.Client.GetAsync($"{Of(ana.EmployeeId)}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "o pedido precisa ser do funcionário da rota");
        (await viewer.Client.GetAsync($"{Of(employee)}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await SetModuleReleasedAsync("hr", false);
        try
        {
            var blocked = await hr.Client.GetAsync(Adjustments);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(blocked)).Should().Be("module_not_released");
            (await CodeAsync(await ana.Client.GetAsync(Mine))).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("hr", true);
        }
    }
}
