using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fts.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the FTS backend (PostgreSQL). Holds the world-scoped core schema
/// (ARCHITECTURE §6.3). Auth (users/coach accounts) and the online tables
/// (seasons/fixtures/match_reports/auctions/bids/transactions/rankings/notifications) land
/// in later phases; the model is kept additive so those are new migrations, not rewrites.
/// </summary>
public sealed class FtsDbContext : DbContext
{
    public FtsDbContext(DbContextOptions<FtsDbContext> options) : base(options) { }

    public DbSet<World> Worlds => Set<World>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Coach> Coaches => Set<Coach>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Transfer> Transfers => Set<Transfer>();

    protected override void OnModelCreating(ModelBuilder b)
    {
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
    }
}
