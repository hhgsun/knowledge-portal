using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KnowledgePortal.Api.Auth;

/// <summary>
/// Rejects requests authenticated via API key. Only session (JWT) auth is allowed.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireSessionAuthAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.User.GetSource() == "api-key")
        {
            context.Result = new ObjectResult(new { error = "This endpoint requires session authentication" })
            {
                StatusCode = 403
            };
        }
    }
}
