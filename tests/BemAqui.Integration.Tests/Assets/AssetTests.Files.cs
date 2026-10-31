using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Assets;

public partial class AssetTests
{
    private const string GpsMarker = "GPS -23.5505,-46.6333";
    private const string XmpMarker = "exif:GPSLatitude";

    /// <summary>
    /// JPEG mínimo de câmera: EXIF com orientação 6, descrição com as coordenadas e o ponteiro do GPS (latitude "S"), XMP com a latitude,
    /// comentário, quadro (SOF0) e varredura (SOS).
    /// </summary>
    internal static byte[] JpegWithGps()
    {
        var tiff = new List<byte>();
        tiff.AddRange("MM"u8.ToArray());
        tiff.AddRange([0x00, 0x2A, 0x00, 0x00, 0x00, 0x08]);
        var description = Encoding.ASCII.GetBytes(GpsMarker + "\0");
        const int descriptionAt = 8 + 2 + (3 * 12) + 4;
        var gpsAt = descriptionAt + description.Length;
        tiff.AddRange([0x00, 0x03]);
        tiff.AddRange([0x01, 0x0E, 0x00, 0x02, 0x00, 0x00, 0x00, (byte)description.Length, 0x00, 0x00, 0x00, descriptionAt]);
        tiff.AddRange([0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00]);
        tiff.AddRange([0x88, 0x25, 0x00, 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)gpsAt]);
        tiff.AddRange([0x00, 0x00, 0x00, 0x00]);
        tiff.AddRange(description);
        tiff.AddRange([0x00, 0x01, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, (byte)'S', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        var exif = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        var xmp = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta " + XmpMarker + "=\"23,33.03S\"/>");
        var comment = Encoding.ASCII.GetBytes("tirada na fabrica");
        var jpeg = new List<byte> { 0xFF, 0xD8 };
        jpeg.AddRange(Segment(0xE1, exif));
        jpeg.AddRange(Segment(0xE1, xmp));
        jpeg.AddRange(Segment(0xFE, comment));
        jpeg.AddRange(Segment(0xC0, [0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00]));
        jpeg.AddRange(Segment(0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]));
        jpeg.AddRange([0x12, 0x34, 0x56, 0xFF, 0xD9]);
        return jpeg.ToArray();
    }

    private static byte[] Segment(byte marker, byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    private static bool Contains(byte[] data, string text) => Encoding.Latin1.GetString(data).Contains(text, StringComparison.Ordinal);

    /// <summary>
    /// CA 8 [Segurança]: a foto é regravada sem EXIF nem GPS (a orientação fica), aparece na ficha e na lista só limpa; documentos PDF;
    /// a rota genérica não grava; anexo de outro bem dá 404 (autorizado pelo registro).
    /// </summary>
    [Fact]
    public async Task Foto_sai_sem_EXIF_nem_GPS_e_anexo_e_autorizado_pelo_registro()
    {
        var supervisor = await SupervisorAsync();
        var asset = await CreateAsync(supervisor, Input(Unique("Centro de usinagem")));
        var other = await CreateAsync(supervisor, Input(Unique("Outro bem")));
        var (id, otherId) = (asset.GetProperty("id").GetGuid(), other.GetProperty("id").GetGuid());
        var original = JpegWithGps();
        Contains(original, GpsMarker).Should().BeTrue();

        var photo = await CreatedAsync(await supervisor.PostAsync($"{Base}/{id}/photo", File("camera.jpg", original, "image/jpeg")));
        var photoId = photo.GetProperty("id").GetGuid();
        var download = await supervisor.GetAsync($"{Base}/{id}/attachments/{photoId}/content");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await download.Content.ReadAsByteArrayAsync();
        Contains(stored, GpsMarker).Should().BeFalse("a descrição com as coordenadas sai");
        Contains(stored, XmpMarker).Should().BeFalse("o XMP sai");
        Contains(stored, "tirada na fabrica").Should().BeFalse("o comentário sai");
        stored.AsSpan().IndexOf(new byte[] { 0x88, 0x25 }).Should().Be(-1, "o ponteiro do GPS sai");
        stored.AsSpan().IndexOf(new byte[] { 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06 }).Should().BePositive("a orientação da câmera fica");
        stored.AsSpan()[^5..].ToArray().Should().Equal(original[^5..], "a imagem comprimida não muda");

        (await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}/{id}")).GetProperty("photoId").GetGuid().Should().Be(photoId);
        var list = await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}?q={asset.GetProperty("tag").GetString()}");
        list.GetProperty("items")[0].GetProperty("photoId").GetGuid().Should().Be(photoId, "miniatura na lista");

        var pdf = await CreatedAsync(await supervisor.PostAsync($"{Base}/{id}/documents", File("manual.pdf", "%PDF-1.4\n%%EOF"u8.ToArray(), "application/pdf")));
        var files = await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}/{id}/attachments");
        files.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().Equal(pdf.GetProperty("id").GetGuid());
        files.GetProperty("photoId").GetGuid().Should().Be(photoId);

        (await supervisor.GetAsync($"{Base}/{otherId}/attachments/{photoId}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound, "anexo de outro bem (IDOR)");
        (await supervisor.PostAsync($"/api/v1/attachments/AssetPhoto/{id}", File("camera.jpg", original, "image/jpeg"))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "a rota genérica não grava foto do bem");
        (await supervisor.PostAsync($"{Base}/{id}/photo", File("manual.pdf", "%PDF-1.4\n%%EOF"u8.ToArray(), "application/pdf"))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "a foto aceita só PNG, JPG e WebP");
        (await (await ActorAsync("warehouse")).PostAsync($"{Base}/{id}/photo", File("camera.jpg", original, "image/jpeg"))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "gravar exige assets.edit");
        (await (await ActorAsync("warehouse")).GetAsync($"{Base}/{id}/attachments/{photoId}/content")).StatusCode.Should().Be(HttpStatusCode.OK, "ler com assets.view");
    }
}
