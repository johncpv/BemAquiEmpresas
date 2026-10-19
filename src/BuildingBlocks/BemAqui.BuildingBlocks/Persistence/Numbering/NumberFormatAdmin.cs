using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence.Numbering;

/// <summary>
/// Numeração editável (CAD-06 CA 9 a 14, ADR 0016). O prefixo e os dígitos mudam a qualquer momento e valem do próximo
/// número em diante; o reinício anual trava depois do primeiro número; o "próximo número" só sobe, com
/// <c>ON CONFLICT ... GREATEST</c>, que também funciona num ano ainda sem nenhum número.
/// </summary>
public sealed partial class NumberFormatAdmin(BemAquiDbContext db, TimeProvider clock) : INumberFormatAdmin
{
    [GeneratedRegex("^[A-Z0-9]{1,6}$")]
    private static partial Regex PrefixPattern();

    /// <summary>Formato que vale para o tipo: o do tenant, se mudou, ou o padrão.</summary>
    public static async Task<NumberFormat?> EffectiveAsync(BemAquiDbContext db, string docType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var custom = await db.NumberFormatOverrides.AsNoTracking().FirstOrDefaultAsync(f => f.DocType == docType, cancellationToken);
        return custom?.ToFormat() ?? NumberFormats.Defaults.GetValueOrDefault(docType);
    }

    public async Task<IReadOnlyList<NumberFormatRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var customs = await db.NumberFormatOverrides.AsNoTracking().ToDictionaryAsync(f => f.DocType, cancellationToken);
        var sequences = await db.NumberSequences.AsNoTracking().ToListAsync(cancellationToken);
        var year = BusinessTime.Today(clock).Year;
        return NumberFormats.Defaults.Select(d =>
        {
            var format = customs.TryGetValue(d.Key, out var c) ? c.ToFormat() : d.Value;
            return Row(d.Key, format, sequences.Where(s => s.DocType == d.Key).ToList(), year, customs.ContainsKey(d.Key));
        }).ToList();
    }

    public async Task<Result<NumberFormatRow>> SaveAsync(string docType, NumberFormatInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!NumberFormats.Defaults.ContainsKey(docType))
        {
            return Error.NotFound(Error.Codes.NotFound, "Tipo de documento sem numeração.");
        }

        var prefix = (input.Prefix ?? string.Empty).Trim().ToUpperInvariant();
        if (!PrefixPattern().IsMatch(prefix))
        {
            return Invalid("prefix", "O prefixo tem de 1 a 6 letras ou números, sem espaço nem acento.");
        }

        if (input.Digits is < 3 or > 8)
        {
            return Invalid("digits", "Use de 3 a 8 dígitos.");
        }

        var other = (await ListAsync(cancellationToken)).FirstOrDefault(r => r.DocType != docType && r.Prefix == prefix);
        if (other is not null)
        {
            return Invalid("prefix", $"Este prefixo já é usado em {other.Label}.");
        }

        var sequences = await db.NumberSequences.AsNoTracking().Where(s => s.DocType == docType).ToListAsync(cancellationToken);
        var current = await EffectiveAsync(db, docType, cancellationToken) ?? NumberFormats.Defaults[docType];
        if (input.Yearly != current.Yearly && sequences.Any(s => s.LastValue > 0))
        {
            return Error.Conflict("numbering.yearly_locked", "Não é possível mudar o reinício anual depois do primeiro número emitido.");
        }

        var year = input.Yearly ? BusinessTime.Today(clock).Year : 0;
        var last = sequences.FirstOrDefault(s => s.Year == year)?.LastValue ?? 0;
        if (input.NextNumber is { } next)
        {
            if (next <= last)
            {
                return Error.Conflict("numbering.next_too_low", $"O próximo número precisa ser maior que o último emitido ({last}).");
            }

            if (next.ToString(System.Globalization.CultureInfo.InvariantCulture).Length > input.Digits)
            {
                return Invalid("nextNumber", $"O próximo número não cabe em {input.Digits} dígitos.");
            }
        }

        await SaveRowAsync(docType, prefix, input, year, cancellationToken);
        return (await ListAsync(cancellationToken)).Single(r => r.DocType == docType);
    }

    private async Task SaveRowAsync(string docType, string prefix, NumberFormatInput input, int year, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var row = await db.NumberFormatOverrides.FirstOrDefaultAsync(f => f.DocType == docType, ct);
        if (row is null)
        {
            row = new NumberFormatOverride { DocType = docType };
            db.NumberFormatOverrides.Add(row);
        }

        row.Prefix = prefix;
        row.Yearly = input.Yearly;
        row.Digits = input.Digits;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        if (input.NextNumber is { } wanted)
        {
            // Só sobe: se alguém numerou no meio, vale o maior (CA 11 e 14).
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO shared.number_sequences (id, doc_type, year, last_value)
                VALUES ({Guid.CreateVersion7()}, {docType}, {year}, {wanted - 1})
                ON CONFLICT (doc_type, year) DO UPDATE SET last_value = GREATEST(shared.number_sequences.last_value, EXCLUDED.last_value)
                """, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
    }

    private static NumberFormatRow Row(string docType, NumberFormat format, IReadOnlyList<NumberSequence> sequences, int year, bool customized)
    {
        var period = format.Yearly ? year : 0;
        var last = sequences.FirstOrDefault(s => s.Year == period)?.LastValue ?? 0;
        return new NumberFormatRow(docType, NumberFormats.Labels.GetValueOrDefault(docType, docType), format.Prefix, format.Yearly, format.Digits, last,
            sequences.Any(s => s.LastValue > 0), format.Format(period, last + 1), customized);
    }

    private static Error Invalid(string field, string message) => Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
