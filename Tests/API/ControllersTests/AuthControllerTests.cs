using System.Net;
using System.Security.Claims;
using API.Controllers.Auth;
using API.Repositories.AppDbContext.Entites;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Tests.API.ControllersTests
{
    [TestFixture]
    public class AuthControllerTests
    {
        private static Mock<UserManager<AppUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<AppUser>>();
            return new Mock<UserManager<AppUser>>(
                store.Object,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<AppUser>(),
                Array.Empty<IUserValidator<AppUser>>(),
                Array.Empty<IPasswordValidator<AppUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null, // services
                new Mock<ILogger<UserManager<AppUser>>>().Object
            );
        }

        private static Mock<SignInManager<AppUser>> MockSignInManager(UserManager<AppUser> userManager)
        {
            var contextAccessor = new Mock<IHttpContextAccessor>();
            contextAccessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());

            var claimsFactory = new Mock<IUserClaimsPrincipalFactory<AppUser>>();

            return new Mock<SignInManager<AppUser>>(
                userManager,
                contextAccessor.Object,
                claimsFactory.Object,
                Options.Create(new IdentityOptions()),
                new Mock<ILogger<SignInManager<AppUser>>>().Object,
                new Mock<IAuthenticationSchemeProvider>().Object,
                new Mock<IUserConfirmation<AppUser>>().Object
            );
        }

        private static DefaultHttpContext MakeHttpContext(string ip = "127.0.0.1", string? userName = null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            if (!string.IsNullOrWhiteSpace(userName))
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, userName!)
                }, "TestAuth");
                ctx.User = new ClaimsPrincipal(identity);
            }

            return ctx;
        }

        private static AuthController CreateController(
            Mock<UserManager<AppUser>> um,
            Mock<SignInManager<AppUser>> sm,
            string ip = "127.0.0.1",
            string? userName = null)
        {
            var controller = new AuthController(um.Object, sm.Object, NullLogger<AuthController>.Instance);
            controller.ControllerContext.HttpContext = MakeHttpContext(ip, userName);
            return controller;
        }

        [Test]
        public async Task Register_ReturnsOk_OnSuccess()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);

            um.Setup(m => m.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Success);
            // Explicitly include the optional authenticationMethod parameter to avoid expression-tree optional args
            sm.Setup(m => m.SignInAsync(It.IsAny<AppUser>(), false, null))
                .Returns(Task.CompletedTask);

            var sut = CreateController(um, sm);
            var req = new RegisterRequest { UserName = "alice", Password = "P@ssw0rd!" };

            var result = await sut.Register(req);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            um.Verify(m => m.CreateAsync(It.Is<AppUser>(u => u.UserName == "alice" && u.Email == "alice@local"),
                "P@ssw0rd!"), Times.Once);
            // Verify with explicit third parameter
            sm.Verify(m => m.SignInAsync(It.Is<AppUser>(u => u.UserName == "alice"), false, null), Times.Once);
        }

        [Test]
        public async Task Register_ReturnsBadRequest_OnIdentityErrors()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);

            um.Setup(m => m.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "weak password" }));
            var sut = CreateController(um, sm);

            var result = await sut.Register(new RegisterRequest { UserName = "bob", Password = "123" });

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            var bad = (BadRequestObjectResult)result;
            var errors = (bad.Value as System.Collections.IEnumerable)?.Cast<object>().ToList();
            Assert.That(errors, Is.Not.Null);
            // Avoid StringComparison overload inside expression by normalizing case
            var hasWeak = errors!.Any(e =>
                (e?.ToString() ?? string.Empty).ToLowerInvariant().Contains("weak password"));
            Assert.That(hasWeak, Is.True);
            sm.Verify(m => m.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        }

        [Test]
        public async Task Login_ReturnsUnauthorized_WhenUserNotFound()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);

            um.Setup(m => m.FindByNameAsync("nobody")).ReturnsAsync((AppUser?)null);

            var sut = CreateController(um, sm, ip: "10.0.0.7");
            var result = await sut.Login(new LoginRequest { UserName = "nobody", Password = "irrelevant" });

            Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
        }

        [Test]
        public async Task Login_ReturnsUnauthorized_OnInvalidPassword()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);

            var user = new AppUser { UserName = "charlie" };
            um.Setup(m => m.FindByNameAsync("charlie")).ReturnsAsync(user);
            sm.Setup(m => m.CheckPasswordSignInAsync(user, "bad", false))
                .ReturnsAsync(SignInResult.Failed);

            var sut = CreateController(um, sm);
            var result = await sut.Login(new LoginRequest { UserName = "charlie", Password = "bad" });

            Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
            sm.Verify(m => m.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        }

        [Test]
        public async Task Login_ReturnsOk_OnSuccess_AndBlocksSecondSession()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);

            var user = new AppUser { UserName = "dora" };
            um.Setup(m => m.FindByNameAsync("dora")).ReturnsAsync(user);
            sm.Setup(m => m.CheckPasswordSignInAsync(user, "good", false))
                .ReturnsAsync(SignInResult.Success);
            // Explicit 3-arg overload to avoid optional args in expression trees
            sm.Setup(m => m.SignInAsync(user, false, null)).Returns(Task.CompletedTask);

            var sut = CreateController(um, sm, ip: "192.168.1.10");

            // First login succeeds
            var first = await sut.Login(new LoginRequest { UserName = "dora", Password = "good" });
            Assert.That(first, Is.InstanceOf<OkObjectResult>());

            // Second login blocked due to active session
            var second = await sut.Login(new LoginRequest { UserName = "dora", Password = "good" });
            Assert.That(second, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task Logout_RemovesActiveSession_AndReturnsOk()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);

            var user = new AppUser { UserName = "eve" };
            um.Setup(m => m.FindByNameAsync("eve")).ReturnsAsync(user);
            sm.Setup(m => m.CheckPasswordSignInAsync(user, "pw", false))
                .ReturnsAsync(SignInResult.Success);
            // Use explicit overload with authenticationMethod
            sm.Setup(m => m.SignInAsync(user, false, null)).Returns(Task.CompletedTask);
            sm.Setup(m => m.SignOutAsync()).Returns(Task.CompletedTask);

            // Login to create active session
            var sut = CreateController(um, sm, userName: null);
            var ok = await sut.Login(new LoginRequest { UserName = "eve", Password = "pw" });
            Assert.That(ok, Is.InstanceOf<OkObjectResult>());

            // Now logout as authenticated user "eve"
            sut.ControllerContext.HttpContext = MakeHttpContext("127.0.0.1", "eve");
            var logout = await sut.Logout();
            Assert.That(logout, Is.InstanceOf<OkResult>());

            // Login should succeed again after logout (session removed)
            var relogin = await sut.Login(new LoginRequest { UserName = "eve", Password = "pw" });
            Assert.That(relogin, Is.InstanceOf<OkObjectResult>());
        }

        [Test]
        public void Me_ReturnsOk_WhenAuthenticated()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);
            var sut = CreateController(um, sm, userName: "frank");

            var res = sut.Me();

            Assert.That(res, Is.InstanceOf<OkObjectResult>());
            var ok = (OkObjectResult)res;
            Assert.That(ok.Value?.ToString(), Does.Contain("frank"));
        }

        [Test]
        public void Me_ReturnsUnauthorized_WhenAnonymous()
        {
            var um = MockUserManager();
            var sm = MockSignInManager(um.Object);
            var sut = CreateController(um, sm, userName: null);

            var res = sut.Me();

            Assert.That(res, Is.InstanceOf<UnauthorizedResult>());
        }
    }
}