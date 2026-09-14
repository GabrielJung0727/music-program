using Mono.Core;
using System.Text.Json;
using Xunit;

namespace Mono.Tests;

public class TidalStreamUrlTests
{
    [Fact]
    public void PickBestHttpUrlPrefersFlacOverHls()
    {
        using var doc = JsonDocument.Parse("""
            {
              "mimeType": "application/vnd.apple.mpegurl",
              "urls": ["https://cdn.example/master.m3u8"],
              "alternate": ["https://cdn.example/track.flac?token=abc"]
            }
            """);

        var url = TidalClient.PickBestHttpUrl(doc.RootElement);
        Assert.Contains(".flac", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScoreStreamUrlPenalizesM3u8()
    {
        Assert.True(TidalClient.ScoreStreamUrl("https://x/a.flac") > TidalClient.ScoreStreamUrl("https://x/master.m3u8"));
    }
}
