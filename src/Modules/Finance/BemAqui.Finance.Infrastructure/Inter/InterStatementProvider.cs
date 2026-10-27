using System.Globalization;
using System.Text.Json;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Application.Statements;
using BemAqui.Finance.Domain;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Infrastructure.Inter;

/// <summary>
/// Extrato pela API do Inter (BNK-03a CA 1 e 3) sobre o <see cref="IInterApiClient"/> (mTLS, token em cache, URL base fixa): o extrato
/// enriquecido (<c>banking/v2/extrato/completo</c>), paginado, e o saldo do dia (<c>banking/v2/saldo?dataSaldo=</c>). Lançamento sem
/// id, data ou valor é contado como recusado, nunca gravado. O corpo da resposta não vai para o log (o cliente só loga a situação).
/// </summary>
public sealed class InterStatementProvider(IInterApiClient inter, IOptions<BankStatementOptions> options) : IBankStatementProvider
{
    public const string StatementPath = "banking/v2/extrato/completo";
    private const int MaxPages = 200;

    public BankProvider Provider => BankProvider.Inter;

    public async Task<InterResult<ProviderStatement>> GetAsync(InterCredentials credentials, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var lines = new List<ProviderStatementLine>();
        var rejected = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var path = $"{StatementPath}?dataInicio={from:yyyy-MM-dd}&dataFim={to:yyyy-MM-dd}&pagina={page}&tamanhoPagina={options.Value.InterPageSize}";
            var response = await inter.SendAsync(credentials, new InterRequest("extrato", HttpMethod.Get, path), cancellationToken);
            if (!response.Ok)
            {
                return InterResult<ProviderStatement>.Fail(response.Failure!);
            }

            if (response.Value!.Status is < 200 or >= 300)
            {
                return InterResult<ProviderStatement>.Fail(InterApiClient.FailureFor(response.Value.Status, token: false));
            }

            bool last;
            try
            {
                using var document = JsonDocument.Parse(response.Value.Body);
                var root = document.RootElement;
                if (root.TryGetProperty("transacoes", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (Line(item) is { } line)
                        {
                            lines.Add(line);
                        }
                        else
                        {
                            rejected++;
                        }
                    }
                }

                var totalPages = root.TryGetProperty("totalPaginas", out var tp) && tp.TryGetInt32(out var t) ? t : 1;
                last = (root.TryGetProperty("ultimaPagina", out var lp) && lp.ValueKind == JsonValueKind.True) || page + 1 >= totalPages;
            }
            catch (JsonException)
            {
                return InterResult<ProviderStatement>.Fail(Unexpected());
            }

            if (last)
            {
                break;
            }
        }

        return InterResult<ProviderStatement>.Success(new ProviderStatement(lines, rejected));
    }

    public async Task<InterResult<decimal>> GetBalanceAsync(InterCredentials credentials, DateOnly date, CancellationToken cancellationToken = default)
    {
        var response = await inter.SendAsync(credentials, new InterRequest("saldo", HttpMethod.Get, $"{InterEnvironments.BalancePath}?dataSaldo={date:yyyy-MM-dd}"), cancellationToken);
        if (!response.Ok)
        {
            return InterResult<decimal>.Fail(response.Failure!);
        }

        if (response.Value!.Status is < 200 or >= 300)
        {
            return InterResult<decimal>.Fail(InterApiClient.FailureFor(response.Value.Status, token: false));
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value.Body);
            var root = document.RootElement;
            if (Number(root, "disponivel") is not { } available)
            {
                return InterResult<decimal>.Fail(Unexpected());
            }

            // Saldo contábil: o disponível mais os bloqueados (continuam na conta); o limite de crédito fica de fora.
            var blocked = (Number(root, "bloqueadoCheque") ?? 0) + (Number(root, "bloqueadoJudicialmente") ?? 0) + (Number(root, "bloqueadoAdministrativo") ?? 0);
            return InterResult<decimal>.Success(decimal.Round(available + blocked, 2));
        }
        catch (JsonException)
        {
            return InterResult<decimal>.Fail(Unexpected());
        }
    }

    /// <summary>Um item de <c>transacoes</c>: <c>idTransacao</c>, <c>dataTransacao</c> (ou <c>dataInclusao</c>), <c>tipoOperacao</c> C/D, <c>valor</c>.</summary>
    internal static ProviderStatementLine? Line(JsonElement item)
    {
        var id = Text(item, "idTransacao");
        var date = Date(Text(item, "dataTransacao")) ?? Date(Text(item, "dataEntrada")) ?? Date(Text(item, "dataInclusao"));
        var value = Number(item, "valor");
        var operation = Text(item, "tipoOperacao")?.ToUpperInvariant();
        if (id is not { Length: > 0 and <= 100 } || date is null || value is null || operation is not ("C" or "D"))
        {
            return null;
        }

        var amount = decimal.Round(Math.Abs(value.Value), 2) * (operation == "D" ? -1 : 1);
        var title = Text(item, "titulo");
        var description = Text(item, "descricao") ?? title ?? string.Empty;
        string? name = null;
        string? document = null;
        if (item.TryGetProperty("detalhes", out var details) && details.ValueKind == JsonValueKind.Object)
        {
            // Crédito: quem pagou; débito: quem recebeu.
            var who = operation == "C" ? new[] { "nomePagador", "nomeRemetente", "nomeEmpresa" } : ["nomeRecebedor", "nomeFavorecido", "nomeBeneficiario", "nomeEmpresa"];
            var doc = operation == "C" ? new[] { "cpfCnpjPagador", "cpfCnpjRemetente" } : ["cpfCnpjRecebedor", "cpfCnpjFavorecido", "cpfCnpjBeneficiario"];
            name = who.Select(k => Text(details, k)).FirstOrDefault(v => v is { Length: > 0 });
            document = doc.Select(k => Text(details, k)).FirstOrDefault(v => v is { Length: > 0 });
        }

        return new ProviderStatementLine(id, date.Value, amount, description, StatementKinds.FromInter(Text(item, "tipoTransacao"), title), name, document);
    }

    private static InterFailure Unexpected() =>
        new(InterFailureKind.BankError, "O Inter respondeu o extrato num formato inesperado.", "Tente de novo em alguns minutos; se continuar, fale com o suporte.");

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static decimal? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateOnly? Date(string? text) =>
        text is { Length: >= 10 } && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
