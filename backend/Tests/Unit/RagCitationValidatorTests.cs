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

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.Equal(1, result.CitationCoverage);
        Assert.Equal(1, result.ClaimSupportCoverage);
        Assert.Equal(["S1"], result.Claims.Single().SourceIds);
        Assert.Equal("VPN talebi portal üzerinden açılır. [S1]", result.Answer);
    }

    [Fact]
    public void Validate_RemovesInventedCitationAndDoesNotTrustUnknownSourceId()
    {
        const string raw = """{"answer":"Uydurma bilgi [S99].","claims":[{"text":"Uydurma bilgi","sourceIds":["S99"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.Equal(0, result.CitationCoverage);
        Assert.Empty(result.Claims);
        Assert.DoesNotContain("[S99]", result.Answer);
        Assert.True(result.InsufficientContext);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Validate_MalformedModelOutput_IsRejectedFailClosed()
    {
        var result = RagCitationValidator.Validate("serbest metin cevap", [Evidence]);

        Assert.Equal("rejected_unstructured", result.GroundingStatus);
        Assert.DoesNotContain("serbest metin cevap", result.Answer);
        Assert.True(result.InsufficientContext);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public void Validate_SeparatesValidCitationIdFromUnsupportedClaim()
    {
        const string raw = """{"answer":"Sunucular ayda bir kapatılır [S1].","claims":[{"text":"Sunucular ayda bir kapatılır.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal(1, result.CitationCoverage);
        Assert.Equal(0, result.ClaimSupportCoverage);
        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.Empty(result.Claims);
        Assert.True(result.InsufficientContext);
        Assert.NotEmpty(result.Warnings);
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

    [Fact]
    public void Validate_ParsesCompleteJsonObjectInsideModelWrapper()
    {
        const string raw = """
            <think>Yanıtı yalnızca verilen kaynağa dayandır.</think>
            Sonuç:
            {"answer":"VPN talebi portal üzerinden açılır [S1].","claims":[{"text":"VPN talebi portal üzerinden açılır.","sourceIds":["S1"]}],"insufficientContext":false}
            """;

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.Equal("VPN talebi portal üzerinden açılır. [S1]", result.Answer);
    }

    [Fact]
    public void Validate_MissingRequiredContractField_IsRejectedFailClosed()
    {
        const string raw = """{"answer":"VPN talebi portal üzerinden açılır [S1].","claims":[]}""";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("rejected_unstructured", result.GroundingStatus);
        Assert.True(result.InsufficientContext);
    }

    [Fact]
    public void Validate_RecoversCitedPlainTextAndStillValidatesGrounding()
    {
        const string raw = """
            MCP yanıtı:
            - VPN talebi portal üzerinden açılır. [S1]
            """;

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.Equal("VPN talebi portal üzerinden açılır. [S1]", result.Answer);
        Assert.Contains(result.Warnings, x => x.Contains("cited text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CitedPlainTextWithInventedSourceStillFailsClosed()
    {
        const string raw = "VPN talebi portal üzerinden açılır. [S99]";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.True(result.InsufficientContext);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public void TryBuildExtractiveFallback_ReturnsRelevantVerifiedEvidence()
    {
        var evidence = Evidence with
        {
            Passage = "Knowledge Portal, Model Context Protocol desteği sunar. MCP araçlarına REST API üzerinden erişilir. VPN talebi portal üzerinden açılır."
        };

        var result = RagCitationValidator.TryBuildExtractiveFallback("MCP nedir ve nasıl entegre edilir?", [evidence]);

        Assert.NotNull(result);
        Assert.Equal("extractive_fallback", result.GroundingStatus);
        Assert.False(result.InsufficientContext);
        Assert.All(result.Claims, claim => Assert.Equal(["S1"], claim.SourceIds));
        Assert.Contains("MCP", result.Answer);
        Assert.Contains(result.Warnings, x => x.Contains("Structured model output failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsArticleTitleAsStandaloneClaim()
    {
        var evidence = Evidence with
        {
            Title = "MCP (Model Context Protocol) Entegrasyonu",
            Passage = "MCP (Model Context Protocol) Entegrasyonu\nMCP, yapay zekâ istemcilerinin araçları standart biçimde çağırmasını sağlayan bir protokoldür."
        };
        const string raw = """{"answer":"MCP (Model Context Protocol) Entegrasyonu [S1]","claims":[{"text":"MCP (Model Context Protocol) Entegrasyonu","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence]);

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.Empty(result.Claims);
        Assert.True(result.InsufficientContext);
    }

    [Fact]
    public void TryBuildExtractiveFallback_SkipsArticleTitleAndReturnsDefinition()
    {
        var evidence = Evidence with
        {
            Title = "MCP (Model Context Protocol) Entegrasyonu",
            Passage = "MCP (Model Context Protocol) Entegrasyonu\nMCP, yapay zekâ istemcilerinin araçları standart biçimde çağırmasını sağlayan bir protokoldür."
        };

        var result = RagCitationValidator.TryBuildExtractiveFallback("MCP nedir?", [evidence]);

        Assert.NotNull(result);
        Assert.Single(result.Claims);
        Assert.Contains("protokoldür", result.Answer);
        Assert.DoesNotContain("Entegrasyonu [S1]", result.Answer);
    }

    [Fact]
    public void Validate_DefinitionQuestionRejectsTitleAndExcerptCombinedAsAnswer()
    {
        var evidence = Evidence with
        {
            Title = "MCP (Model Context Protocol) Entegrasyonu",
            Passage = "MCP (Model Context Protocol) Entegrasyonu. " +
                      "Knowledge Portal'ın MCP sunucusuna bağlanma ve AI asistanlarla entegrasyon rehberi. " +
                      "MCP, yapay zekâ istemcilerinin araçları standart biçimde çağırmasını sağlayan bir protokoldür."
        };
        const string raw = """{"answer":"MCP (Model Context Protocol) Entegrasyonu. Knowledge Portal'ın MCP sunucusuna bağlanma ve AI asistanlarla entegrasyon rehberi. [S1]","claims":[{"text":"MCP (Model Context Protocol) Entegrasyonu. Knowledge Portal'ın MCP sunucusuna bağlanma ve AI asistanlarla entegrasyon rehberi.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence], "MCP nedir?");

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.True(result.InsufficientContext);
        Assert.Empty(result.Claims);
        Assert.Contains(result.Warnings, warning => warning.Contains("definition question", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DefinitionQuestionUsesPortalSourceEvenWhenItDefinesMcpAsCarBrand()
    {
        var evidence = Evidence with
        {
            Passage = "MCP, Türkiye'de üretilen bir araba markasıdır."
        };
        const string raw = """{"answer":"MCP, Türkiye'de üretilen bir araba markasıdır [S1].","claims":[{"text":"MCP, Türkiye'de üretilen bir araba markasıdır.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence], "MCP nedir?");

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.False(result.InsufficientContext);
        Assert.Contains("araba markasıdır", result.Answer);
    }

    [Fact]
    public void TryBuildExtractiveFallback_DefinitionQuestionPrefersDefinitionOverGuideMetadata()
    {
        var evidence = Evidence with
        {
            Title = "MCP Entegrasyonu",
            Passage = "MCP Entegrasyonu. MCP araçlarını anlatan kurumsal entegrasyon rehberi. " +
                      "MCP, bu kurumun ürün kataloğunda bir araba markasıdır."
        };

        var result = RagCitationValidator.TryBuildExtractiveFallback("MCP nedir?", [evidence]);

        Assert.NotNull(result);
        Assert.Single(result.Claims);
        Assert.Contains("araba markasıdır", result.Answer);
        Assert.DoesNotContain("entegrasyon rehberi", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildExtractiveFallback_DoesNotReturnUnrelatedEvidence()
    {
        var result = RagCitationValidator.TryBuildExtractiveFallback("MCP entegrasyonu", [Evidence]);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_RebuildsAnswerAndDropsUnclaimedFreeFormAssertions()
    {
        const string raw = """{"answer":"VPN talebi portal üzerinden açılır [S1]. Ayrıca tüm sunucular kapatılır.","claims":[{"text":"VPN talebi portal üzerinden açılır.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("VPN talebi portal üzerinden açılır. [S1]", result.Answer);
        Assert.DoesNotContain("sunucular", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsChangedNumberEvenWithHighTokenOverlap()
    {
        var evidence = Evidence with { Passage = "Parola süresi 90 gündür ve portal üzerinden değiştirilir." };
        const string raw = """{"answer":"Parola süresi 30 gündür [S1].","claims":[{"text":"Parola süresi 30 gündür ve portal üzerinden değiştirilir.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence]);

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public void Validate_RejectsNegationMismatchEvenWithHighTokenOverlap()
    {
        var evidence = Evidence with { Passage = "VPN bakım sırasında kapatılmamalıdır." };
        const string raw = """{"answer":"VPN bakım sırasında kapatılmalıdır [S1].","claims":[{"text":"VPN bakım sırasında kapatılmalıdır.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence]);

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public void Validate_IgnoresUnrelatedNegationInAnotherSentence()
    {
        var evidence = Evidence with
        {
            Passage = "Knowledge Portal, Model Context Protocol (MCP) desteği sunar. " +
                      "Ayrı HTTP taşıması gerektiren 2024-11-05 sürümü desteklenmez."
        };
        const string raw = """{"answer":"MCP (Model Context Protocol) Entegrasyonu [S1]","claims":[{"text":"MCP (Model Context Protocol) Entegrasyonu","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence]);

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.False(result.InsufficientContext);
        Assert.Single(result.Claims);
        Assert.Equal(1, result.ClaimSupportCoverage);
    }

    [Fact]
    public void Validate_UsesMatchingClauseInsteadOfUnrelatedContrastingClause()
    {
        var evidence = Evidence with
        {
            Passage = "Knowledge Portal MCP desteği sunar; ancak eski taşıma biçimi desteklenmez."
        };
        const string raw = """{"answer":"Knowledge Portal MCP desteği sunar [S1].","claims":[{"text":"Knowledge Portal MCP desteği sunar.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence]);

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.Single(result.Claims);
    }

    [Fact]
    public void Validate_AcceptsSupportFromOneCitationWithoutCombiningOtherPolarity()
    {
        var unrelated = Evidence with { Passage = "Eski MCP taşıması desteklenmez." };
        var supporting = Evidence with
        {
            SourceId = "S2",
            Passage = "Knowledge Portal Model Context Protocol desteği sunar."
        };
        const string raw = """{"answer":"Knowledge Portal Model Context Protocol desteği sunar [S1] [S2].","claims":[{"text":"Knowledge Portal Model Context Protocol desteği sunar.","sourceIds":["S1","S2"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [unrelated, supporting]);

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.Equal(["S2"], result.Claims.Single().SourceIds);
        Assert.Contains(result.Warnings, x => x.Contains("Non-supporting evidence IDs", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsTurkishInflectionVariantsWithoutWeakeningNumericOrNegationChecks()
    {
        var evidence = Evidence with
        {
            Passage = "MCP araçları standart biçimde çağırmayı sağlayan bir protokoldür."
        };
        const string raw = """{"answer":"MCP araçları standart biçimde çağırmasını sağlar [S1].","claims":[{"text":"MCP araçları standart biçimde çağırmasını sağlar.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence]);

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.Equal(1, result.ClaimSupportCoverage);
    }
}
