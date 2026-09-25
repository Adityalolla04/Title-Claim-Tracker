using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TitleClaimTracker.Controllers;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Identity;
using TitleClaimTracker.Infrastructure.Security;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class AuthControllerTests
{
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

        var controller = new AuthController(userManager.Object, signInManager.Object, roleManager.Object, antiforgery.Object);

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
        var controller = new AuthController(userManager.Object, signInManager.Object, roleManager.Object, new Mock<IAntiforgery>().Object);

        var result = await controller.Login(new LoginRequest("inactive@example.com", "password"));

        Assert.IsType<UnauthorizedResult>(result.Result);
        signInManager.Verify(manager => manager.PasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
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
        var controller = new AuthController(userManager.Object, signInManager.Object, roleManager.Object, new Mock<IAntiforgery>().Object);

        var result = await controller.Me();

        Assert.IsType<UnauthorizedResult>(result.Result);
        signInManager.Verify(manager => manager.SignOutAsync(), Times.Once);
    }

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