using API.Domain.Messaging.Interfaces;
using API.Domain.Options;
using API.Hubs;
using API.Repositories.AppDbContext;
using API.Repositories.AppDbContext.Entites;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Security.Claims;

namespace Tests.API.HubsTests;

[TestFixture]
public class ChatHubTests
{
    private Mock<ILogger<ChatHub>> _mockLogger;
    private AppDbContext _dbContext;
    private Mock<UserManager<AppUser>> _mockUserManager;
    private Mock<IRabbitMqService> _mockBus;
    private RabbitMqOptions _rmqOptions;
    private Mock<HubCallerContext> _mockContext;
    private Mock<IHubCallerClients> _mockClients;
    private Mock<ISingleClientProxy> _mockCaller;
    private Mock<IClientProxy> _mockGroup;
    private Mock<IGroupManager> _mockGroups;
    private Mock<HttpContext> _mockHttpContext;
    private Mock<ConnectionInfo> _mockConnection;
    private ChatHub _chatHub;
    private AppUser _testUser;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<ChatHub>>();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options);

        _mockUserManager = CreateMockUserManager();
        _mockBus = new Mock<IRabbitMqService>();
        _rmqOptions = new RabbitMqOptions { CommandsExchange = "commands" };
        _mockContext = new Mock<HubCallerContext>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockCaller = new Mock<ISingleClientProxy>();
        _mockGroup = new Mock<IClientProxy>();
        _mockGroups = new Mock<IGroupManager>();
        _mockHttpContext = new Mock<HttpContext>();
        _mockConnection = new Mock<ConnectionInfo>();

        // Setup test data
        _testUser = new AppUser
        {
            Id = "test-user-id",
            UserName = "testuser",
            Email = "test@example.com"
        };

        // Setup Hub context
        SetupHubContext();

        // Create ChatHub instance
        _chatHub = new ChatHub(
            _dbContext,
            _mockUserManager.Object,
            _mockBus.Object,
            _rmqOptions,
            _mockLogger.Object)
        {
            Context = _mockContext.Object,
            Clients = _mockClients.Object,
            Groups = _mockGroups.Object
        };
    }

    private Mock<UserManager<AppUser>> CreateMockUserManager()
    {
        var mockStore = new Mock<IUserStore<AppUser>>();
        return new Mock<UserManager<AppUser>>(mockStore.Object, null, null, null, null, null, null, null, null);
    }

    private void SetupHubContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "testuser")
        });
        var principal = new ClaimsPrincipal(identity);

        _mockContext.Setup(x => x.ConnectionId).Returns("test-connection-id");
        _mockContext.Setup(x => x.User).Returns(principal);
        _mockConnection.Setup(x => x.RemoteIpAddress).Returns(IPAddress.Parse("127.0.0.1"));
        _mockHttpContext.Setup(x => x.Connection).Returns(_mockConnection.Object);

        _mockClients.Setup(x => x.Caller).Returns(_mockCaller.Object);
        _mockClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_mockGroup.Object);

    }


    [Test]
    public async Task OnDisconnectedAsync_WithoutException_ShouldLogInformation()
    {
        // Act
        await _chatHub.OnDisconnectedAsync(null);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User testuser disconnected from ChatHub")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public async Task OnDisconnectedAsync_WithException_ShouldLogWarning()
    {
        // Arrange
        var exception = new Exception("Test exception");

        // Act
        await _chatHub.OnDisconnectedAsync(exception);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString().Contains("User testuser disconnected from ChatHub with exception")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public async Task JoinRoom_ShouldAddToGroupAndLoadMessages()
    {
        // Arrange
        var roomId = Guid.NewGuid().ToString();
        var roomGuid = Guid.Parse(roomId);
        _dbContext.Messages.AddRange(
            new Message
            {
                Id = Guid.NewGuid(), ChatRoomId = roomGuid, Content = "m1",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3), UserName = "u"
            },
            new Message
            {
                Id = Guid.NewGuid(), ChatRoomId = roomGuid, Content = "m2",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2), UserName = "u"
            },
            new Message
            {
                Id = Guid.NewGuid(), ChatRoomId = roomGuid, Content = "m3",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1), UserName = "u"
            }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        await _chatHub.JoinRoom(roomId);

        // Assert
        _mockGroups.Verify(
            x => x.AddToGroupAsync("test-connection-id", roomId, It.IsAny<CancellationToken>()),
            Times.Once);


    }

    [Test]
    public async Task SendMessage_WithRegularMessage_ShouldSaveAndBroadcast()
    {
        // Arrange
        var roomId = Guid.NewGuid().ToString();
        var content = "Hello world!";
        _mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);

        // Act
        await _chatHub.SendMessage(roomId, content);

        // Assert
        Assert.That(_dbContext.Messages.Count(), Is.EqualTo(1));
        var msg = await _dbContext.Messages.AsNoTracking().FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(msg.Content, Is.EqualTo(content));
            Assert.That(msg.IsBotMessage, Is.False);
            Assert.That(msg.ChatRoomId, Is.EqualTo(Guid.Parse(roomId)));
            Assert.That(msg.UserName, Is.EqualTo(_testUser.UserName));
        });
    }

    [Test]
    public async Task SendMessage_WithInvalidStockCommand_ShouldSendBotMessage()
    {
        // Arrange
        var roomId = Guid.NewGuid().ToString();
        var content = "/stock"; // Invalid - no stock code
        _mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);

        // Act
        await _chatHub.SendMessage(roomId, content);

        // Assert
        Assert.That(_dbContext.Messages.Count(), Is.EqualTo(1));
        var msg = await _dbContext.Messages.AsNoTracking().FirstAsync();
        Assert.That(msg.IsBotMessage, Is.True);
    }

    [Test]
    public async Task SendMessage_WhenUserNotFound_ShouldReturnEarly()
    {
        // Arrange
        var roomId = Guid.NewGuid().ToString();
        var content = "Hello world!";
        _mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((AppUser)null);

        // Act
        await _chatHub.SendMessage(roomId, content);

        // Assert
        Assert.That(_dbContext.Messages.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task SendBotMessage_ShouldSaveAndBroadcastBotMessage()
    {
        // Arrange
        var roomId = Guid.NewGuid().ToString();
        var content = "Stock price: AAPL $150.00";
        var botUserName = "StockBot";

        // Act
        await _chatHub.SendBotMessage(roomId, content, botUserName);

        // Assert
        Assert.That(_dbContext.Messages.Count(), Is.EqualTo(1));
        var msg = await _dbContext.Messages.AsNoTracking().FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(msg.IsBotMessage, Is.True);
            Assert.That(msg.UserName, Is.EqualTo(botUserName));
            Assert.That(msg.Content, Is.EqualTo(content));
            Assert.That(msg.UserId, Is.Null);
        });
    }

    [Test]
    public void SendBotMessage_OnException_ShouldLogErrorAndRethrow()
    {
        // Arrange
        var roomId = Guid.NewGuid().ToString();
        var content = "Test bot message";
        var exception = new Exception("Test exception");
        
        // Force an exception by disposing the context
        _dbContext.Dispose();

        // Act & Assert
        var thrownException = Assert.ThrowsAsync<ObjectDisposedException>(() => _chatHub.SendBotMessage(roomId, content));
        Assert.That(thrownException, Is.Not.Null);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString().Contains($"Failed to send bot message from StockBot to room {roomId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [TearDown]
    public void TearDown()
    {
        (_chatHub as IDisposable)?.Dispose();
        _dbContext?.Dispose();
        _chatHub = null;

    }
}