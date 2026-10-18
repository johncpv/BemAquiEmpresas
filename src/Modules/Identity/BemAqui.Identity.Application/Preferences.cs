using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Preferências de quem está logado: o tema segue o usuário em qualquer dispositivo (FND-09a CA 9).</summary>
public sealed record PreferencesDto(string Theme);

public sealed class Preferences(IIdentityDb db, TimeProvider clock)
{
    public static readonly string[] Themes = ["system", "light", "dark"];

    public async Task<PreferencesDto> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var theme = await db.UserPreferences.Where(p => p.UserId == userId && p.Key == UserPreference.Theme).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken);
        return new PreferencesDto(Themes.Contains(theme) ? theme! : "system");
    }

    public async Task<Result<PreferencesDto>> SaveAsync(Guid userId, PreferencesDto preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!Themes.Contains(preferences.Theme))
        {
            return Error.Validation(Error.Codes.Validation, "Tema inválido: use Automático, Claro ou Escuro.", new Dictionary<string, string[]> { ["theme"] = ["Tema inválido."] });
        }

        var row = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.Key == UserPreference.Theme, cancellationToken);
        if (row is null)
        {
            row = new UserPreference { UserId = userId, Key = UserPreference.Theme };
            db.UserPreferences.Add(row);
        }

        row.Value = preferences.Theme;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return preferences;
    }
}
