using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Service-layer failure carrying the HTTP status it maps to.
/// Controllers convert it with <see cref="ToActionResult"/> to keep the standard { error } shape.
/// </summary>
public record ServiceError(int StatusCode, string Message)
{
    public IActionResult ToActionResult() => new ObjectResult(new { error = Message }) { StatusCode = StatusCode };
}
