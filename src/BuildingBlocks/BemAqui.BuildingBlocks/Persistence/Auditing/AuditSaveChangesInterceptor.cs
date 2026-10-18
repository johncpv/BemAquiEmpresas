using System.Reflection;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// Antes de salvar: converte exclusão de <see cref="ISoftDeletable"/> em exclusão lógica e grava em
/// <c>audit.audit_log</c>, no mesmo SaveChanges (mesma transação), o diff das entidades <see cref="IAuditable"/>.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock, AuditOrigin origin) : SaveChangesInterceptor
{
    public const string Masked = "***";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var audit = new List<AuditEntry>();
        foreach (var entry in entries)
        {
            var softDeleted = false;
            if (entry is { State: EntityState.Deleted, Entity: ISoftDeletable deletable })
            {
                entry.State = EntityState.Modified;
                deletable.MarkDeleted(now);
                softDeleted = true;
            }

            if (entry.Entity is not IAuditable)
            {
                continue;
            }

            var action = softDeleted || entry.State == EntityState.Deleted ? AuditActions.Deleted
                : entry.State == EntityState.Added ? AuditActions.Created
                : AuditActions.Updated;

            var changes = Diff(entry, action);
            if (action == AuditActions.Updated && changes.Count == 0)
            {
                continue;
            }

            audit.Add(new AuditEntry
            {
                Entity = entry.Metadata.ClrType.Name,
                EntityId = Convert.ToString(entry.Property("Id").CurrentValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                Action = action,
                UserId = currentUser.UserId,
                At = now,
                Ip = currentUser.IpAddress,
                Origin = origin.Label,
                Changes = JsonSerializer.Serialize(changes),
            });
        }

        context.Set<AuditEntry>().AddRange(audit);
    }

    private static Dictionary<string, Dictionary<string, object?>> Diff(EntityEntry entry, string action)
    {
        var changes = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsShadowProperty() && property.Metadata.Name != "IsDeleted")
            {
                continue;
            }

            var changed = action switch
            {
                AuditActions.Created => property.CurrentValue is not null,
                AuditActions.Deleted when entry.State == EntityState.Deleted => true,
                _ => property.IsModified && !Equals(property.OriginalValue, property.CurrentValue),
            };

            if (!changed)
            {
                continue;
            }

            var member = property.Metadata.PropertyInfo;
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Metadata.Name);
            changes[name] = member?.GetCustomAttribute<ProtectedSecretAttribute>() is not null
                ? new() { ["changed"] = true }
                : member?.GetCustomAttribute<PersonalDataAttribute>() is not null
                    ? new() { ["old"] = Mask(action == AuditActions.Created ? null : property.OriginalValue), ["new"] = Mask(property.CurrentValue) }
                    : new() { ["old"] = action == AuditActions.Created ? null : property.OriginalValue, ["new"] = action == AuditActions.Deleted && entry.State == EntityState.Deleted ? null : property.CurrentValue };
        }

        return changes;
    }

    private static string? Mask(object? value) => value is null ? null : Masked;
}
