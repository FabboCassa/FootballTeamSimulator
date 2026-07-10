using Fts.Infrastructure.Auth;
using Fts.Infrastructure.Notifications;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fts.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the FTS backend (PostgreSQL). Extends <see cref="IdentityDbContext{TUser,TRole,TKey}"/>
/// so ASP.NET Core Identity owns the auth tables (added in Phase 7.2), on top of the world-scoped
/// core schema (ARCHITECTURE §6.3). The remaining online tables
/// (seasons/fixtures/match_reports/auctions/bids/transactions/rankings/notifications) land in later
/// phases; the model is kept additive so those are new migrations, not rewrites.
/// </summary>
public sealed class FtsDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public FtsDbContext(DbContextOptions<FtsDbContext> options) : base(options) { }

    public DbSet<World> Worlds => Set<World>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Coach> Coaches => Set<Coach>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Transfer> Transfers => Set<Transfer>();

    public DbSet<CoachProfile> CoachProfiles => Set<CoachProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<DeviceRegistration> DeviceRegistrations => Set<DeviceRegistration>();

    public DbSet<PrivateLeague> PrivateLeagues => Set<PrivateLeague>();
    public DbSet<LeagueMember> LeagueMembers => Set<LeagueMember>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // IdentityDbContext.OnModelCreating configures the Identity tables — call it first.
        base.OnModelCreating(b);

        b.Entity<World>(e =>
        {
            e.ToTable("worlds");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        });

        b.Entity<League>(e =>
        {
            e.ToTable("leagues");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.World)
                .WithMany(w => w.Leagues)
                .HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorldId, x.Division });
        });

        b.Entity<Club>(e =>
        {
            e.ToTable("clubs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ShortName).HasMaxLength(8).IsRequired();
            e.HasOne(x => x.World)
                .WithMany(w => w.Clubs)
                .HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.League)
                .WithMany(l => l.Clubs)
                .HasForeignKey(x => x.LeagueId)
                .OnDelete(DeleteBehavior.Restrict);
            // Players are unique per world (§6.3) — the same holds for clubs' Sim.Core ids.
            e.HasIndex(x => new { x.WorldId, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.LeagueId);
        });

        b.Entity<Coach>(e =>
        {
            e.ToTable("coaches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.World)
                .WithMany(w => w.Coaches)
                .HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
            // One coach per club at most; a coach can also be clubless (between jobs).
            e.HasOne(x => x.Club)
                .WithOne(c => c.Coach)
                .HasForeignKey<Coach>(x => x.ClubId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorldId, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
        });

        b.Entity<Player>(e =>
        {
            e.ToTable("players");
            e.HasKey(x => x.Id);
            e.Property(x => x.FirstName).HasMaxLength(60).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(60).IsRequired();
            e.Property(x => x.AttributesJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.World)
                .WithMany(w => w.Players)
                .HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Club)
                .WithMany(c => c.Players)
                .HasForeignKey(x => x.ClubId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorldId, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.ClubId);
        });

        b.Entity<Transfer>(e =>
        {
            e.ToTable("transfers");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.World)
                .WithMany(w => w.Transfers)
                .HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player)
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorldId, x.SeasonYear, x.Day });
        });

        // --- Auth (Phase 7.2) ---------------------------------------------------------------

        // Friendlier snake_case names for the Identity tables (the base type maps them to
        // AspNetUsers/AspNetRoles/... by default). "users" matches ARCHITECTURE §6.3.
        b.Entity<AppUser>(e => e.ToTable("users"));
        b.Entity<IdentityRole<Guid>>(e => e.ToTable("roles"));
        b.Entity<IdentityUserRole<Guid>>(e => e.ToTable("user_roles"));
        b.Entity<IdentityUserClaim<Guid>>(e => e.ToTable("user_claims"));
        b.Entity<IdentityUserLogin<Guid>>(e => e.ToTable("user_logins"));
        b.Entity<IdentityUserToken<Guid>>(e => e.ToTable("user_tokens"));
        b.Entity<IdentityRoleClaim<Guid>>(e => e.ToTable("role_claims"));

        b.Entity<CoachProfile>(e =>
        {
            e.ToTable("coach_profiles");
            // Shared primary key with the user (1:1).
            e.HasKey(x => x.UserId);
            e.Property(x => x.DisplayName).HasMaxLength(60).IsRequired();
            e.HasOne(x => x.User)
                .WithOne(u => u.Profile)
                .HasForeignKey<CoachProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DisplayName);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasOne(x => x.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        // --- Notifications / device tokens (Phase 7.4) --------------------------------------

        b.Entity<DeviceRegistration>(e =>
        {
            e.ToTable("device_registrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(512).IsRequired();
            e.Property(x => x.Platform).HasConversion<int>();
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // A device token is globally unique (one per app install) — the upsert key.
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        // --- Private leagues / lobbies (Phase 8.1) ------------------------------------------

        b.Entity<PrivateLeague>(e =>
        {
            e.ToTable("private_leagues");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.InviteCode).HasMaxLength(16).IsRequired();
            e.Property(x => x.Mode).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            // Deleting the world removes its private league (and members) — the "last member
            // leaves" path deletes the world and lets this cascade clean up the lobby.
            e.HasOne(x => x.World)
                .WithMany()
                .HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
            // Invite codes are globally unique — the join lookup key.
            e.HasIndex(x => x.InviteCode).IsUnique();
            e.HasIndex(x => x.CreatorUserId);
        });

        b.Entity<LeagueMember>(e =>
        {
            e.ToTable("league_members");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.PrivateLeague)
                .WithMany(l => l.Members)
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            // The assigned club (8.2) — SetNull, not cascade, to avoid a second cascade path
            // from worlds → clubs → members (portable across providers).
            e.HasOne(x => x.Club)
                .WithMany()
                .HasForeignKey(x => x.ClubId)
                .OnDelete(DeleteBehavior.SetNull);
            // One membership per account per league.
            e.HasIndex(x => new { x.PrivateLeagueId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });
    }
}
