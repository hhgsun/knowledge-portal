namespace KnowledgePortal.Api.Auth;

public static class RbacService
{
    private static readonly Dictionary<string, HashSet<string>> RolePermissions = new()
    {
        ["admin"] =
        [
            "articles:create", "articles:edit_own", "articles:edit_any",
            "articles:delete_own", "articles:delete_any",
            "articles:publish", "articles:archive",
            "tags:manage", "users:manage", "analytics:view", "api_keys:manage"
        ],
        ["editor"] =
        [
            "articles:create", "articles:edit_own",
            "articles:delete_own", "articles:publish", "articles:archive",
            "tags:manage", "analytics:view"
        ],
        ["viewer"] = []
    };

    public static bool HasPermission(string role, string permission)
    {
        return RolePermissions.TryGetValue(role, out var permissions)
            && permissions.Contains(permission);
    }
}
