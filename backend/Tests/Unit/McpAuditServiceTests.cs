using System.Text.Json;
using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Tests.Unit;

public class McpAuditServiceTests
{
    [Fact]
    public void SummarizeArguments_DoesNotExposeRawValues()
    {
        const string secret = "kp_supersecretcredential123";
        var arguments = JsonSerializer.SerializeToElement(new
        {
            query = "confidential project query",
            token = secret,
            include_content = true,
            limit = 10
        });

        var summary = McpAuditService.SummarizeArguments(arguments);

        Assert.Contains("query(string:length=", summary);
        Assert.Contains("token(string:length=", summary);
        Assert.Contains("include_content(boolean)", summary);
        Assert.Contains("limit(number)", summary);
        Assert.DoesNotContain(secret, summary);
        Assert.DoesNotContain("confidential project query", summary);
    }
}
