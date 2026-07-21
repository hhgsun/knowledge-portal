using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace KnowledgePortal.Api.Auth;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePermissionAttribute(string permission) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Unauthorized" });
            return;
        }

        // Principal-aware check — applies the API-key permission cap (editor max, no deletes)
        if (!RbacService.HasPermission(user, permission))
        {
            context.Result = new ObjectResult(new { error = "Forbidden" }) { StatusCode = 403 };
        }
    }
}
