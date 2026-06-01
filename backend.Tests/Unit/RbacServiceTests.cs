using KnowledgePortal.Api.Auth;

namespace backend.Tests.Unit;

public class RbacServiceTests
{
    [Theory]
    [InlineData("admin", Permissions.ArticlesCreate, true)]
    [InlineData("admin", Permissions.UsersManage, true)]
    [InlineData("admin", Permissions.ApiKeysManage, true)]
    [InlineData("editor", Permissions.ArticlesCreate, true)]
    [InlineData("editor", Permissions.ArticlesPublish, true)]
    [InlineData("editor", Permissions.UsersManage, false)]
    [InlineData("editor", Permissions.ApiKeysManage, false)]
    [InlineData("viewer", Permissions.ArticlesCreate, true)]
    [InlineData("viewer", Permissions.ArticlesEditOwn, true)]
    [InlineData("viewer", Permissions.ArticlesPublish, false)]
    [InlineData("viewer", Permissions.TagsManage, false)]
    [InlineData("viewer", Permissions.UsersManage, false)]
    [InlineData("viewer", Permissions.AnalyticsView, false)]
    [InlineData("unknown_role", Permissions.ArticlesCreate, false)]
    [InlineData("", Permissions.ArticlesCreate, false)]
    public void HasPermission_ReturnsExpectedResult(string role, string permission, bool expected)
    {
        var result = RbacService.HasPermission(role, permission);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AllPermissionConstants_AreUsedInRoleMatrix()
    {
        // Verify that every permission constant is assigned to at least one role
        var allPermissions = new[]
        {
            Permissions.ArticlesCreate, Permissions.ArticlesEditOwn, Permissions.ArticlesEditAny,
            Permissions.ArticlesDeleteOwn, Permissions.ArticlesDeleteAny,
            Permissions.ArticlesPublish, Permissions.ArticlesArchive, Permissions.ArticlesApprove,
            Permissions.TagsManage, Permissions.UsersManage, Permissions.AnalyticsView, Permissions.ApiKeysManage
        };

        foreach (var permission in allPermissions)
        {
            // Admin should have all permissions
            Assert.True(RbacService.HasPermission("admin", permission),
                $"Admin should have permission: {permission}");
        }
    }
}
