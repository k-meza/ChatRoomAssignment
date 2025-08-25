using API.Repositories.AppDbContext.Entites;
using Microsoft.EntityFrameworkCore;

namespace API.Repositories.AppDbContext;

public interface IAppDbContext
{
    public DbSet<ChatRoom> ChatRooms { get; set; }
    public DbSet<Message> Messages { get; set; }
}