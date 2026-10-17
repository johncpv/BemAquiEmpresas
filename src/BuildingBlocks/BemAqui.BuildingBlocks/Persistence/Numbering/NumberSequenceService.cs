using BemAqui.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence.Numbering;

/// <summary>Incremento atômico com <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>: a linha fica bloqueada até o commit.</summary>
public sealed class NumberSequenceService(BemAquiDbContext db, TimeProvider clock) : INumberSequence
{
    public async Task<string> NextAsync(string docType, CancellationToken cancellationToken = default)
    {
        if (!NumberFormats.Defaults.TryGetValue(docType, out var format))
        {
            throw new ArgumentException($"Tipo de documento sem numeração definida: '{docType}'.", nameof(docType));
        }

        var year = format.Yearly ? BusinessTime.Today(clock).Year : 0;
        var values = await db.Database
            .SqlQuery<long>($"""
                INSERT INTO shared.number_sequences (id, doc_type, year, last_value)
                VALUES ({Guid.CreateVersion7()}, {docType}, {year}, 1)
                ON CONFLICT (doc_type, year) DO UPDATE SET last_value = shared.number_sequences.last_value + 1
                RETURNING last_value AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return format.Format(year, values.Single());
    }
}
