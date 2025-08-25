namespace API.Repositories.AppDbContext.Entites;

public class ChatRoom
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}