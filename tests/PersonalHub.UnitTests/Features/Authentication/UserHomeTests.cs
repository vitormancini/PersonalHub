using System.Security.Claims;
using PersonalHub.Web.Features.Authentication;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.UnitTests.Features.Authentication;

public class UserHomeTests
{
    [Theory]
    [InlineData(new[] { AppRoles.Personal }, "/personal")]
    [InlineData(new[] { AppRoles.Student }, "/aluno")]
    [InlineData(new[] { AppRoles.Student, AppRoles.Personal }, "/personal")]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "Admin" }, null)]
    public void ForRoles_ReturnsHomeByRole(string[] roles, string? expected)
    {
        Assert.Equal(expected, UserHome.ForRoles(roles));
    }

    [Fact]
    public void For_AnonymousUser_ReturnsNull()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Null(UserHome.For(anonymous));
    }

    [Fact]
    public void For_AuthenticatedStudent_ReturnsStudentHome()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.Student)], authenticationType: "Test");

        Assert.Equal(UserHome.Student, UserHome.For(new ClaimsPrincipal(identity)));
    }
}
