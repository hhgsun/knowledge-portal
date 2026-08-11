using System.Security.Cryptography;
using KnowledgePortal.Api.Helpers;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class AttachmentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kp-storage-test", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAtomic_WritesContentAndReturnsSha256()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FileStorage:BasePath"] = _root }).Build();
        var bytes = "kalıcı içerik"u8.ToArray();

        var hash = await AttachmentHelper.SaveAtomicAsync(config, "article", "stored.txt",
            new MemoryStream(bytes));

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hash);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(AttachmentHelper.GetFilePath(config, "article", "stored.txt")));
        Assert.Empty(Directory.GetFiles(AttachmentHelper.GetArticleDirectory(config, "article"), "*.upload"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
