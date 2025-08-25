using API.Repositories.AppDbContext.Entites;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace API.Repositories.AppDbContext;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ChatRoom> ChatRooms { get; set; }
    public DbSet<Message> Messages { get; set; }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ChatRoom>()
            .HasIndex(x => x.Name)
            .IsUnique();

        b.Entity<Message>()
            .HasIndex(x => new { x.ChatRoomId, x.CreatedAtUtc });

        b.Entity<Message>()
            .HasOne(m => m.ChatRoom)
            .WithMany(r => r.Messages)
            .HasForeignKey(m => m.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Message>()
            .HasOne(m => m.User)
            .WithMany() // not tracking reverse navigation explicitly
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}