using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagConflictDetectorTests
{
    [Fact]
    public void Assess_DetectsNumericConflictAndPrefersApprovedAuthority()
    {
        var evidence = new[]
        {
            Evidence("S1", "VPN oturum süresi 30 dakikadır.", authority: 60, approved: false),
            Evidence("S2", "VPN oturum süresi 45 dakikadır.", authority: 90, approved: true)
        };

        var result = RagConflictDetector.Assess("VPN oturum süresi nedir?", evidence);

        Assert.Equal("conflicts_detected", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("numeric", conflict.Kind);
        Assert.Equal("S2", conflict.PreferredSourceId);
        Assert.Equal("preferred_by_governance", conflict.Resolution);
    }

    [Fact]
    public void Assess_LeavesEqualGovernanceConflictUnresolved()
    {
        var evidence = new[]
        {
            Evidence("S1", "VPN erişimi desteklenir."),
            Evidence("S2", "VPN erişimi desteklenmez.")
        };

        var result = RagConflictDetector.Assess("VPN erişimi desteklenir mi?", evidence);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("polarity", conflict.Kind);
        Assert.Null(conflict.PreferredSourceId);
        Assert.Equal("unresolved_equal_governance", conflict.Resolution);
    }

    [Fact]
    public void Assess_DetectsOpposingPolicyModalities()
    {
        var evidence = new[]
        {
            Evidence("S1", "VPN kullanımı zorunludur."),
            Evidence("S2", "VPN kullanımı isteğe bağlıdır.")
        };

        var result = RagConflictDetector.Assess("VPN kullanımı nasıl yapılır?", evidence);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("policy_modality", conflict.Kind);
    }

    private static RagEvidence Evidence(string id, string passage, int authority = 50, bool approved = false) =>
        new(id, id, id, id, "article", null, null, null, passage, .9,
            AuthorityWeight: authority, Approved: approved, ReviewState: "current",
            ReliabilityScore: 80, UpdatedAt: "2026-08-26T00:00:00Z");
}
