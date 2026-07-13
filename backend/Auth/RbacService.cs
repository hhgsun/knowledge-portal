namespace KnowledgePortal.Api.Auth;

public static class RbacService
{
    private static readonly Dictionary<string, HashSet<string>> RolePermissions = new()
    {
        ["admin"] =
        [
            Permissions.ArticlesCreate, Permissions.ArticlesEditOwn, Permissions.ArticlesEditAny,
            Permissions.ArticlesDeleteOwn, Permissions.ArticlesDeleteAny,
            Permissions.ArticlesPublish, Permissions.ArticlesArchive, Permissions.ArticlesApprove,
            Permissions.TagsManage, Permissions.UsersManage, Permissions.AnalyticsView,
            Permissions.ApiKeysManage, Permissions.ApiKeysManageAny
        ],
        ["editor"] =
        [
            Permissions.ArticlesCreate, Permissions.ArticlesEditOwn,
            Permissions.ArticlesDeleteOwn, Permissions.ArticlesPublish, Permissions.ArticlesArchive, Permissions.ArticlesApprove,
            Permissions.TagsManage, Permissions.AnalyticsView, Permissions.ApiKeysManage
        ],
        ["viewer"] =
        [
            Permissions.ArticlesCreate, Permissions.ArticlesEditOwn, Permissions.ArticlesDeleteOwn,
            Permissions.ApiKeysManage
        ]
    };

    public static bool HasPermission(string role, string permission)
    {
        return RolePermissions.TryGetValue(role, out var permissions)
            && permissions.Contains(permission);
    }

    public static bool CanEditArticle(string role, bool isOwner) =>
        HasPermission(role, Permissions.ArticlesEditAny)
        || (isOwner && HasPermission(role, Permissions.ArticlesEditOwn));

    public static bool CanDeleteArticle(string role, bool isOwner) =>
        HasPermission(role, Permissions.ArticlesDeleteAny)
        || (isOwner && HasPermission(role, Permissions.ArticlesDeleteOwn));

    /// <summary>Viewers only see published articles or their own; other roles see everything.</summary>
    public static bool CanViewArticle(string role, string articleStatus, bool isOwner) =>
        role != "viewer" || articleStatus == "published" || isOwner;
}
