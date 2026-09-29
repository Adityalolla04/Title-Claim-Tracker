using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TitleClaimTracker.Controllers;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Identity;
using TitleClaimTracker.Infrastructure.Security;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class AuthControllerTests
{
    [Fact]
    public void CsrfEndpoint_SetsAngularRequestTokenSeparatelyFromServerCookieToken()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(service => service.GetAndStoreTokens(It.IsAny<HttpContext>()))
            .Returns(new AntiforgeryTokenSet("request-token", "server-cookie-token", "__RequestVerificationToken", "X-XSRF-TOKEN"));
        var controller = CreateController(userManager, signInManager, roleManager, antiforgery.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = controller.GetCsrfToken();

        Assert.IsType<NoContentResult>(result);
        var setCookie = Assert.Single(controller.Response.Headers.SetCookie.Where(value => value!.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)));
        Assert.Contains("request-token", setCookie);
        Assert.DoesNotContain("server-cookie-token", setCookie);
    }

    [Fact]
    public async Task PublicRegistration_AssignsUserRole_AndNeverAdminRole()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var antiforgery = new Mock<IAntiforgery>();
        var createdUser = new ApplicationUser { Id = "user-1", Email = "person@example.com", UserName = "person@example.com", IsActive = true };

        userManager.Setup(manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), "A-very-strong-password!1"))
            .Callback<ApplicationUser, string>((user, _) =>
            {
                user.Id = createdUser.Id;
                user.Email = createdUser.Email;
                user.UserName = createdUser.UserName;
            })
            .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(manager => manager.AddToRoleAsync(It.IsAny<ApplicationUser>(), AppRoles.User)).ReturnsAsync(IdentityResult.Success);
        userManager.Setup(manager => manager.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync([AppRoles.User]);
        roleManager.Setup(manager => manager.RoleExistsAsync(AppRoles.User)).ReturnsAsync(true);
        signInManager.Setup(manager => manager.SignInAsync(It.IsAny<ApplicationUser>(), false, It.IsAny<string?>())).Returns(Task.CompletedTask);

        var controller = CreateController(userManager, signInManager, roleManager, antiforgery.Object);

        var result = await controller.Register(
            new RegisterRequest("person@example.com", "A-very-strong-password!1", "Person"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var account = Assert.IsType<CurrentAccountDto>(response.Value);
        Assert.Equal("user-1", account.Id);
        Assert.Equal([AppRoles.User], account.Roles);
        userManager.Verify(manager => manager.AddToRoleAsync(It.IsAny<ApplicationUser>(), AppRoles.User), Times.Once);
        userManager.Verify(manager => manager.AddToRoleAsync(It.IsAny<ApplicationUser>(), AppRoles.Administrator), Times.Never);
    }

    [Fact]
    public async Task Login_RejectsInactiveAccount_WithoutCreatingSession()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var inactiveUser = new ApplicationUser { Id = "inactive", Email = "inactive@example.com", UserName = "inactive@example.com", IsActive = false };
        userManager.Setup(manager => manager.FindByEmailAsync("inactive@example.com")).ReturnsAsync(inactiveUser);
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.Login(new LoginRequest("inactive@example.com", "password"));

        Assert.IsType<UnauthorizedResult>(result.Result);
        signInManager.Verify(manager => manager.PasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Login_RejectsAdministratorAccount()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@example.com", UserName = "admin@example.com", IsActive = true };
        userManager.Setup(manager => manager.FindByEmailAsync(admin.Email)).ReturnsAsync(admin);
        userManager.Setup(manager => manager.IsInRoleAsync(admin, AppRoles.Administrator)).ReturnsAsync(true);
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.Login(new LoginRequest(admin.Email, "password"));

        Assert.IsType<UnauthorizedResult>(result.Result);
        signInManager.Verify(manager => manager.PasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task AdminLogin_RejectsAddressOutsideConfiguredAllowlist()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.AdminLogin(new LoginRequest("other@example.com", "password"));

        Assert.IsType<UnauthorizedResult>(result.Result);
        userManager.Verify(manager => manager.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        signInManager.Verify(manager => manager.PasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task AdminLogin_RequiresAdministratorRoleForConfiguredAddress()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var user = new ApplicationUser { Id = "user-1", Email = "admin@example.com", UserName = "admin@example.com", IsActive = true };
        userManager.Setup(manager => manager.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager.Setup(manager => manager.IsInRoleAsync(user, AppRoles.Administrator)).ReturnsAsync(false);
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.AdminLogin(new LoginRequest(user.Email, "password"));

        Assert.IsType<UnauthorizedResult>(result.Result);
        signInManager.Verify(manager => manager.PasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task AdminLogin_AllowsConfiguredAdministrator()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var user = new ApplicationUser { Id = "admin-1", Email = "admin@example.com", UserName = "admin@example.com", IsActive = true };
        userManager.Setup(manager => manager.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager.Setup(manager => manager.IsInRoleAsync(user, AppRoles.Administrator)).ReturnsAsync(true);
        userManager.Setup(manager => manager.GetRolesAsync(user)).ReturnsAsync([AppRoles.Administrator]);
        signInManager.Setup(manager => manager.PasswordSignInAsync(user, "password", false, true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.AdminLogin(new LoginRequest(user.Email, "password"));

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var account = Assert.IsType<CurrentAccountDto>(response.Value);
        Assert.Equal([AppRoles.Administrator], account.Roles);
    }

    [Fact]
    public async Task CurrentSession_RejectsInactiveAccount_AndSignsOut()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var inactiveUser = new ApplicationUser { Id = "inactive", Email = "inactive@example.com", UserName = "inactive@example.com", IsActive = false };
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(inactiveUser);
        signInManager.Setup(manager => manager.SignOutAsync()).Returns(Task.CompletedTask);
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.Me();

        Assert.IsType<UnauthorizedResult>(result.Result);
        signInManager.Verify(manager => manager.SignOutAsync(), Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_ReturnsSameGenericResponseForUnknownAddress()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        userManager.Setup(manager => manager.FindByEmailAsync("missing@example.com")).ReturnsAsync((ApplicationUser?)null);
        var sender = new Mock<IAccountEmailSender>();
        sender.SetupGet(service => service.IsConfigured).Returns(true);
        var controller = CreateController(userManager, signInManager, roleManager, emailSender: sender.Object);

        var result = await controller.ForgotPassword(new ForgotPasswordRequest("missing@example.com"), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("If an active account matches that email, password reset instructions will be sent.",
            response.Value!.GetType().GetProperty("message")!.GetValue(response.Value));
        sender.Verify(service => service.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_SendsResetLinkWithoutReturningToken()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var user = new ApplicationUser { Id = "user-1", Email = "person@example.com", UserName = "person@example.com", DisplayName = "Person", IsActive = true };
        userManager.Setup(manager => manager.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager.Setup(manager => manager.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("identity-token");
        var sender = new Mock<IAccountEmailSender>();
        sender.SetupGet(service => service.IsConfigured).Returns(true);
        sender.Setup(service => service.SendPasswordResetAsync(user.Email, user.DisplayName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = CreateController(userManager, signInManager, roleManager, emailSender: sender.Object);

        var result = await controller.ForgotPassword(new ForgotPasswordRequest(user.Email), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.DoesNotContain("identity-token", System.Text.Json.JsonSerializer.Serialize(response.Value));
        sender.Verify(service => service.SendPasswordResetAsync(user.Email, user.DisplayName,
            It.Is<string>(url => url.StartsWith("http://localhost:4200/reset-password?", StringComparison.Ordinal) && url.Contains("token=identity-token", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_AppliesIdentityResetToken()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var user = new ApplicationUser { Id = "user-1", Email = "person@example.com", IsActive = true };
        userManager.Setup(manager => manager.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager.Setup(manager => manager.ResetPasswordAsync(user, "identity-token", "A-very-strong-password!1"))
            .ReturnsAsync(IdentityResult.Success);
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.ResetPassword(new ResetPasswordRequest(user.Email, "identity-token", "A-very-strong-password!1"));

        Assert.IsType<OkObjectResult>(result);
        userManager.Verify(manager => manager.ResetPasswordAsync(user, "identity-token", "A-very-strong-password!1"), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_RejectsInvalidIdentityToken()
    {
        var userManager = CreateUserManager();
        var roleManager = CreateRoleManager();
        var signInManager = CreateSignInManager(userManager);
        var user = new ApplicationUser { Id = "user-1", Email = "person@example.com", IsActive = true };
        userManager.Setup(manager => manager.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager.Setup(manager => manager.ResetPasswordAsync(user, "expired-token", "A-very-strong-password!1"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }));
        var controller = CreateController(userManager, signInManager, roleManager);

        var result = await controller.ResetPassword(new ResetPasswordRequest(user.Email, "expired-token", "A-very-strong-password!1"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static AuthController CreateController(
        Mock<UserManager<ApplicationUser>> userManager,
        Mock<SignInManager<ApplicationUser>> signInManager,
        Mock<RoleManager<IdentityRole>> roleManager,
        IAntiforgery? antiforgery = null,
        IAccountEmailSender? emailSender = null) => new(
            userManager.Object,
            signInManager.Object,
            roleManager.Object,
            antiforgery ?? new Mock<IAntiforgery>().Object,
            emailSender ?? Mock.Of<IAccountEmailSender>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PasswordReset:ClientBaseUrl"] = "http://localhost:4200",
                ["Identity:AdminAccess:Email"] = "admin@example.com"
            }).Build(),
            Mock.Of<ILogger<AuthController>>());

    private static Mock<UserManager<ApplicationUser>> CreateUserManager() => new(
        Mock.Of<IUserStore<ApplicationUser>>(),
        null!, null!, null!, null!, null!, null!, null!, null!);

    private static Mock<RoleManager<IdentityRole>> CreateRoleManager() => new(
        Mock.Of<IRoleStore<IdentityRole>>(),
        null!, null!, null!, null!);

    private static Mock<SignInManager<ApplicationUser>> CreateSignInManager(Mock<UserManager<ApplicationUser>> userManager) => new(
        userManager.Object,
        Mock.Of<IHttpContextAccessor>(),
        Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
        Options.Create(new IdentityOptions()),
        Mock.Of<ILogger<SignInManager<ApplicationUser>>>(),
        Mock.Of<IAuthenticationSchemeProvider>(),
        Mock.Of<IUserConfirmation<ApplicationUser>>());
}