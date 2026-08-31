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
    public DbSet<LeagueFixture> LeagueFixtures => Set<LeagueFixture>();
    public DbSet<LeagueLineup> LeagueLineups => Set<LeagueLineup>();
    public DbSet<LeagueTraining> LeagueTrainings => Set<LeagueTraining>();

    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();

    public DbSet<LeagueOffer> LeagueOffers => Set<LeagueOffer>();
    public DbSet<LeagueListing> LeagueListings => Set<LeagueListing>();

    public DbSet<LiveMatch> LiveMatches => Set<LiveMatch>();

    public DbSet<RankedWorld> RankedWorlds => Set<RankedWorld>();
    public DbSet<RankedGroup> RankedGroups => Set<RankedGroup>();
    public DbSet<RankedSeat> RankedSeats => Set<RankedSeat>();
    public DbSet<RankedCoach> RankedCoaches => Set<RankedCoach>();
    public DbSet<RankedFixture> RankedFixtures => Set<RankedFixture>();
    public DbSet<RankedLineup> RankedLineups => Set<RankedLineup>();
    public DbSet<RankedTraining> RankedTrainings => Set<RankedTraining>();
    public DbSet<RankedOffer> RankedOffers => Set<RankedOffer>();
    public DbSet<RankedAuction> RankedAuctions => Set<RankedAuction>();
    public DbSet<RankedAward> RankedAwards => Set<RankedAward>();
    public DbSet<RankedLiveMatch> RankedLiveMatches => Set<RankedLiveMatch>();

    public DbSet<IntegrityFlag> IntegrityFlags => Set<IntegrityFlag>();
    public DbSet<AccountSignal> AccountSignals => Set<AccountSignal>();

    public DbSet<BalanceRevision> BalanceRevisions => Set<BalanceRevision>();
    public DbSet<AdminAuditEntry> AdminAudit => Set<AdminAuditEntry>();

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
            // Server-authoritative condition (Phase 8.4). Column defaults backfill any pre-8.4 rows to
            // a sensible neutral state (an existing world otherwise gets 0 fitness = broken); new rows
            // carry the entity's seeded values (50/50/100).
            e.Property(x => x.Form).HasDefaultValue(50);
            e.Property(x => x.Morale).HasDefaultValue(50);
            e.Property(x => x.Fitness).HasDefaultValue(100);
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

        // --- Season: fixtures & submitted inputs (Phase 8.3) --------------------------------

        b.Entity<LeagueFixture>(e =>
        {
            e.ToTable("league_fixtures");
            e.HasKey(x => x.Id);
            // Cascade from the private league — disbanding a league removes its fixtures. The two
            // club FKs are Restrict (NoAction): a single table with two cascade paths back to clubs
            // (already cascaded from worlds) would create multiple cascade paths, which SQL Server /
            // some providers reject; the "last member leaves" teardown deletes fixtures explicitly
            // before clubs anyway (portable across PostgreSQL and the SQLite test provider).
            e.HasOne(x => x.PrivateLeague)
                .WithMany()
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HomeClub)
                .WithMany()
                .HasForeignKey(x => x.HomeClubId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AwayClub)
                .WithMany()
                .HasForeignKey(x => x.AwayClubId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PrivateLeagueId, x.Round, x.MatchIndex });
        });

        b.Entity<LeagueLineup>(e =>
        {
            e.ToTable("league_lineups");
            e.HasKey(x => x.Id);
            e.Property(x => x.LineupJson).IsRequired();
            e.HasOne(x => x.PrivateLeague)
                .WithMany()
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Club)
                .WithMany()
                .HasForeignKey(x => x.ClubId)
                .OnDelete(DeleteBehavior.Restrict);
            // One live submission per club (upsert key), plus a lookup by member.
            e.HasIndex(x => new { x.PrivateLeagueId, x.ClubId }).IsUnique();
            e.HasIndex(x => new { x.PrivateLeagueId, x.UserId });
        });

        // --- Season: submitted training plans (Phase 8.4) -----------------------------------

        b.Entity<LeagueTraining>(e =>
        {
            e.ToTable("league_trainings");
            e.HasKey(x => x.Id);
            e.Property(x => x.TrainingJson).IsRequired();
            e.HasOne(x => x.PrivateLeague)
                .WithMany()
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict (like league_lineups) — two club FKs already cascade from worlds; the disband
            // teardown deletes league_trainings explicitly before clubs (portable Postgres/SQLite).
            e.HasOne(x => x.Club)
                .WithMany()
                .HasForeignKey(x => x.ClubId)
                .OnDelete(DeleteBehavior.Restrict);
            // One live training plan per club (upsert key), plus a lookup by member.
            e.HasIndex(x => new { x.PrivateLeagueId, x.ClubId }).IsUnique();
            e.HasIndex(x => new { x.PrivateLeagueId, x.UserId });
        });

        // --- Online auctions (Phase 8.5) ----------------------------------------------------

        b.Entity<Auction>(e =>
        {
            e.ToTable("auctions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            // Cascade from the private league — disbanding a league removes its lots.
            e.HasOne(x => x.PrivateLeague)
                .WithMany()
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict on the player FK: players already cascade from worlds, so a second cascade path
            // (auctions → private_leagues → worlds AND auctions → players → worlds) would be rejected by
            // some providers. The disband teardown deletes auctions explicitly before players.
            e.HasOne(x => x.Player)
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PrivateLeagueId, x.Status });
            e.HasIndex(x => x.PlayerId);
        });

        b.Entity<Bid>(e =>
        {
            e.ToTable("bids");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Auction)
                .WithMany(a => a.Bids)
                .HasForeignKey(x => x.AuctionId)
                .OnDelete(DeleteBehavior.Cascade);
            // PrivateLeagueId/ClubId/UserId are plain denormalised columns (no relationship) — the
            // league/club referential integrity is server-authoritative, and this keeps a single cascade
            // path (bids → auctions → private_leagues), portable across PostgreSQL and the SQLite test provider.
            e.HasIndex(x => x.AuctionId);
            e.HasIndex(x => x.PrivateLeagueId);
        });

        // --- Live match control (Phase 8.6) -------------------------------------------------

        b.Entity<LiveMatch>(e =>
        {
            e.ToTable("live_matches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.ChangesJson).IsRequired();
            // Cascade from the private league — disbanding a league removes its live sessions. The
            // fixture / club / user ids are plain denormalised columns (no relationship), keeping a single
            // cascade path (live_matches → private_leagues), portable across PostgreSQL and the SQLite
            // test provider — the same approach as bids (8.5).
            e.HasOne(x => x.PrivateLeague)
                .WithMany()
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            // One live session per fixture.
            e.HasIndex(x => x.FixtureId).IsUnique();
            e.HasIndex(x => new { x.PrivateLeagueId, x.Status });
        });

        // --- Public ranked ladder (Phase 9.1) -----------------------------------------------

        b.Entity<RankedWorld>(e =>
        {
            e.ToTable("ranked_worlds");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => x.Status);
        });

        b.Entity<RankedGroup>(e =>
        {
            e.ToTable("ranked_groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne(x => x.RankedWorld)
                .WithMany(w => w.Groups)
                .HasForeignKey(x => x.RankedWorldId)
                .OnDelete(DeleteBehavior.Cascade);
            // The generated world is materialised lazily and is optional — SetNull (not cascade) so a
            // world teardown can never take the competition structure with it, and so there is a single
            // cascade path into ranked_groups (from ranked_worlds).
            e.HasOne(x => x.World)
                .WithMany()
                .HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.RankedWorldId, x.Kind, x.Tier, x.GroupIndex });
            e.HasIndex(x => new { x.Kind, x.Status });
            // Season counter (Phase 9.3): a DB default of 1 so groups created before the seasonal reset
            // existed are backfilled as "on their first season" rather than season 0.
            e.Property(x => x.SeasonNumber).HasDefaultValue(1);
        });

        b.Entity<RankedSeat>(e =>
        {
            e.ToTable("ranked_seats");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.RankedGroup)
                .WithMany(g => g.Seats)
                .HasForeignKey(x => x.RankedGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            // SetNull (like league_members.ClubId): clubs already cascade from worlds, and the seat must
            // survive a world teardown so the group keeps its fixed size.
            e.HasOne(x => x.Club)
                .WithMany()
                .HasForeignKey(x => x.ClubId)
                .OnDelete(DeleteBehavior.SetNull);
            // A seat number is unique inside its group — the fixed-size guarantee at the DB level.
            e.HasIndex(x => new { x.RankedGroupId, x.SeatIndex }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<RankedCoach>(e =>
        {
            e.ToTable("ranked_coaches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne(x => x.RankedWorld)
                .WithMany()
                .HasForeignKey(x => x.RankedWorldId)
                .OnDelete(DeleteBehavior.Cascade);
            // One ladder enrolment per account, ever (the lookup key for /ranked/me).
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => new { x.RankedWorldId, x.Status });
        });

        // --- Ranked real-time season (Phase 9.2) --------------------------------------------

        b.Entity<RankedFixture>(e =>
        {
            e.ToTable("ranked_fixtures");
            e.HasKey(x => x.Id);
            // Cascade from the group — retiring a group removes its schedule. The two club FKs are
            // Restrict (like league_fixtures): a single table with two cascade paths back to clubs
            // (already cascaded from worlds) would create multiple cascade paths some providers reject.
            e.HasOne(x => x.RankedGroup)
                .WithMany()
                .HasForeignKey(x => x.RankedGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HomeClub)
                .WithMany()
                .HasForeignKey(x => x.HomeClubId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AwayClub)
                .WithMany()
                .HasForeignKey(x => x.AwayClubId)
                .OnDelete(DeleteBehavior.Restrict);
            // Deterministic identity of a fixture within a group; drives the "resolve the lowest due round".
            e.HasIndex(x => new { x.RankedGroupId, x.Round, x.MatchIndex }).IsUnique();
            e.HasIndex(x => new { x.RankedGroupId, x.IsPlayed });
        });

        // --- Live ranked matches (task 12.3) ------------------------------------------------

        b.Entity<RankedLiveMatch>(e =>
        {
            e.ToTable("ranked_live_matches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.ChangesJson).IsRequired();
            // Cascade from the group, exactly like ranked_fixtures — one cascade path, portable across
            // PostgreSQL and the SQLite test provider. The fixture / club / user ids stay plain
            // denormalised columns (no relationship), which is what keeps that path single.
            e.HasOne(x => x.RankedGroup)
                .WithMany()
                .HasForeignKey(x => x.RankedGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            // One live session per fixture.
            e.HasIndex(x => x.FixtureId).IsUnique();
            // The calendar's question, every tick: "is anything in this round still being played?"
            e.HasIndex(x => new { x.RankedGroupId, x.Round, x.Status });
        });

        b.Entity<RankedLineup>(e =>
        {
            e.ToTable("ranked_lineups");
            e.HasKey(x => x.Id);
            e.Property(x => x.LineupJson).IsRequired();
            e.HasOne(x => x.RankedGroup)
                .WithMany()
                .HasForeignKey(x => x.RankedGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict on the club FK (clubs already cascade from worlds — single cascade path via the group).
            e.HasOne(x => x.Club)
                .WithMany()
                .HasForeignKey(x => x.ClubId)
                .OnDelete(DeleteBehavior.Restrict);
            // One live submission per club in a group (upsert key), plus a lookup by coach.
            e.HasIndex(x => new { x.RankedGroupId, x.ClubId }).IsUnique();
            e.HasIndex(x => new { x.RankedGroupId, x.UserId });
        });

        // --- Daily loop: stored training plans (Phase 9.4) ----------------------------------

        b.Entity<RankedTraining>(e =>
        {
            e.ToTable("ranked_trainings");
            e.HasKey(x => x.Id);
            e.Property(x => x.TrainingJson).IsRequired();
            e.HasOne(x => x.RankedGroup)
                .WithMany()
                .HasForeignKey(x => x.RankedGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict on the club FK (clubs already cascade from worlds — single cascade path via the group),
            // exactly like ranked_lineups; the seasonal reset deletes these rows explicitly.
            e.HasOne(x => x.Club)
                .WithMany()
                .HasForeignKey(x => x.ClubId)
                .OnDelete(DeleteBehavior.Restrict);
            // One live training plan per club in a group (upsert key), plus a lookup by coach.
            e.HasIndex(x => new { x.RankedGroupId, x.ClubId }).IsUnique();
            e.HasIndex(x => new { x.RankedGroupId, x.UserId });
        });

        // --- Private-league transfer market (Phase 12.1) ------------------------------------

        b.Entity<LeagueOffer>(e =>
        {
            e.ToTable("league_offers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.ProposedBy).HasConversion<int>();
            // Cascade from the lobby (single path). Player/club/user ids are plain denormalised columns,
            // the same convention as bids/ranked_offers — integrity is server-authoritative.
            e.HasOne(x => x.PrivateLeague)
                .WithMany()
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PrivateLeagueId, x.Status });
            e.HasIndex(x => x.SellerUserId);
            e.HasIndex(x => x.BuyerUserId);
            e.HasIndex(x => new { x.PrivateLeagueId, x.PlayerId });
        });

        b.Entity<LeagueListing>(e =>
        {
            e.ToTable("league_listings");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.PrivateLeague)
                .WithMany()
                .HasForeignKey(x => x.PrivateLeagueId)
                .OnDelete(DeleteBehavior.Cascade);
            // One live listing per player per league.
            e.HasIndex(x => new { x.PrivateLeagueId, x.PlayerId }).IsUnique();
            e.HasIndex(x => new { x.PrivateLeagueId, x.ClubId });
        });

        b.Entity<RankedOffer>(e =>
        {
            e.ToTable("ranked_offers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            // Cascade from the group (single path). The player/club/user ids are plain denormalised columns.
            e.HasOne(x => x.RankedGroup)
                .WithMany()
                .HasForeignKey(x => x.RankedGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RankedGroupId, x.Status });
            e.HasIndex(x => x.SellerUserId);
            e.HasIndex(x => x.BuyerUserId);
        });

        b.Entity<RankedAuction>(e =>
        {
            e.ToTable("ranked_auctions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            // Cascade from the group (single path). Player/club/user ids are plain denormalised columns.
            e.HasOne(x => x.RankedGroup)
                .WithMany()
                .HasForeignKey(x => x.RankedGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RankedGroupId, x.Status });
            e.HasIndex(x => new { x.RankedGroupId, x.WindowIndex });
            // Task 12.2: "what have I got on the board?" — the seller's own lots, and the settlement's
            // per-lot due query (status + timer) now that lots no longer share a window-wide end.
            e.HasIndex(x => new { x.RankedGroupId, x.SellerClubId });
            e.HasIndex(x => new { x.Status, x.EndsUtc });
        });

        // --- Coach ranking & seasonal rewards (Phase 9.3) -----------------------------------

        b.Entity<RankedAward>(e =>
        {
            e.ToTable("ranked_awards");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.WorldName).HasMaxLength(120).IsRequired();
            e.Property(x => x.GroupName).HasMaxLength(120).IsRequired();
            // NO foreign keys on purpose: a palmarès must survive the world/group it was earned in
            // (a seasonal reset reopens the group, and a world can be retired) — see the entity remarks.
            e.HasIndex(x => new { x.UserId, x.AwardedUtc });
            e.HasIndex(x => new { x.UserId, x.Kind });
        });

        // --- Abuse & integrity (Phase 9.5) --------------------------------------------------

        b.Entity<IntegrityFlag>(e =>
        {
            e.ToTable("integrity_flags");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Details).HasMaxLength(512).IsRequired();
            // NO foreign keys on purpose: the audit trail must survive the group/world it refers to,
            // exactly like ranked_awards — see the entity remarks.
            e.HasIndex(x => new { x.Status, x.CreatedUtc });
            e.HasIndex(x => new { x.Kind, x.CreatedUtc });
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.SubjectUserId);
        });

        b.Entity<AccountSignal>(e =>
        {
            e.ToTable("account_signals");
            e.HasKey(x => x.Id);
            e.Property(x => x.AddressHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.DeviceHash).HasMaxLength(64).IsRequired();
            // Fingerprints belong to the account: deleting the account deletes them.
            e.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // Upsert key, plus the reverse lookup the link heuristics walk (who else was seen here?).
            e.HasIndex(x => new { x.UserId, x.AddressHash, x.DeviceHash }).IsUnique();
            e.HasIndex(x => x.AddressHash);
            e.HasIndex(x => x.DeviceHash);
        });

        // --- Live ops (Phase 10.3) ----------------------------------------------------------

        b.Entity<BalanceRevision>(e =>
        {
            e.ToTable("balance_revisions");
            e.HasKey(x => x.Id);
            // The whole serialised BalanceConfig — tens of KB, deliberately unbounded (a MaxLength here
            // would turn "someone added a section to Sim.Core" into a truncated, unreadable revision).
            e.Property(x => x.Json).IsRequired();
            e.Property(x => x.Note).HasMaxLength(280).IsRequired();
            e.Property(x => x.CreatedByEmail).HasMaxLength(256).IsRequired();
            // The active balance is "the highest revision", so the counter must be unique — two rows
            // claiming the same number would make the question ambiguous. Concurrent pushes race on this
            // index and the loser gets a duplicate-key error rather than a silent overwrite.
            e.HasIndex(x => x.Revision).IsUnique();
            // NO foreign key to the author on purpose: a revision outlives the account that pushed it
            // (same reasoning as ranked_awards), which is why the email is captured by value.
        });

        b.Entity<AdminAuditEntry>(e =>
        {
            e.ToTable("admin_audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasConversion<int>();
            e.Property(x => x.ActorEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.Target).HasMaxLength(120).IsRequired();
            e.Property(x => x.Details).HasMaxLength(512).IsRequired();
            // FK-free like ranked_awards / integrity_flags: an audit trail a cascade delete can erase is
            // not an audit trail.
            e.HasIndex(x => x.CreatedUtc);
            e.HasIndex(x => new { x.Action, x.CreatedUtc });
            e.HasIndex(x => x.ActorUserId);
        });
    }
}
