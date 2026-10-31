using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>
/// Configurações → Privacidade (LGPD-01a, ADR 0018 e 0037): pedido do titular atendido só pelo Proprietário (conferido aqui, em toda
/// chamada; 403 <c>forbidden</c> para os demais, mesmo com <c>settings.edit</c> ou <c>hr.view_sensitive</c>). O pedido nasce <c>Open</c> com o
/// número <c>LGPD-AAAA-NNNN</c> e o prazo em dias corridos; a identidade conferida é exigida antes de exportar (409
/// <c>subject_identity_not_verified</c>); a exportação roda num job com o <c>tenantId</c>; o download é por link de uso único de 15 minutos.
/// Nada é apagado: só concluído ou recusado. Toda mudança passa pela auditoria do pedido (<c>IAuditable</c>).
/// </summary>
public sealed class PrivacyRequestService(
    IIdentityDb db,
    INumberSequence numbers,
    IEnumerable<IPersonalDataProvider> providers,
    IPermissionService permissions,
    IUserDirectory users,
    ICurrentUser currentUser,
    AttachmentService attachments,
    IAuditWriter audit,
    IBackgroundJobClient jobs,
    ITenantContext tenant,
    TimeProvider clock)
{
    public const string ResponseDaysKey = "privacy.response_days";
    public const string NotificationType = "settings.privacy";
    public const string ZipContentType = "application/zip";

    public static string Link(Guid id) => "/app/configuracoes/privacidade/" + id.ToString("D");

    /// <summary>Só o Proprietário (CA 2): o servidor confere em toda chamada, como nas alçadas da FIN-03b.</summary>
    public async Task<Error?> OwnerOnlyAsync(CancellationToken cancellationToken = default) =>
        currentUser.UserId is { } me && (await permissions.GetAsync(me, cancellationToken)).IsOwner
            ? null
            : Error.Forbidden(Error.Codes.Forbidden, "Só o Proprietário atende os pedidos do titular.");

    public async Task<Result<PagedList<PrivacyRequestRow>>> ListAsync(PrivacyRequestStatus? status, PrivacyRequestKind? kind, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        ArgumentNullException.ThrowIfNull(page);
        var query = db.PrivacyRequests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        if (kind is { } k)
        {
            query = query.Where(r => r.Kind == k);
        }

        var today = BusinessTime.Today(clock);
        var list = await query.OrderBy(r => r.Status).ThenBy(r => r.DueOn).ThenByDescending(r => r.CreatedAt).ToPagedListAsync(page, cancellationToken);
        return new PagedList<PrivacyRequestRow>(list.Items.Select(r => new PrivacyRequestRow(r.Id, r.Number, r.SubjectType, PrivacyRequestRules.MaskName(r.SubjectName), r.Kind,
            r.RequestedOn, r.DueOn, r.Status, PrivacyRequestRules.DueChip(r.Status, r.DueOn, today), r.IsIdentityVerified, r.ExportStatus)).ToList(), list.Page, list.PageSize, list.Total);
    }

    public async Task<Result<PrivacyRequestDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var request = await db.PrivacyRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return request is null ? NotFound() : await DetailAsync(request, cancellationToken);
    }

    /// <summary>Seletor de titular (CA 1): pergunta ao módulo dono de cada tipo, sem ler tabela de outro módulo.</summary>
    public async Task<Result<IReadOnlyList<PersonalDataSubjectMatch>>> SearchSubjectsAsync(PersonalDataSubjectKind kind, string? term, CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        if (kind == PersonalDataSubjectKind.Unknown || string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return Result<IReadOnlyList<PersonalDataSubjectMatch>>.Success([]);
        }

        var found = new List<PersonalDataSubjectMatch>();
        foreach (var provider in providers)
        {
            found.AddRange(await provider.SearchAsync(kind, term.Trim(), cancellationToken));
        }

        return found.Take(20).ToList();
    }

    /// <summary>"Registrar pedido" (CA 1): número <c>LGPD</c>, prazo pela data do pedido e o parâmetro, titular conferido no módulo dono.</summary>
    public async Task<Result<PrivacyRequestDetail>> CreateAsync(CreatePrivacyRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var today = BusinessTime.Today(clock);
        var errors = new Dictionary<string, string[]>();
        if (input.RequestedOn > today)
        {
            errors["requestedOn"] = ["A data do pedido não pode ser no futuro."];
        }

        if (input.Notes is { Length: > PrivacyRequest.NotesMaxLength })
        {
            errors["notes"] = ["A observação pode ter até 500 caracteres."];
        }

        string name;
        string? contact = null;
        if (input.SubjectType == PersonalDataSubjectKind.Unknown)
        {
            name = (input.SubjectName ?? string.Empty).Trim();
            contact = input.SubjectContact?.Trim();
            if (name.Length is 0 or > 150)
            {
                errors["subjectName"] = ["Informe o nome do titular (até 150 caracteres)."];
            }

            if (string.IsNullOrEmpty(contact) || contact.Length > 200)
            {
                errors["subjectContact"] = ["Informe um meio de contato do titular (até 200 caracteres)."];
            }
        }
        else
        {
            var subject = input.SubjectId is { } subjectId ? await ResolveAsync(input.SubjectType, subjectId, cancellationToken) : null;
            if (subject is null)
            {
                errors["subjectId"] = ["Escolha o titular na lista ou marque \"Titular não encontrado\"."];
                name = string.Empty;
            }
            else
            {
                name = subject.Name.Length > 150 ? subject.Name[..150] : subject.Name;
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", errors.Values.First()[0], errors);
        }

        var days = await ResponseDaysAsync(cancellationToken);
        var request = new PrivacyRequest
        {
            Number = await numbers.NextAsync(PrivacyRequest.NumberType, cancellationToken),
            SubjectType = input.SubjectType,
            SubjectId = input.SubjectType == PersonalDataSubjectKind.Unknown ? null : input.SubjectId,
            SubjectName = name,
            SubjectContact = contact,
            Kind = input.Kind,
            RequestedOn = input.RequestedOn,
            Channel = input.Channel,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            DueOn = PrivacyRequestRules.DueOn(input.RequestedOn, days),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = currentUser.UserId,
        };
        db.PrivacyRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(request, cancellationToken);
    }

    /// <summary>"Conferir identidade" (CA 5): como, a data e a observação, sem copiar o documento; quem e quando ficam no pedido.</summary>
    public async Task<Result<PrivacyRequestDetail>> VerifyIdentityAsync(Guid id, VerifyIdentityInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (request, error) = await OpenAsync(id, input.Version, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (input.CheckedOn > BusinessTime.Today(clock) || input.CheckedOn < request!.RequestedOn.AddDays(-30))
        {
            return Error.Validation("validation", "Informe a data em que a identidade foi conferida (até hoje).", new Dictionary<string, string[]> { ["checkedOn"] = ["Data inválida."] });
        }

        if (input.Notes is { Length: > PrivacyRequest.NotesMaxLength })
        {
            return Error.Validation("validation", "A observação pode ter até 500 caracteres.", new Dictionary<string, string[]> { ["notes"] = ["Até 500 caracteres."] });
        }

        request.IdentityMethod = input.Method;
        request.IdentityCheckedOn = input.CheckedOn;
        request.IdentityNotes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        request.IdentityVerifiedAt = clock.GetUtcNow();
        request.IdentityVerifiedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(request, cancellationToken);
    }

    /// <summary>"Concluir pedido" (CA 4): a resposta (data, o que foi feito, se o arquivo foi enviado) e o pedido fica <c>Done</c>.</summary>
    public async Task<Result<PrivacyRequestDetail>> ConcludeAsync(Guid id, ConcludePrivacyRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (request, error) = await OpenAsync(id, input.Version, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var response = (input.Response ?? string.Empty).Trim();
        if (response.Length is 0 or > PrivacyRequest.NotesMaxLength)
        {
            return Error.Validation("validation", "Descreva o que foi feito (até 500 caracteres).", new Dictionary<string, string[]> { ["response"] = ["Descreva o que foi feito."] });
        }

        if (input.RespondedOn > BusinessTime.Today(clock) || input.RespondedOn < request!.RequestedOn)
        {
            return Error.Validation("validation", "A data da resposta fica entre a data do pedido e hoje.", new Dictionary<string, string[]> { ["respondedOn"] = ["Data inválida."] });
        }

        request.Response = response;
        request.RespondedOn = input.RespondedOn;
        request.FileSent = input.FileSent;
        request.Status = PrivacyRequestStatus.Done;
        request.ClosedAt = clock.GetUtcNow();
        request.ClosedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(request, cancellationToken);
    }

    /// <summary>"Recusar" (CA 4): exige o motivo (ex.: retenção legal, titular não identificado); o pedido fica <c>Refused</c>.</summary>
    public async Task<Result<PrivacyRequestDetail>> RefuseAsync(Guid id, RefusePrivacyRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (request, error) = await OpenAsync(id, input.Version, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is 0 or > PrivacyRequest.NotesMaxLength)
        {
            return Error.Validation("validation", "Informe o motivo da recusa (até 500 caracteres).", new Dictionary<string, string[]> { ["reason"] = ["Informe o motivo da recusa."] });
        }

        request!.RefusalReason = reason;
        request.Status = PrivacyRequestStatus.Refused;
        request.ClosedAt = clock.GetUtcNow();
        request.ClosedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(request, cancellationToken);
    }

    /// <summary>
    /// "Abrir cadastro" (CA 11): a correção é feita na tela de edição do cadastro, com a auditoria normal; o pedido guarda o link do que foi
    /// aberto. Só para pedido de Correção com a identidade conferida.
    /// </summary>
    public async Task<Result<PrivacyCorrectionTarget>> OpenCorrectionAsync(Guid id, PrivacyVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (request, error) = await OpenAsync(id, input.Version, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (request!.Kind != PrivacyRequestKind.Correction)
        {
            return Error.Conflict("privacy_request_not_correction", "Só o pedido de correção abre o cadastro por aqui.");
        }

        if (!request.IsIdentityVerified)
        {
            return IdentityNotVerified();
        }

        var subject = request.SubjectId is { } subjectId ? await ResolveAsync(request.SubjectType, subjectId, cancellationToken) : null;
        var link = subject?.Kind switch
        {
            PersonalDataSubjectKind.Party => "/app/cadastros/terceiros/" + subject.Id.ToString("D"),
            PersonalDataSubjectKind.Contact when subject.PartyId is { } party => "/app/cadastros/terceiros/" + party.ToString("D"),
            PersonalDataSubjectKind.Employee => "/app/rh/funcionarios/" + subject.Id.ToString("D"),
            PersonalDataSubjectKind.User => "/app/acessos/usuarios",
            _ => null,
        };
        if (link is null)
        {
            return Error.Conflict("privacy_subject_not_found", "O titular não tem cadastro no sistema: registre a resposta e conclua o pedido.");
        }

        request.CorrectionLink = link;
        await db.SaveChangesAsync(cancellationToken);
        return new PrivacyCorrectionTarget(link, await DetailAsync(request, cancellationToken));
    }

    /// <summary>
    /// "Exportar dados do titular" (CA 8 e 10): pedido de Acesso ou Portabilidade com a identidade conferida (senão 409
    /// <c>subject_identity_not_verified</c>); o job (só <c>tenantId</c> e ids) monta o ZIP e avisa no sino. A leitura sensível fica registrada
    /// agora, com o número do pedido, quem e o IP.
    /// </summary>
    public async Task<Result<PrivacyExportStarted>> StartExportAsync(Guid id, PrivacyVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (request, error) = await OpenAsync(id, input.Version, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (!PrivacyRequestRules.Exports(request!.Kind))
        {
            return Error.Conflict("privacy_request_not_export", "Só os pedidos de acesso e de portabilidade exportam os dados.");
        }

        if (!request.IsIdentityVerified)
        {
            return IdentityNotVerified();
        }

        if (request.ExportStatus == PrivacyExportStatus.Running)
        {
            return Error.Conflict("privacy_export_running", "A exportação já está sendo gerada. Aguarde o aviso no sino.");
        }

        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Exportação do titular fora do escopo de tenant.");
        request.ExportStatus = PrivacyExportStatus.Running;
        request.ExportError = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(PrivacyRequest), request.Id.ToString(), request.Number, cancellationToken);
        var args = new PrivacyExportArgs(request.Id, currentUser.UserId!.Value);
        jobs.Create<PrivacyExportJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
        return new PrivacyExportStarted(await DetailAsync(request, cancellationToken), "Gerando a exportação… Avisaremos no sino quando estiver pronta.");
    }

    /// <summary>"Baixar exportação" (CA 10): gera o link de uso único (15 min); só o hash fica no pedido.</summary>
    public async Task<Result<PrivacyDownloadLink>> CreateDownloadLinkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var request = await db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        if (request.ExportStatus != PrivacyExportStatus.Ready || request.ExportAttachmentId is null)
        {
            return Error.Conflict("privacy_export_not_ready", "A exportação ainda não está pronta.");
        }

        var token = OpaqueToken.New();
        request.DownloadTokenHash = OpaqueToken.Hash(token);
        request.DownloadTokenExpiresAt = clock.GetUtcNow() + PrivacyRequestRules.DownloadLinkLifetime;
        await db.SaveChangesAsync(cancellationToken);
        return new PrivacyDownloadLink($"/api/v1/settings/privacy-requests/{id:D}/export/download?token={Uri.EscapeDataString(token)}", request.DownloadTokenExpiresAt.Value, FileName(request));
    }

    /// <summary>
    /// Download pelo link (CA 10): login do Proprietário, o token do pedido da rota, não vencido e ainda não usado (usado, ele some). Grava
    /// <c>SensitiveRead</c> com o número do pedido. Token errado, vencido ou de outro pedido: 404, sem dizer qual.
    /// </summary>
    public async Task<Result<PrivacyExportFile>> DownloadAsync(Guid id, string? token, CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var request = await db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var now = clock.GetUtcNow();
        if (request is null || string.IsNullOrEmpty(token) || request.DownloadTokenHash is null || request.DownloadTokenExpiresAt is not { } expires || expires < now
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(request.DownloadTokenHash), System.Text.Encoding.ASCII.GetBytes(OpaqueToken.Hash(token)))
            || request.ExportAttachmentId is not { } attachmentId)
        {
            return Error.NotFound("privacy_download_link_invalid", "Link de download inválido ou vencido. Gere outro em \"Baixar exportação\".");
        }

        request.DownloadTokenHash = null;
        request.DownloadTokenExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);
        var content = await attachments.DownloadCheckedAsync(PrivacyRequest.OwnerType, request.Id, attachmentId, cancellationToken);
        if (content.IsFailure)
        {
            return content.Error!;
        }

        await audit.RecordReadAsync(nameof(PrivacyRequest), request.Id.ToString(), request.Number, cancellationToken);
        return new PrivacyExportFile(content.Value.Content, FileName(request), ZipContentType);
    }

    /// <summary>
    /// Histórico do pedido (CA 12): registro, conferência, resposta, exportação e download, com quem, quando, o quê e o IP. Os campos
    /// <c>[PersonalData]</c> já saem mascarados da auditoria (ADR 0018); só o Proprietário vê.
    /// </summary>
    public async Task<Result<IReadOnlyList<PrivacyHistoryEntry>>> HistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        if (!await db.PrivacyRequests.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var key = id.ToString();
        var entries = await db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(PrivacyRequest) && a.EntityId == key)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(200).ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in entries.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, cancellationToken);
        }

        return entries.Select(e => new PrivacyHistoryEntry(e.Id, e.At, e.Action, e.UserId is { } u ? names.GetValueOrDefault(u) ?? "Usuário" : "Sistema", e.Ip, e.Changes)).ToList();
    }

    public async Task<Result<PrivacySettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var row = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == ResponseDaysKey, cancellationToken);
        return new PrivacySettingsDto(ParseDays(row?.Value), row?.Id);
    }

    /// <summary>Prazo de resposta (CA 1): dias corridos, de 1 a 30 (o legal pede 15, editável pelo jurídico), auditado.</summary>
    public async Task<Result<PrivacySettingsDto>> SaveSettingsAsync(PrivacySettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        if (input.ResponseDays is < PrivacyRequestRules.MinResponseDays or > PrivacyRequestRules.MaxResponseDays)
        {
            const string message = "Informe de 1 a 30 dias corridos.";
            return Error.Validation("validation", message, new Dictionary<string, string[]> { ["responseDays"] = [message] });
        }

        var row = await db.TenantSettings.FirstOrDefaultAsync(s => s.Key == ResponseDaysKey, cancellationToken);
        if (row is null)
        {
            row = new TenantSetting { Key = ResponseDaysKey };
            db.TenantSettings.Add(row);
        }

        row.Value = input.ResponseDays.ToString(CultureInfo.InvariantCulture);
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new PrivacySettingsDto(input.ResponseDays, row.Id);
    }

    public async Task<int> ResponseDaysAsync(CancellationToken cancellationToken = default) =>
        ParseDays(await db.TenantSettings.AsNoTracking().Where(s => s.Key == ResponseDaysKey).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken));

    /// <summary>Titular identificado pelo módulo dono do tipo (ADR 0037 §1).</summary>
    public async Task<PersonalDataSubject?> ResolveAsync(PersonalDataSubjectKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        foreach (var provider in providers)
        {
            if (await provider.ResolveAsync(kind, id, cancellationToken) is { } subject)
            {
                return subject;
            }
        }

        return null;
    }

    public static string FileName(PrivacyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"dados-do-titular-{request.Number}.zip";
    }

    private async Task<(PrivacyRequest? Request, Error? Error)> OpenAsync(Guid id, uint version, CancellationToken ct)
    {
        if (await OwnerOnlyAsync(ct) is { } denied)
        {
            return (null, denied);
        }

        var request = await db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return (null, NotFound());
        }

        if (request.Status != PrivacyRequestStatus.Open)
        {
            return (null, Error.Conflict("privacy_request_closed", "O pedido já foi concluído ou recusado."));
        }

        db.Entry(request).Property(r => r.Version).OriginalValue = version;
        return (request, null);
    }

    private async Task<PrivacyRequestDetail> DetailAsync(PrivacyRequest r, CancellationToken ct)
    {
        var verifier = r.IdentityVerifiedBy is { } by ? await users.DisplayNameAsync(by, ct) : null;
        var today = BusinessTime.Today(clock);
        return new PrivacyRequestDetail(r.Id, r.Number, r.SubjectType, r.SubjectId, r.SubjectName, r.SubjectContact, r.Kind, r.RequestedOn, r.Channel, r.Notes, r.DueOn,
            PrivacyRequestRules.DueChip(r.Status, r.DueOn, today), r.Status, PrivacyRequestRules.RequiresIdentity(r.Kind), r.IsIdentityVerified, r.IdentityVerifiedAt, verifier,
            r.IdentityMethod, r.IdentityCheckedOn, r.IdentityNotes, r.Response, r.RespondedOn, r.FileSent, r.RefusalReason, r.ClosedAt, r.CorrectionLink,
            r.Status == PrivacyRequestStatus.Open && PrivacyRequestRules.Exports(r.Kind) && r.IsIdentityVerified, r.ExportStatus, r.ExportedAt, r.ExportError,
            r.ClosedAt is { } closed && r.ExportAttachmentId is not null ? closed + PrivacyRequestRules.ExportRetention : null, r.CreatedAt, r.Version);
    }

    private static int ParseDays(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days is >= PrivacyRequestRules.MinResponseDays and <= PrivacyRequestRules.MaxResponseDays
            ? days
            : PrivacyRequestRules.DefaultResponseDays;

    private static Error NotFound() => Error.NotFound("privacy_request_not_found", "Pedido do titular não encontrado.");

    private static Error IdentityNotVerified() => Error.Conflict(PrivacyRequestRules.IdentityNotVerifiedCode, PrivacyRequestRules.IdentityNotVerifiedMessage);
}
