using System.Security.Claims;
using KnowledgePortal.Api.Auth;

namespace KnowledgePortal.Api.Tests.Unit;

/// <summary>Principal-aware RBAC: API-key permission cap ("editor minus delete").</summary>
public class ApiKeyRbacTests
{
    private static ClaimsPrincipal Principal(string role, string source) =>
        new(new ClaimsIdentity(
            [new Claim("role", role), new Claim("source", source)],
            authenticationType: "Test"));

    [Theory]
    [InlineData("admin", "editor")]
    [InlineData("editor", "editor")]
    [InlineData("viewer", "viewer")]
    public void GetEffectiveRole_ApiKey_CapsAdminAtEditor(string ownerRole, string expected)
    {
        Assert.Equal(expected, RbacService.GetEffectiveRole(Principal(ownerRole, "api-key")));
    }

    [Fact]
    public void GetEffectiveRole_Session_Unchanged()
    {
        Assert.Equal("admin", RbacService.GetEffectiveRole(Principal("admin", "session")));
    }

    [Fact]
    public void ApiKey_ArticleDeletePermission_AlwaysDenied()
    {
        Assert.False(RbacService.HasPermission(Principal("admin", "api-key"), Permissions.ArticlesDeleteAny));
        Assert.False(RbacService.HasPermission(Principal("editor", "api-key"), Permissions.ArticlesDeleteAny));
        Assert.False(RbacService.HasPermission(Principal("viewer", "api-key"), Permissions.ArticlesDeleteAny));
    }

    [Theory]
    [InlineData(Permissions.UsersManage)]
    [InlineData(Permissions.ArticlesEditAny)]
    [InlineData(Permissions.ApiKeysManageAny)]
    public void AdminOwnedApiKey_LosesAdminOnlyPermissions(string permission)
    {
        Assert.True(RbacService.HasPermission(Principal("admin", "session"), permission));
        Assert.False(RbacService.HasPermission(Principal("admin", "api-key"), permission));
    }

    [Theory]
    [InlineData(Permissions.ArticlesCreate)]
    [InlineData(Permissions.ArticlesEditOwn)]
    [InlineData(Permissions.ArticlesPublish)]
    [InlineData(Permissions.ArticlesArchive)]
    [InlineData(Permissions.TagsManage)]
    public void AdminOwnedApiKey_KeepsEditorPermissions(string permission)
    {
        Assert.True(RbacService.HasPermission(Principal("admin", "api-key"), permission));
    }

    [Fact]
    public void SessionPrincipals_ArticleDeleteIsAdminOnly()
    {
        Assert.True(RbacService.HasPermission(Principal("admin", "session"), Permissions.ArticlesDeleteAny));
        Assert.False(RbacService.HasPermission(Principal("editor", "session"), Permissions.ArticlesDeleteAny));
        Assert.False(RbacService.HasPermission(Principal("viewer", "session"), Permissions.ArticlesDeleteAny));
    }

    [Fact]
    public void CanEditArticle_ApiKey_FollowsEffectiveRole()
    {
        // Admin key capped to editor: edit_own only — cannot edit others' articles
        Assert.True(RbacService.CanEditArticle(Principal("admin", "api-key"), isOwner: true));
        Assert.False(RbacService.CanEditArticle(Principal("admin", "api-key"), isOwner: false));
        Assert.True(RbacService.CanEditArticle(Principal("admin", "session"), isOwner: false));
    }

    [Fact]
    public void CanViewArticle_ApiKey_EditorScope()
    {
        // Editor-capped keys see everything (like editors); viewer keys keep viewer scope
        Assert.True(RbacService.CanViewArticle(Principal("admin", "api-key"), "draft", isOwner: false));
        Assert.False(RbacService.CanViewArticle(Principal("viewer", "api-key"), "draft", isOwner: false));
        Assert.True(RbacService.CanViewArticle(Principal("viewer", "api-key"), "published", isOwner: false));
    }
}
