using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Mudanças de tabela legal e de parâmetro do RH desde o último fechamento de folha (FLH-01 CA 12, revisão §4.3): lidas da auditoria
/// (não podem ser omitidas), com o quê, antes → depois, quem e quando, para o diálogo de fechamento pedir ciência. Sem nenhum fechamento
/// ainda, só as feitas por usuário (o seed já aparece como "A confirmar com a contadora").
/// </summary>
public sealed class PayrollChangeNotices(IHrDb db, IAuditReader audit, IUserDirectory users)
{
    public const string NotAcknowledgedCode = "payroll_changes_not_acknowledged";
    public const string NotAcknowledgedMessage = "Leia as mudanças de tabela e de parâmetro desde o último fechamento e marque a ciência antes de fechar.";
    private const int MaxText = 200;

    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal) { "version", "editedAt", "updatedAt", "createdAt", "id", "schemaVersion" };

    public async Task<IReadOnlyList<PayrollChangeNotice>> SinceLastClosingAsync(CancellationToken cancellationToken = default)
    {
        var last = await db.PayrollRuns.AsNoTracking().Where(r => r.ClosedAt != null).MaxAsync(r => r.ClosedAt, cancellationToken);
        var changes = await audit.ChangesSinceAsync([nameof(PayrollTaxTable), nameof(HrParameter)], last, usersOnly: last is null, 200, cancellationToken);
        if (changes.Count == 0)
        {
            return [];
        }

        var ids = changes.Select(c => Guid.TryParse(c.EntityId, out var id) ? id : Guid.Empty).Distinct().ToList();
        var tables = await db.PayrollTaxTables.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var parameters = await db.HrParameters.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Key, cancellationToken);
        var names = new Dictionary<Guid, string?>();
        var notices = new List<PayrollChangeNotice>();
        foreach (var change in changes)
        {
            var id = Guid.TryParse(change.EntityId, out var parsed) ? parsed : Guid.Empty;
            var fields = Fields(change.Changes);
            string what;
            if (change.Entity == nameof(PayrollTaxTable))
            {
                what = tables.TryGetValue(id, out var t) ? $"Tabela {PayrollTaxRules.KindLabel(t.Kind)} de {PayrollTaxRules.MonthLabel(t.ValidFrom)}" : "Tabela da folha";
            }
            else
            {
                what = parameters.TryGetValue(id, out var key) ? $"Parâmetro {key}" : "Parâmetro do RH";
            }

            string? by = null;
            if (change.UserId is { } user)
            {
                if (!names.TryGetValue(user, out by))
                {
                    names[user] = by = await users.DisplayNameAsync(user, cancellationToken);
                }
            }

            notices.Add(new PayrollChangeNotice(what, ActionLabel(change, fields), fields, by ?? change.Origin ?? "Sistema", change.At));
        }

        return notices;
    }

    private static string ActionLabel(AuditChange change, IReadOnlyList<PayrollChangeField> fields)
    {
        if (change.Entity != nameof(PayrollTaxTable))
        {
            return change.Action == AuditActions.Created ? "Criado" : "Alterado";
        }

        var status = fields.FirstOrDefault(f => f.Field == "status")?.After;
        return change.Action switch
        {
            AuditActions.Created when fields.Any(f => f.Field == "rectifiesId" && f.After is not null) => "Retificação criada",
            AuditActions.Created => "Criada",
            _ when status == nameof(PayrollTaxTableStatus.Active) => "Conferida",
            _ when status == nameof(PayrollTaxTableStatus.Superseded) => "Substituída pela retificação",
            _ => "Editada",
        };
    }

    private static List<PayrollChangeField> Fields(string json)
    {
        var fields = new List<PayrollChangeField>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return fields;
            }

            foreach (var property in doc.RootElement.EnumerateObject().Where(p => !Hidden.Contains(p.Name)))
            {
                var old = property.Value.TryGetProperty("old", out var o) ? Text(o) : null;
                var @new = property.Value.TryGetProperty("new", out var n) ? Text(n) : null;
                if (old is null && @new is null)
                {
                    continue;
                }

                fields.Add(new PayrollChangeField(property.Name, old, @new));
            }
        }
        catch (JsonException)
        {
            // Diff ilegível: a mudança continua listada, sem os campos.
        }

        return fields;
    }

    private static string? Text(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText(),
        };
        return text is { Length: > MaxText } ? text[..MaxText] + "…" : text;
    }
}
