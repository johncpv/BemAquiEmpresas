using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Regras comuns ao plano de contas e aos centros de custo (CAD-05a CA 5, 6, 8, 11 e 16): máscara <c>9.9.99</c>, o filho começa
/// com o código do pai, código único entre os não excluídos e sem ciclo. Quem tem filhos não troca de código nem de pai.
/// </summary>
public static class TreeValidation
{
    /// <summary>Palavras da mensagem: "a conta" / "dela mesma" ou "o centro de custo" / "dele mesmo".</summary>
    public sealed record Words(string The, string Self);

    public static readonly Words Account = new("a conta", "dela mesma");
    public static readonly Words CostCenter = new("o centro de custo", "dele mesmo");

    public static async Task<(Error? Error, T? Parent)> ValidateAsync<T>(IQueryable<T> set, T? current, string? code, Guid? parentId, Words words, CancellationToken ct)
        where T : class, ITreeNode
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(words);
        if (!TreeCode.IsValid(code))
        {
            return (Invalid("code", TreeCode.MaskMessage), null);
        }

        T? parent = null;
        if (parentId is { } pid)
        {
            parent = await set.FirstOrDefaultAsync(n => n.Id == pid, ct);
            if (parent is null)
            {
                return (Invalid("parentId", $"Escolha {words.The} pai da lista."), null);
            }

            if (current is not null)
            {
                // Sobe pelos pais: se chegar nela mesma, seria ciclo (CA 11).
                var parents = await set.AsNoTracking().Select(n => new { n.Id, n.ParentId }).ToDictionaryAsync(n => n.Id, n => n.ParentId, ct);
                for (Guid? at = pid; at is { } id; at = parents.GetValueOrDefault(id))
                {
                    if (id == current.Id)
                    {
                        return (Invalid("parentId", char.ToUpperInvariant(words.The[0]) + words.The[1..] + " não pode ficar dentro " + words.Self + "."), null);
                    }
                }
            }
        }

        var expected = TreeCode.ParentCode(code!);
        if (parent is null && expected is not null)
        {
            return (Invalid("parentId", $"Escolha {words.The} pai {expected} ou use um código de um nível só."), null);
        }

        if (parent is not null && expected != parent.Code)
        {
            return (Invalid("code", $"O código precisa começar com {parent.Code}."), null);
        }

        if (current is not null && (current.Code != code || current.ParentId != parentId) && await set.AnyAsync(n => n.ParentId == current.Id, ct))
        {
            return (Invalid("code", "Mova ou troque o código das filhas antes."), null);
        }

        if (await set.AnyAsync(n => n.Code == code && (current == null || n.Id != current.Id), ct))
        {
            return (Error.Conflict("tree_code_taken", $"Já existe {words.The} {code}."), null);
        }

        return (null, parent);
    }

    public static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
