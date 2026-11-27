using BemAqui.Identity.Application;
using BemAqui.Identity.Application.ConnectedApps;
using BemAqui.Identity.Domain;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BemAqui.Identity.Infrastructure;

/// <summary>Aplicativo conectado (<c>identity.connected_apps</c>, INT-01): um por <c>client_id</c>.</summary>
internal sealed class ConnectedAppConfiguration : IEntityTypeConfiguration<ConnectedApp>
{
    public void Configure(EntityTypeBuilder<ConnectedApp> b)
    {
        b.ToTable("connected_apps", IdentitySchema.Name);
        b.Property(e => e.ClientId).HasMaxLength(ConnectedApp.MaxClientIdLength);
        b.HasIndex(e => e.ClientId).IsUnique();
        b.Property(e => e.Name).HasMaxLength(ConnectedApp.MaxNameLength);
        b.Property(e => e.RequiredModule).HasMaxLength(40);
    }
}

/// <summary>Intenção de conexão: só o hash (único), 10 min, uso único.</summary>
internal sealed class AppConnectIntentConfiguration : IEntityTypeConfiguration<AppConnectIntent>
{
    public void Configure(EntityTypeBuilder<AppConnectIntent> b)
    {
        b.ToTable("app_connect_intents", IdentitySchema.Name);
        b.Property(e => e.IntentHash).HasMaxLength(64);
        b.HasIndex(e => e.IntentHash).IsUnique();
        b.Property(e => e.RequestBindingHash).HasMaxLength(64);
        b.HasIndex(e => e.ExpiresAt);
        b.HasOne<ConnectedApp>().WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Código de autorização: só o hash (único), 60 s, uso único.</summary>
internal sealed class AppAuthorizationCodeConfiguration : IEntityTypeConfiguration<AppAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<AppAuthorizationCode> b)
    {
        b.ToTable("app_authorization_codes", IdentitySchema.Name);
        b.Property(e => e.CodeHash).HasMaxLength(64);
        b.HasIndex(e => e.CodeHash).IsUnique();
        b.Property(e => e.RedirectUri).HasMaxLength(500);
        b.Property(e => e.CodeChallenge).HasMaxLength(64);
        b.Property(e => e.Scopes).HasColumnType("text[]");
        b.Property(e => e.AccountLabel).HasMaxLength(AppConnection.MaxAccountLabelLength);
        b.HasIndex(e => e.ExpiresAt);
        b.HasOne<ConnectedApp>().WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Conexão: no máximo uma não revogada por aplicativo (índice único parcial), concorrência otimista pelo xmin.</summary>
internal sealed class AppConnectionConfiguration : IEntityTypeConfiguration<AppConnection>
{
    public void Configure(EntityTypeBuilder<AppConnection> b)
    {
        b.ToTable("app_connections", IdentitySchema.Name);
        b.Property(e => e.GrantedScopes).HasColumnType("text[]");
        b.Property(e => e.AccountLabel).HasMaxLength(AppConnection.MaxAccountLabelLength);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.StatusReason).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.RevokedReason).HasMaxLength(200);
        b.Property(e => e.LastUsedIp).HasMaxLength(64);
        b.HasIndex(e => e.AppId).IsUnique().HasFilter("status <> 'Revoked'").HasDatabaseName("ux_app_connections_open");
        b.HasIndex(e => e.GrantorUserId);
        b.HasOne<ConnectedApp>().WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Token opaco de aplicativo: só o hash (único); a família liga as renovações.</summary>
internal sealed class AppTokenConfiguration : IEntityTypeConfiguration<AppToken>
{
    public void Configure(EntityTypeBuilder<AppToken> b)
    {
        b.ToTable("app_tokens", IdentitySchema.Name);
        b.Property(e => e.TokenHash).HasMaxLength(64);
        b.HasIndex(e => e.TokenHash).IsUnique();
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(8);
        b.HasIndex(e => e.ConnectionId);
        b.HasIndex(e => e.FamilyId);
        b.HasOne<AppConnection>().WithMany().HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>A linha do Conecta nasce em todo tenant (INT-01, regras), com o nome da configuração do ambiente.</summary>
public sealed class ConnectedAppSeeder : ITenantSeeder
{
    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();
        var options = tenantScope.GetRequiredService<IOptions<ConnectedAppOptions>>().Value;
        var app = await db.ConnectedApps.FirstOrDefaultAsync(a => a.ClientId == options.ClientId, cancellationToken);
        if (app is null)
        {
            db.ConnectedApps.Add(new ConnectedApp
            {
                ClientId = options.ClientId, Name = options.Name, RequiredModule = options.RequiredModule,
                CreatedAt = tenantScope.GetRequiredService<TimeProvider>().GetUtcNow(),
            });
        }
        else
        {
            app.Name = options.Name;
            app.RequiredModule = options.RequiredModule;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
