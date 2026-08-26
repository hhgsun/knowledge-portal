using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagTokenCounterTests
{
    [Fact]
    public void CountTokens_IsUnicodeAwareAndConservativeForTurkishText()
    {
        var counter = new RagTokenCounter();

        Assert.True(counter.CountTokens("şifre doğrulaması") >= 8);
        Assert.True(counter.CountTokens("plainlatintext") < counter.CountTokens("şifredoğrulama"));
    }

    [Fact]
    public void TruncateToTokens_NeverExceedsBudgetOrSplitsSurrogatePair()
    {
        var counter = new RagTokenCounter();
        var result = counter.TruncateToTokens("VPN 🔐 profili kullanıcı sertifikasıyla kurulur", 8,
            out var tokens, out var truncated);

        Assert.True(truncated);
        Assert.True(tokens <= 8);
        Assert.False(result.Length > 0 && char.IsHighSurrogate(result[^1]));
    }

    [Fact]
    public void ObserveActualCount_CalibratesFutureEstimatesUpward()
    {
        var counter = new RagTokenCounter();
        var before = counter.CountTokens("vpn profile certificate validation");

        counter.ObserveActualCount(before, before * 2);

        var calibrated = counter.CountTokens("vpn profile certificate validation");
        Assert.True(calibrated > before);

        for (var i = 0; i < 10; i++)
            counter.ObserveActualCount(calibrated, calibrated);

        Assert.Equal(calibrated, counter.CountTokens("vpn profile certificate validation"));
    }
}
