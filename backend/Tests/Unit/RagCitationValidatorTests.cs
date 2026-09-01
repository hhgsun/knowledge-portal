using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagCitationValidatorTests
{
    [Fact]
    public void RenderSupportedAnswer_FormatsSummaryAndExplanationWithoutAddingFacts()
    {
        var claims = new List<RagClaim>
        {
            new("VPN erişimi sertifika tabanlıdır.", ["S1"]),
            new("Kullanıcı önce VPN profilini indirir.", ["S1"]),
            new("Ardından kullanıcı sertifikasını seçer.", ["S2"])
        };

        var answer = RagCitationValidator.RenderSupportedAnswer(claims, "VPN nasıl kurulur?", "fallback", false);

        Assert.Equal("VPN erişimi sertifika tabanlıdır. [S1]\n\n**Açıklama**\n\n" +
                     "- Kullanıcı önce VPN profilini indirir. [S1]\n" +
                     "- Ardından kullanıcı sertifikasını seçer. [S2]", answer);
    }

    [Fact]
    public void RenderSupportedAnswer_UsesEnglishHeadingForEnglishQuestion()
    {
        var claims = new List<RagClaim>
        {
            new("VPN access uses certificates.", ["S1"]),
            new("The profile is downloaded first.", ["S1"])
        };

        var answer = RagCitationValidator.RenderSupportedAnswer(claims, "How does VPN work?", "fallback", false);

        Assert.Contains("**Explanation**", answer);
        Assert.DoesNotContain("**Açıklama**", answer);
    }

    [Fact]
    public void RenderSupportedAnswer_GroupsStructuredClaimRoles()
    {
        var claims = new List<RagClaim>
        {
            new("VPN profili sertifika tabanlıdır.", ["S1"], "summary"),
            new("Profili portal üzerinden indirin.", ["S1"], "step"),
            new("Yalnız yönetilen cihazlar desteklenir.", ["S2"], "constraint"),
            new("Servis yoksa yerel bağlantı kullanılır.", ["S3"], "exception"),
            new("Eski rehber 30 saniye belirtir.", ["S4"], "conflict")
        };

        var answer = RagCitationValidator.RenderSupportedAnswer(claims, "VPN nasıl çalışır?", "", false);

        Assert.Contains("**Adımlar**\n\n1. Profili", answer);
        Assert.Contains("**Sınırlar**\n\n- Yalnız", answer);
        Assert.Contains("**İstisnalar**\n\n- Servis", answer);
        Assert.Contains("**Kaynak uyuşmazlıkları**\n\n- Eski", answer);
    }

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
    public void Validate_AcceptsClaimOnlyStructuredOutput()
    {
        const string raw = """{"claims":[{"text":"VPN talebi portal üzerinden açılır.","role":"summary","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [Evidence]);

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.Equal("summary", result.Claims.Single().Role);
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

    [Fact]
    public void Validate_BareConfigurationKeyRejectsSingleTerseClaimRegardlessOfPunctuation()
    {
        var evidence = Evidence with
        {
            Passage = "Reranking:External: kapalı varsayılan external cross-encoder, timeout ve veri sınırları. Reranking:External, aday pasajları yeniden sıralar."
        };
        const string raw = """{"answer":"Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları [S1].","claims":[{"text":"Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence], "Reranking:External");

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.True(result.InsufficientContext);
        Assert.Single(result.Claims);
        Assert.Contains(result.Warnings, warning => warning.Contains("summary and at least one separate", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_BareConfigurationKeyPreservesSummaryBeforeExplanationParagraph()
    {
        var evidence = Evidence with
        {
            Passage = "Reranking:External: kapalı varsayılan external cross-encoder, timeout ve veri sınırları. Reranking:External, aday pasajları harici bir modelle yeniden sıralar. Harici servis hata verdiğinde yerel sıralama kullanılır."
        };
        const string raw = """{"answer":"Reranking:External: kapalı varsayılan external cross-encoder, timeout ve veri sınırları [S1]. Reranking:External, aday pasajları harici bir modelle yeniden sıralar [S1]. Harici servis hata verdiğinde yerel sıralama kullanılır [S1].","claims":[{"text":"Reranking:External: kapalı varsayılan external cross-encoder, timeout ve veri sınırları.","sourceIds":["S1"]},{"text":"Reranking:External, aday pasajları harici bir modelle yeniden sıralar.","sourceIds":["S1"]},{"text":"Harici servis hata verdiğinde yerel sıralama kullanılır.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence], "Reranking:External");

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.False(result.InsufficientContext);
        Assert.Equal(3, result.Claims.Count);
        Assert.StartsWith("Reranking:External: kapalı varsayılan", result.Answer);
        Assert.Contains("[S1]\n\nReranking:External, aday pasajları", result.Answer);
    }

    [Fact]
    public void Validate_ConfigurationDefinitionAcceptsSourceShorthandAndFollowupExplanation()
    {
        var evidence = Evidence with
        {
            Passage = "Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları. Harici servis hata verdiğinde yerel sıralama kullanılır."
        };
        const string raw = """{"answer":"Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları [S1]. Harici servis hata verdiğinde yerel sıralama kullanılır [S1].","claims":[{"text":"Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları.","sourceIds":["S1"]},{"text":"Harici servis hata verdiğinde yerel sıralama kullanılır.","sourceIds":["S1"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw, [evidence], "Reranking:External nedir?");

        Assert.Equal("lexically_grounded", result.GroundingStatus);
        Assert.False(result.InsufficientContext);
        Assert.Equal(2, result.Claims.Count);
        Assert.Contains("[S1]\n\nHarici servis hata verdiğinde", result.Answer);
    }

    [Fact]
    public void Validate_ConfigurationDefinitionRejectsUnrelatedQuestionHeadingAsExplanation()
    {
        var definition = Evidence with
        {
            Passage = "Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları."
        };
        var unrelatedHeading = Evidence with
        {
            SourceId = "S2",
            Title = "Knowledge Portal — Başlangıç Rehberi",
            Passage = "Knowledge Portal Nedir?"
        };
        var relevantExplanation = Evidence with
        {
            SourceId = "S20",
            Passage = "Opsiyonel external cross-encoder yalnız açıkça etkinleştirilir ve hata halinde yerel sıralamaya döner."
        };
        const string raw = """{"answer":"Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları [S1]. Knowledge Portal Nedir? [S2]","claims":[{"text":"Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları.","sourceIds":["S1"]},{"text":"Knowledge Portal Nedir?","sourceIds":["S2"]}],"insufficientContext":false}""";

        var result = RagCitationValidator.Validate(raw,
            [definition, unrelatedHeading, relevantExplanation], "Reranking:External nedir?");

        Assert.Equal("rejected_unsupported", result.GroundingStatus);
        Assert.True(result.InsufficientContext);
        Assert.Single(result.Claims);
        Assert.DoesNotContain("Knowledge Portal", result.Claims.Single().Text);
    }

    [Fact]
    public void TryEnrichSupportedSummary_AppendsRelevantEvidenceAsNewParagraph()
    {
        var summary = new RagClaim(
            "Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları.", ["S1"]);
        var summaryEvidence = Evidence with
        {
            Passage = summary.Text
        };
        var explanationEvidence = Evidence with
        {
            SourceId = "S2",
            Passage = "Opsiyonel external cross-encoder yalnız açıkça etkinleştirilir, aday ve timeout sınırları kullanır ve hatada yerel sonuca döner."
        };

        var result = RagCitationValidator.TryEnrichSupportedSummary(
            "Reranking:External nedir?", [summaryEvidence, explanationEvidence], [summary]);

        Assert.NotNull(result);
        Assert.Equal("extractive_enrichment", result.GroundingStatus);
        Assert.False(result.InsufficientContext);
        Assert.Equal(2, result.Claims.Count);
        Assert.Contains("[S1]\n\nOpsiyonel external cross-encoder", result.Answer);
        Assert.Contains("[S2]", result.Answer);
    }

    [Fact]
    public void TryEnrichSupportedSummary_IgnoresUnrelatedQuestionHeading()
    {
        var summary = new RagClaim(
            "Reranking:External, kapalı varsayılan external cross-encoder, timeout ve veri sınırları.", ["S1"]);
        var summaryEvidence = Evidence with { Passage = summary.Text };
        var unrelatedHeading = Evidence with
        {
            SourceId = "S2",
            Title = "Knowledge Portal — Başlangıç Rehberi",
            Passage = "Knowledge Portal Nedir?"
        };
        var explanation = Evidence with
        {
            SourceId = "S20",
            Passage = "Opsiyonel external cross-encoder yalnız açıkça etkinleştirilir, candidate/metin/timeout sınırları kullanır ve hata veya geçersiz yanıtta yerel sonuca döner."
        };

        var result = RagCitationValidator.TryEnrichSupportedSummary(
            "Reranking:External nedir?", [summaryEvidence, unrelatedHeading, explanation], [summary]);

        Assert.NotNull(result);
        Assert.Equal(2, result.Claims.Count);
        Assert.Contains("Opsiyonel external cross-encoder", result.Answer);
        Assert.Contains("[S20]", result.Answer);
        Assert.DoesNotContain("Knowledge Portal", result.Answer);
        Assert.DoesNotContain("[S2]", result.Answer);
    }

    [Fact]
    public void TryBuildConfigurationExplanationFallback_ExtractsFlattenedEntryAndExplanation()
    {
        var flattened = Evidence with
        {
            Passage = "Önemli Yapılandırmalar Ollama:Ranking: freshness ve authority ağırlıkları " +
                      "Reranking:External: kapalı varsayılan external cross-encoder, timeout ve veri sınırları " +
                      "Ollama:ChunkTargetWords / ChunkOverlapWords ayarları."
        };
        var explanation = Evidence with
        {
            SourceId = "S20",
            Passage = "Opsiyonel external cross-encoder yalnız açıkça etkinleştirilir, candidate/metin/timeout sınırları kullanır ve hata veya geçersiz yanıtta yerel sonuca döner."
        };

        var result = RagCitationValidator.TryBuildConfigurationExplanationFallback(
            "Reranking:External nedir?", [flattened, explanation]);

        Assert.NotNull(result);
        Assert.Equal("extractive_fallback", result.GroundingStatus);
        Assert.False(result.InsufficientContext);
        Assert.Equal(2, result.Claims.Count);
        Assert.StartsWith("Reranking:External: kapalı varsayılan external cross-encoder, timeout ve veri sınırları. [S1]", result.Answer);
        Assert.Contains("[S1]\n\nOpsiyonel external cross-encoder", result.Answer);
        Assert.Contains("[S20]", result.Answer);
        Assert.DoesNotContain("Ollama:ChunkTargetWords", result.Answer);
    }
}
