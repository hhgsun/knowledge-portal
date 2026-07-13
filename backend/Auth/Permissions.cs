namespace KnowledgePortal.Api.Auth;

/// <summary>
/// Permission string constants for RBAC checks.
/// Always use these instead of magic strings.
/// </summary>
public static class Permissions
{
    // Articles
    public const string ArticlesCreate = "articles:create";
    public const string ArticlesEditOwn = "articles:edit_own";
    public const string ArticlesEditAny = "articles:edit_any";
    public const string ArticlesDeleteOwn = "articles:delete_own";
    public const string ArticlesDeleteAny = "articles:delete_any";
    public const string ArticlesPublish = "articles:publish";
    public const string ArticlesArchive = "articles:archive";
    public const string ArticlesApprove = "articles:approve";

    // Tags
    public const string TagsManage = "tags:manage";

    // Users
    public const string UsersManage = "users:manage";

    // Analytics
    public const string AnalyticsView = "analytics:view";

    // API Keys
    public const string ApiKeysManage = "api_keys:manage";
    public const string ApiKeysManageAny = "api_keys:manage_any";
}
