using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Models;

namespace SteamyKeyz.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GamePlatform> GamePlatforms => Set<GamePlatform>();
    public DbSet<Key> Keys => Set<Key>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── GamePlatform: composite PK ──────────────────────────
        modelBuilder.Entity<GamePlatform>()
            .HasKey(gp => new { gp.GameId, gp.PlatformId });

        // ── User: unique constraints ────────────────────────────
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username).IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();

        // ── ShoppingCart: one-to-one with User ──────────────────
        modelBuilder.Entity<ShoppingCart>()
            .HasIndex(sc => sc.UserId).IsUnique();

        modelBuilder.Entity<ShoppingCart>()
            .HasOne(sc => sc.User)
            .WithOne(u => u.ShoppingCart)
            .HasForeignKey<ShoppingCart>(sc => sc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── CartItem → ShoppingCart: cascade ────────────────────
        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.ShoppingCart)
            .WithMany(sc => sc.CartItems)
            .HasForeignKey(ci => ci.ShoppingCartId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Game)
            .WithMany(g => g.CartItems)
            .HasForeignKey(ci => ci.GameId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Platform)
            .WithMany(p => p.CartItems)
            .HasForeignKey(ci => ci.PlatformId)
            .OnDelete(DeleteBehavior.NoAction);

        // ── GamePlatform: cascades ──────────────────────────────
        modelBuilder.Entity<GamePlatform>()
            .HasOne(gp => gp.Game)
            .WithMany(g => g.GamePlatforms)
            .HasForeignKey(gp => gp.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GamePlatform>()
            .HasOne(gp => gp.Platform)
            .WithMany(p => p.GamePlatforms)
            .HasForeignKey(gp => gp.PlatformId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Key: unique constraint + no cascade ─────────────────
        modelBuilder.Entity<Key>()
            .HasIndex(k => k.KeyValue).IsUnique();

        modelBuilder.Entity<Key>()
            .HasOne(k => k.Game)
            .WithMany(g => g.Keys)
            .HasForeignKey(k => k.GameId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Key>()
            .HasOne(k => k.Platform)
            .WithMany(p => p.Keys)
            .HasForeignKey(k => k.PlatformId)
            .OnDelete(DeleteBehavior.NoAction);

        // ── Invoice: no cascade from User (preserve history) ────
        modelBuilder.Entity<Invoice>()
            .HasIndex(i => i.InvoiceNumber).IsUnique();

        modelBuilder.Entity<Invoice>()
            .HasOne(i => i.User)
            .WithMany(u => u.Invoices)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // ── InvoiceItem: cascade from Invoice, unique Key ───────
        modelBuilder.Entity<InvoiceItem>()
            .HasIndex(ii => ii.KeyId).IsUnique();

        modelBuilder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Invoice)
            .WithMany(i => i.InvoiceItems)
            .HasForeignKey(ii => ii.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Key)
            .WithOne(k => k.InvoiceItem)
            .HasForeignKey<InvoiceItem>(ii => ii.KeyId)
            .OnDelete(DeleteBehavior.NoAction);

        // ── Unique constraints ──────────────────────────────────
        modelBuilder.Entity<Role>()
            .HasIndex(r => r.Name).IsUnique();

        modelBuilder.Entity<Platform>()
            .HasIndex(p => p.Name).IsUnique();

        // ── Default values ──────────────────────────────────────
        modelBuilder.Entity<User>()
            .Property(u => u.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        modelBuilder.Entity<User>()
            .Property(u => u.IsActive).HasDefaultValue(true);

        modelBuilder.Entity<ShoppingCart>()
            .Property(sc => sc.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        modelBuilder.Entity<ShoppingCart>()
            .Property(sc => sc.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        modelBuilder.Entity<Key>()
            .Property(k => k.Status).HasDefaultValue("Available");

        modelBuilder.Entity<Key>()
            .Property(k => k.AddedAt).HasDefaultValueSql("GETUTCDATE()");

        modelBuilder.Entity<Invoice>()
            .Property(i => i.Status).HasDefaultValue("Pending");

        modelBuilder.Entity<Invoice>()
            .Property(i => i.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
    }
}
