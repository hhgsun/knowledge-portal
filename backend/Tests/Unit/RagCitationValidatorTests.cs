using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagCitationValidatorTests
{
    private static readonly RagEvidence Evidence = new("S1", "a1", "VPN Rehberi", "vpn-rehberi",
        "article", null, null, null, "VPN talebi portal üzerinden açılır.", .9);

    [Fact]
    public void Validate_AcceptsClaimsBoundToKnownEvidence()
    {
        const string raw = """{"answer":"VPN talebi portal üzerinden açılır [S1].","claims":[{"text":"VPN talebi portal üzerinden açılır.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("citations_verified", result.GroundingStatus);
        Assert.Equal(1, result.CitationCoverage);
        Assert.Equal(["S1"], result.Claims.Single().SourceIds);
    }

    [Fact]
    public void Validate_RemovesInventedCitationAndDoesNotTrustUnknownSourceId()
    {
        const string raw = """{"answer":"Uydurma bilgi [S99].","claims":[{"text":"Uydurma bilgi","sourceIds":["S99"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("failed", result.GroundingStatus);
        Assert.Equal(0, result.CitationCoverage);
        Assert.Empty(result.Claims.Single().SourceIds);
        Assert.DoesNotContain("[S99]", result.Answer);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Validate_MalformedModelOutput_IsExplicitlyUnverified()
    {
        var result = RagCitationValidator.Validate("serbest metin cevap", [Evidence]);

        Assert.Equal("unverified", result.GroundingStatus);
        Assert.Equal("serbest metin cevap", result.Answer);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public void Validate_ParsesJsonCodeFence()
    {
        const string raw = """
            ```json
            {"answer":"Bilgi yok.","claims":[],"insufficientContext":true}
            ```
            """;

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.True(result.InsufficientContext);
        Assert.Equal("insufficient_context", result.GroundingStatus);
        Assert.Equal(1, result.CitationCoverage);
    }
}
