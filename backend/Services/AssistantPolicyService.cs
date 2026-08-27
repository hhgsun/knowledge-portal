using System.Security.Claims;
using KnowledgePortal.Api.Auth;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantPolicyDecision(bool Allowed, string? Error = null);

/// <summary>
/// Server-owned route authorization. Router/model confidence never grants a permission.
/// The first release exposes read-only routes only; analytics retains its existing
/// analytics:view and session-only contract.
/// </summary>
public sealed class AssistantPolicyService
{
    public AssistantPolicyDecision Authorize(AssistantRoute route, ClaimsPrincipal principal)
    {
        if (route != AssistantRoute.Analytics) return new(true);
        if (principal.GetSource() == "api-key")
            return new(false, "Analytics assistant requests require an interactive session.");
        if (!RbacService.HasPermission(principal, Permissions.AnalyticsView))
            return new(false, "You do not have permission to view analytics.");
        return new(true);
    }
}
