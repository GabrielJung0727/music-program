using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 시크바에 그릴 히트맵·핀의 좌표 계산. Core는 10초 버킷으로 집계해 보내고,
/// Control은 그걸 트랙 길이에 대한 0–1 비율로 바꾼다.
/// </summary>
public class SeekLayerTests
{
    private static SnapshotHeatBucket Bucket(long at, int count)
        => new() { TrackId = "t", BucketMs = at, Count = count };

    [Fact]
    public void BandsSpanTenSecondBucketsAsFractions()
    {
        // 100초 트랙. 0–10초 버킷은 0.0–0.1 을 차지한다.
        var bands = SeekLayers.Bands([Bucket(0, 1)], 100_000);

        var b = Assert.Single(bands);
        Assert.Equal(0.0, b.Start, 3);
        Assert.Equal(0.1, b.End, 3);
    }

    [Fact]
    public void IntensityIsRelativeToTheBusiestBucket()
    {
        var bands = SeekLayers.Bands([Bucket(0, 1), Bucket(10_000, 4), Bucket(20_000, 2)], 100_000);

        Assert.Equal(0.25, bands[0].Intensity, 3);
        Assert.Equal(1.00, bands[1].Intensity, 3);
        Assert.Equal(0.50, bands[2].Intensity, 3);
    }

    [Fact]
    public void BandsAreClampedToTheTrack()
    {
        // 마지막 버킷이 트랙 끝을 넘어가도 1.0 을 넘지 않는다.
        var bands = SeekLayers.Bands([Bucket(20_000, 1)], 25_000);

        Assert.Equal(0.8, bands[0].Start, 3);
        Assert.Equal(1.0, bands[0].End, 3);
    }

    [Fact]
    public void ZeroDurationYieldsNothing()
    {
        // 곡이 없을 때 0으로 나누지 않는다.
        Assert.Empty(SeekLayers.Bands([Bucket(0, 3)], 0));
        Assert.Empty(SeekLayers.Marks([new SnapshotPin { MediaTimeMs = 5 }], 0));
    }

    [Fact]
    public void MarksUseOnlyCurrentTrackPins()
    {
        var pins = new[]
        {
            new SnapshotPin { Id = "p1", MediaTimeMs = 30_000, Text = "여기", OnCurrentTrack = true },
            new SnapshotPin { Id = "p2", MediaTimeMs = 40_000, Text = "다른 곡", OnCurrentTrack = false }
        };

        var marks = SeekLayers.Marks(pins, 100_000);

        var m = Assert.Single(marks);
        Assert.Equal("p1", m.Id);
        Assert.Equal(0.3, m.Position, 3);
        Assert.Equal("여기", m.Text);
    }
}
