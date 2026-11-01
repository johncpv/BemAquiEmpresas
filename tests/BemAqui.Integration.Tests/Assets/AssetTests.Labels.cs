using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Assets.Application;
using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>
/// PAT-01b: etiqueta com QR (só a URL curta), tamanho pelo parâmetro, lote em segundo plano, reimpressão igual, histórico e a leitura pela
/// etiqueta (assets.view antes da busca, 404 uniforme). A sessão por PIN fica fora pela varredura do ShopfloorPinTests (CA 10).
/// </summary>
public partial class AssetTests
{
    private const string Labels = "/api/v1/assets/label-batches";
    private const string Parameters = "/api/v1/assets/parameters";
    private const float PointsPerMm = 72f / 25.4f;

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().Select(p => p.Text));
    }

    private static (int Pages, double Width, double Height) PdfPages(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var first = document.GetPage(1);
        return (document.NumberOfPages, first.Width, first.Height);
    }

    private static async Task<byte[]> PdfAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<List<AuditEntry>> AuditAsync(Guid assetId)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking()
            .Where(a => a.Entity == "Asset" && a.EntityId == assetId.ToString()).OrderBy(a => a.At).ToListAsync();
    }

    private static string? Format(AuditEntry entry) => JsonDocument.Parse(entry.Changes).RootElement.GetProperty("format").GetProperty("new").GetString();

    /// <summary>
    /// CA 1 e 2 [Segurança]: o PDF traz o QR, a etiqueta, o nome e o nome do tenant, sem valor, série nem responsável; o QR decodificado é só
    /// <c>https://{host primário}/b/{etiqueta}</c>, sem id interno nem token.
    /// </summary>
    [Fact]
    public async Task Etiqueta_leva_so_a_URL_curta_no_QR_e_nenhum_dado_do_bem()
    {
        var supervisor = await SupervisorAsync();
        var responsible = await EmployeeAsync("Joana Responsável");
        var asset = await CreateAsync(supervisor, Input(Unique("Prensa hidráulica"), serial: "SN-SIGILO-77", value: 98_765.43m, responsible: responsible));
        var (id, tag, name) = (asset.GetProperty("id").GetGuid(), asset.GetProperty("tag").GetString()!, asset.GetProperty("name").GetString()!);

        var response = await supervisor.GetAsync($"{Base}/{id}/label");
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be($"etiqueta-{tag}.pdf");
        var pdf = await PdfAsync(response);
        var text = PdfText(pdf);
        text.Should().Contain(tag).And.Contain(name.Split(' ')[0]).And.Contain("Empresa A Ltda");
        text.Should().NotContain("SN-SIGILO-77").And.NotContain("98.765,43").And.NotContain("98765").And.NotContain("Joana");
        var (pages, width, height) = PdfPages(pdf);
        pages.Should().Be(1);
        width.Should().BeApproximately(50 * PointsPerMm, 0.5, "seed 50 × 25 mm (P-13)");
        height.Should().BeApproximately(25 * PointsPerMm, 0.5);

        await using var scope = await ScopeAsync();
        _ = scope.ServiceProvider.GetRequiredService<IPdfRenderer>();
        var document = (await scope.ServiceProvider.GetRequiredService<AssetLabelService>().LabelDocumentAsync(id))!;
        var decoded = QrDecoding.Decode(document);
        decoded.Should().Be($"https://{Host}/b/{tag}");
        decoded.Should().NotContain(id.ToString()).And.NotContain(id.ToString("N"));
    }

    /// <summary>CA 1 e 11: o tamanho vem de assets.label_size; gravar exige settings.edit, valida a faixa e vai para o histórico.</summary>
    [Fact]
    public async Task Tamanho_da_etiqueta_e_parametro_com_settings_edit_e_historico()
    {
        var supervisor = await SupervisorAsync();
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var asset = await CreateAsync(supervisor, Input(Unique("Furadeira de bancada"), "Tool"));
        var id = asset.GetProperty("id").GetGuid();

        var current = await supervisor.GetFromJsonAsync<JsonElement>(Parameters);
        current.GetProperty("labelWidthMm").GetInt32().Should().Be(50);
        current.GetProperty("labelHeightMm").GetInt32().Should().Be(25);

        var refused = await supervisor.PutAsJsonAsync(Parameters, new { labelWidthMm = 60, labelHeightMm = 30 });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem settings.edit");
        (await owner.PutAsJsonAsync(Parameters, new { labelWidthMm = 10, labelHeightMm = 30 })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        try
        {
            var saved = await JsonAsync(await owner.PutAsJsonAsync(Parameters, new { labelWidthMm = 60, labelHeightMm = 30 }));
            saved.GetProperty("labelWidthMm").GetInt32().Should().Be(60);
            var (_, width, height) = PdfPages(await PdfAsync(await supervisor.GetAsync($"{Base}/{id}/label")));
            width.Should().BeApproximately(60 * PointsPerMm, 0.5);
            height.Should().BeApproximately(30 * PointsPerMm, 0.5);

            await using var scope = await ScopeAsync();
            var parameterId = saved.GetProperty("labelSizeId").GetGuid().ToString();
            var history = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking()
                .Where(a => a.Entity == "AssetParameter" && a.EntityId == parameterId).ToListAsync();
            history.Should().NotBeEmpty();
            history.Should().Contain(h => h.Changes.Contains("60x30", StringComparison.Ordinal));
        }
        finally
        {
            (await owner.PutAsJsonAsync(Parameters, new { labelWidthMm = 50, labelHeightMm = 25 })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// CA 3 e 12: até 50, o PDF sai na hora (uma por página ou a grade A4); acima, o job leva só o tenantId e os ids e avisa no sino; o PDF é
    /// só de quem pediu; acima de 500, recusa; a impressão do lote vai para o histórico de cada bem.
    /// </summary>
    [Fact]
    public async Task Lote_ate_50_na_hora_e_acima_em_segundo_plano_com_tenantId()
    {
        var supervisor = await SupervisorAsync();
        var prefix = "Lote " + Guid.NewGuid().ToString("N")[..6];
        var ids = new List<Guid>();
        await using (var scope = await ScopeAsync())
        {
            var service = scope.ServiceProvider.GetRequiredService<AssetService>();
            for (var i = 0; i < 52; i++)
            {
                ids.Add((await service.CreateAsync(new AssetInput($"{prefix} chave {i:00}", AssetType.Tool))).Value.Id);
            }
        }

        var small = await JsonAsync(await supervisor.PostAsJsonAsync(Labels, new { assetIds = ids.Take(3), layout = "Label" }));
        small.GetProperty("queued").GetBoolean().Should().BeFalse();
        small.GetProperty("count").GetInt32().Should().Be(3);
        var smallPdf = await PdfAsync(await supervisor.GetAsync($"{Labels}/{small.GetProperty("attachmentId").GetGuid()}"));
        PdfPages(smallPdf).Pages.Should().Be(3, "uma etiqueta por página");
        (await (await ActorAsync("assets:view")).GetAsync($"{Labels}/{small.GetProperty("attachmentId").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "só quem pediu");

        var sheet = await JsonAsync(await supervisor.PostAsJsonAsync(Labels, new { assetIds = ids.Take(30), layout = "A4Sheet" }));
        var (sheetPages, sheetWidth, _) = PdfPages(await PdfAsync(await supervisor.GetAsync($"{Labels}/{sheet.GetProperty("attachmentId").GetGuid()}")));
        sheetPages.Should().Be(1, "30 etiquetas de 50 × 25 cabem numa folha (3 × 10)");
        sheetWidth.Should().BeApproximately(210 * PointsPerMm, 0.5);

        var before = _jobs.Created.Count;
        var queued = await JsonAsync(await supervisor.PostAsJsonAsync(Labels, new { allFromFilter = true, filter = new { q = prefix }, layout = "Label" }));
        queued.GetProperty("queued").GetBoolean().Should().BeTrue();
        queued.GetProperty("count").GetInt32().Should().Be(52, "todos do filtro");
        var job = _jobs.Created.Skip(before).Single(j => j.Job.Type == typeof(AssetLabelBatchJob)).Job;
        job.Args[0].Should().Be(tenancy.TenantA.Id, "job com tenantId (§4.3)");
        var args = (AssetLabelBatchArgs)job.Args[1];
        args.AssetIds.Should().BeEquivalentTo(ids, "só os ids nos argumentos");

        var instance = ActivatorUtilities.CreateInstance<AssetLabelBatchJob>(Factory.Services);
        await instance.ExecuteAsync(tenancy.TenantA.Id, args, CancellationToken.None);
        await using (var scope = await ScopeAsync())
        {
            var notification = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Notifications.AsNoTracking()
                .Where(n => n.UserId == args.RequestedBy && n.Type == AssetLabelService.NotificationType).OrderByDescending(n => n.CreatedAt).FirstAsync();
            notification.Title.Should().Contain("Etiquetas prontas");
            var lote = Guid.Parse(notification.Link[(notification.Link.IndexOf("lote=", StringComparison.Ordinal) + 5)..]);
            PdfPages(await PdfAsync(await supervisor.GetAsync($"{Labels}/{lote}"))).Pages.Should().Be(52);
        }

        var tooMany = await supervisor.PostAsJsonAsync(Labels, new { assetIds = Enumerable.Range(0, 501).Select(_ => Guid.CreateVersion7()), layout = "Label" });
        tooMany.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await supervisor.PostAsJsonAsync(Labels, new { assetIds = Array.Empty<Guid>(), layout = "Label" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var history = await AuditAsync(ids[0]);
        history.Count(h => h.Action == AuditActions.Exported && Format(h) == AssetLabelService.PrintFormat).Should().Be(3, "lote pequeno, folha A4 e todos do filtro");
    }

    /// <summary>CA 4, 11 e 12: a reimpressão (assets.edit) sai igual à original; impressão e reimpressão ficam no histórico com quem; a leitura não.</summary>
    [Fact]
    public async Task Reimpressao_sai_igual_e_fica_no_historico_e_a_leitura_nao()
    {
        var supervisor = await SupervisorAsync();
        var viewer = await ActorAsync("assets:view");
        var asset = await CreateAsync(supervisor, Input(Unique("Esmerilhadeira")));
        var (id, tag) = (asset.GetProperty("id").GetGuid(), asset.GetProperty("tag").GetString()!);

        var original = await PdfAsync(await viewer.GetAsync($"{Base}/{id}/label"));
        (await viewer.GetAsync($"{Base}/{id}/label/reprint")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "reimprimir exige assets.edit");
        var reprint = await PdfAsync(await supervisor.GetAsync($"{Base}/{id}/label/reprint"));
        PdfText(reprint).Should().Be(PdfText(original));
        reprint.Should().Equal(original, "a etiqueta nunca muda: o mesmo PDF");

        (await supervisor.GetAsync($"{Base}/by-tag/{tag}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await supervisor.GetAsync($"{Base}/{id}/card")).StatusCode.Should().Be(HttpStatusCode.OK);
        var history = (await AuditAsync(id)).Where(h => h.Action == AuditActions.Exported).ToList();
        history.Select(Format).Should().Equal(AssetLabelService.PrintFormat, AssetLabelService.ReprintFormat);
        history.Should().OnlyContain(h => h.UserId != null, "quem imprimiu");
        (await AuditAsync(id)).Should().NotContain(h => h.Action == AuditActions.SensitiveRead, "a leitura não é registrada");
        (await (await supervisor.GetAsync($"{Base}/{Guid.CreateVersion7()}/label")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("asset_not_found");
    }

    /// <summary>
    /// CA 5 e 7 [Segurança]: sem assets.view, 403 antes de buscar (igual para etiqueta que existe e que não existe); com a permissão, a ficha
    /// resumida sem valor nem série; inexistente, excluída, malformada ou de outro tenant: o mesmo 404 "Etiqueta não encontrada.".
    /// </summary>
    [Fact]
    public async Task Leitura_pela_etiqueta_confere_a_permissao_antes_e_responde_404_igual()
    {
        var supervisor = await SupervisorAsync();
        var asset = await CreateAsync(supervisor, Input(Unique("Torno mecânico"), serial: "SN-FICHA-1", value: 1_500m));
        var (id, tag) = (asset.GetProperty("id").GetGuid(), asset.GetProperty("tag").GetString()!);
        var gone = await CreateAsync(supervisor, Input(Unique("Bem excluído")));
        var goneTag = gone.GetProperty("tag").GetString()!;
        (await (await ActorAsync(Group.OwnerSeedKey)).DeleteAsync($"{Base}/{gone.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var outsider = await ActorAsync();
        var forbiddenExisting = await outsider.GetAsync($"{Base}/by-tag/{tag}");
        var forbiddenMissing = await outsider.GetAsync($"{Base}/by-tag/PAT-99999999");
        forbiddenExisting.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbiddenMissing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbiddenExisting)).Should().Be(Code(await JsonAsync(forbiddenMissing)), "não revela se a etiqueta existe");

        var card = await JsonAsync(await supervisor.GetAsync($"{Base}/by-tag/{tag.ToLowerInvariant()}"));
        card.GetProperty("id").GetGuid().Should().Be(id);
        card.GetProperty("tag").GetString().Should().Be(tag);
        card.TryGetProperty("acquisitionValue", out _).Should().BeFalse();
        card.TryGetProperty("serialNumber", out _).Should().BeFalse();
        card.GetProperty("nextMaintenanceDate").ValueKind.Should().Be(JsonValueKind.Null, "a manutenção chega com a PAT-03a");

        // Outro tenant: a etiqueta do tenant B não existe no banco do tenant A.
        var otherTenantTag = await OtherTenantTagAsync();
        var bodies = new List<string>();
        foreach (var missing in new[] { "PAT-99999999", goneTag, "x'or'1=1", otherTenantTag })
        {
            var response = await supervisor.GetAsync($"{Base}/by-tag/{Uri.EscapeDataString(missing)}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, missing);
            var problem = await JsonAsync(response);
            Code(problem).Should().Be(AssetService.TagNotFoundCode);
            problem.GetProperty("detail").GetString().Should().Be("Etiqueta não encontrada.");
            bodies.Add(problem.GetProperty("code").GetString() + "|" + problem.GetProperty("title").GetString() + "|" + problem.GetProperty("detail").GetString());
        }

        bodies.Distinct().Should().ContainSingle("a mesma resposta para os quatro casos");
    }

    /// <summary>Bem gravado só no banco do tenant B, com uma etiqueta que a sequência do tenant A não chega (8 dígitos).</summary>
    private async Task<string> OtherTenantTagAsync()
    {
        var info = (await Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantDirectory>().FindAsync(tenancy.TenantB.Id))!;
        await using var scope = await Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>().CreateScopeAsync(info);
        var db = scope.ServiceProvider.GetRequiredService<IAssetsDb>();
        var tag = "PAT-" + Random.Shared.Next(90_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var asset = new BemAqui.Assets.Domain.Asset { Name = Unique("Bem do tenant B"), Type = AssetType.Tool, CreatedAt = DateTimeOffset.UtcNow };
        asset.AssignTag(tag);
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return tag;
    }
}
