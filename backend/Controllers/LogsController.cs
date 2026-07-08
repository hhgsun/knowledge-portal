using KnowledgePortal.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/logs")]
[Authorize]
[RequirePermission(Permissions.UsersManage)]
[RequireSessionAuth]
public class LogsController(IConfiguration configuration) : ControllerBase
{
    private string GetLogsDirectory()
    {
        var basePath = configuration["Logging:FilePath"] ?? "../data/logs";
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), basePath));
    }

    [HttpGet]
    public IActionResult GetLogFiles()
    {
        var logsDir = GetLogsDirectory();
        if (!Directory.Exists(logsDir))
            return Ok(new { files = Array.Empty<object>(), total = 0 });

        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var files = Directory.GetFiles(logsDir, "log_*.log")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name)
            .Select(f => new
            {
                fileName = f.Name,
                sizeBytes = f.Length,
                createdAt = f.CreationTimeUtc.ToString("o"),
                lastModifiedAt = f.LastWriteTimeUtc.ToString("o"),
                isToday = f.Name == $"log_{today}.log",
                canDelete = f.Name != $"log_{today}.log"
            })
            .ToList();

        return Ok(new { files, total = files.Count });
    }

    [HttpGet("{fileName}")]
    public IActionResult GetLogContent(string fileName, [FromQuery] int? tail)
    {
        if (!IsValidLogFileName(fileName))
            return BadRequest(new { error = "Invalid log file name" });

        var logsDir = GetLogsDirectory();
        var filePath = Path.Combine(logsDir, fileName);

        if (!Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(logsDir)))
            return BadRequest(new { error = "Invalid file path" });

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { error = "Log file not found" });

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var allLines = reader.ReadToEnd().Split('\n');

        var lines = tail.HasValue && tail.Value > 0
            ? allLines.TakeLast(tail.Value).ToArray()
            : allLines;

        return Ok(new
        {
            fileName,
            totalLines = allLines.Length,
            returnedLines = lines.Length,
            content = string.Join('\n', lines)
        });
    }

    [HttpDelete("{fileName}")]
    public IActionResult DeleteLogFile(string fileName)
    {
        if (!IsValidLogFileName(fileName))
            return BadRequest(new { error = "Invalid log file name" });

        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        if (fileName == $"log_{today}.log")
            return BadRequest(new { error = "Cannot delete today's log file" });

        var logsDir = GetLogsDirectory();
        var filePath = Path.Combine(logsDir, fileName);

        if (!Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(logsDir)))
            return BadRequest(new { error = "Invalid file path" });

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { error = "Log file not found" });

        System.IO.File.Delete(filePath);
        return Ok(new { message = $"Log file '{fileName}' deleted successfully" });
    }

    private static bool IsValidLogFileName(string fileName)
    {
        // Only allow log_YYYYMMDD.log pattern
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (!fileName.StartsWith("log_") || !fileName.EndsWith(".log"))
            return false;

        var datePart = fileName[4..^4]; // Remove "log_" prefix and ".log" suffix
        return datePart.Length == 8 && datePart.All(char.IsDigit);
    }
}
