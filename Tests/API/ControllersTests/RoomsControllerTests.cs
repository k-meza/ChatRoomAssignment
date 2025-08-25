using System.Net;
using System.Security.Claims;
using API.Controllers.Rooms;
using API.Repositories.AppDbContext;
using API.Repositories.AppDbContext.Entites;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.API.ControllersTests
{
    [TestFixture]
    public class RoomsControllerTests
    {
        private static AppDbContext CreateDb(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .EnableSensitiveDataLogging()
                .Options;
            return new AppDbContext(options);
        }

        private static RoomsController CreateController(AppDbContext db, string? userName = "alice",
            string? ip = "127.0.0.1")
        {
            var controller = new RoomsController(db, NullLogger<RoomsController>.Instance);

            var httpCtx = new DefaultHttpContext
            {
                Connection = { RemoteIpAddress = IPAddress.Parse(ip ?? "127.0.0.1") }
            };

            if (!string.IsNullOrWhiteSpace(userName))
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, userName!)
                }, authenticationType: "TestAuth");

                httpCtx.User = new ClaimsPrincipal(identity);
            }

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpCtx
            };

            return controller;
        }

        [Test]
        public async Task GetRoom_ReturnsOrderedDtos()
        {
            // Arrange
            using var db = CreateDb(nameof(GetRoom_ReturnsOrderedDtos));
            db.ChatRooms.AddRange(
                new ChatRoom { Name = "Zulu" },
                new ChatRoom { Name = "Alpha" },
                new ChatRoom { Name = "Mike" }
            );
            await db.SaveChangesAsync();

            var sut = CreateController(db);

            // Act
            var result = await sut.GetRoom();

            // Assert
            var list = result.ToList();
            Assert.That(list, Has.Count.EqualTo(3));
            Assert.That(list.Select(r => r.Name).ToArray(), Is.EqualTo(new[] { "Alpha", "Mike", "Zulu" }));
            Assert.That(list.All(r => r.Id != Guid.Empty), Is.True, "Ids should be mapped from entities");
        }

        [Test]
        public async Task CreateRoom_ReturnsBadRequest_WhenNameIsNullOrWhitespace()
        {
            // Arrange
            using var db = CreateDb(nameof(CreateRoom_ReturnsBadRequest_WhenNameIsNullOrWhitespace));
            var sut = CreateController(db);

            // Act
            var r1 = await sut.CreateRoom(new CreateRoomRequest { Name = null! });
            var r2 = await sut.CreateRoom(new CreateRoomRequest { Name = "" });
            var r3 = await sut.CreateRoom(new CreateRoomRequest { Name = "   " });

            // Assert
            Assert.That(r1, Is.TypeOf<BadRequestObjectResult>());
            Assert.That(r2, Is.TypeOf<BadRequestObjectResult>());
            Assert.That(r3, Is.TypeOf<BadRequestObjectResult>());

            // Ensure nothing was added
            Assert.That(await db.ChatRooms.CountAsync(), Is.EqualTo(0));
        }

        [Test]
        public async Task CreateRoom_ReturnsConflict_WhenRoomAlreadyExists_CaseSensitiveByExactTrimmedMatch()
        {
            // Arrange
            using var db =
                CreateDb(nameof(CreateRoom_ReturnsConflict_WhenRoomAlreadyExists_CaseSensitiveByExactTrimmedMatch));
            db.ChatRooms.Add(new ChatRoom { Name = "General" });
            await db.SaveChangesAsync();

            var sut = CreateController(db);

            // Act
            var conflictSame = await sut.CreateRoom(new CreateRoomRequest { Name = "General" });
            var okDifferentCase =
                await sut.CreateRoom(new CreateRoomRequest
                    { Name = "general" }); // depends on DB collation; InMemory is case-sensitive by default
            var conflictTrimmed = await sut.CreateRoom(new CreateRoomRequest { Name = "  General  " });

            // Assert
            Assert.That(conflictSame, Is.TypeOf<ConflictObjectResult>());
            // For in-memory provider default string comparer is case-sensitive; "general" is different
            Assert.That(okDifferentCase, Is.TypeOf<OkObjectResult>());
            Assert.That(conflictTrimmed, Is.TypeOf<ConflictObjectResult>());

            // Final count: initial 1 + okDifferentCase 1 = 2
            Assert.That(await db.ChatRooms.CountAsync(), Is.EqualTo(2));
        }

        [Test]
        public async Task CreateRoom_CreatesRoom_AndReturnsDto()
        {
            // Arrange
            using var db = CreateDb(nameof(CreateRoom_CreatesRoom_AndReturnsDto));
            var sut = CreateController(db, userName: "bob", ip: "10.0.0.2");

            // Act
            var action = await sut.CreateRoom(new CreateRoomRequest { Name = "Developers" });

            // Assert
            var ok = action as OkObjectResult;
            Assert.That(ok, Is.Not.Null, "Expected OkObjectResult");

            var dto = ok!.Value as RoomDto;
            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.Name, Is.EqualTo("Developers"));
            Assert.That(dto.Id, Is.Not.EqualTo(Guid.Empty));

            var entity = await db.ChatRooms.SingleAsync(r => r.Name == "Developers");
            Assert.That(entity.Id, Is.EqualTo(dto.Id));
        }

        [Test]
        public async Task CreateRoom_TrimmedNameIsPersisted()
        {
            // Arrange
            using var db = CreateDb(nameof(CreateRoom_TrimmedNameIsPersisted));
            var sut = CreateController(db);

            // Act
            var action = await sut.CreateRoom(new CreateRoomRequest { Name = "   Lounge  " });

            // Assert
            var ok = action as OkObjectResult;
            Assert.That(ok, Is.Not.Null);

            var dto = (RoomDto)ok!.Value!;
            Assert.That(dto.Name, Is.EqualTo("Lounge"));

            var entity = await db.ChatRooms.SingleAsync();
            Assert.That(entity.Name, Is.EqualTo("Lounge"));
        }

        [Test]
        public async Task GetRoom_WhenNoRooms_ReturnsEmptyList()
        {
            // Arrange
            using var db = CreateDb(nameof(GetRoom_WhenNoRooms_ReturnsEmptyList));
            var sut = CreateController(db);

            // Act
            var result = await sut.GetRoom();

            // Assert
            Assert.That(result, Is.Empty);
        }
    }
}