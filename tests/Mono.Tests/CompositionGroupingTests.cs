using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 작품(Work) 그룹핑. 같은 작품의 여러 악장·연주를 한 줄로 묶되,
/// 확신이 없으면 묶지 않는다 — 잘못된 병합이 미병합보다 나쁘다.
/// </summary>
public class CompositionGroupingTests
{
    [Fact]
    public void MovementsOfOneWorkShareAKey()
    {
        var first = CompositionGrouping.Identify(
            "Symphony No. 5 in C minor, Op. 67: I. Allegro con brio", "Ludwig van Beethoven");
        var second = CompositionGrouping.Identify(
            "Symphony No. 5 in C minor, Op. 67: II. Andante con moto", "Ludwig van Beethoven");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Value.Key, second!.Value.Key);
        Assert.Equal("Symphony No. 5 in C minor, Op. 67", first.Value.Title);
    }

    [Fact]
    public void DifferentComposersNeverShareAKey()
    {
        var a = CompositionGrouping.Identify("Requiem: Introitus", "Mozart");
        var b = CompositionGrouping.Identify("Requiem: Introitus", "Verdi");

        Assert.NotEqual(a!.Value.Key, b!.Value.Key);
    }

    [Fact]
    public void NoComposerMeansNoGrouping()
    {
        // 작곡가 태그가 없으면 제목만으로 묶지 않는다.
        Assert.Null(CompositionGrouping.Identify("Blue Train", null));
        Assert.Null(CompositionGrouping.Identify("Blue Train", "   "));
    }

    [Fact]
    public void NonMovementSuffixIsNotStripped()
    {
        // "Live at Carnegie Hall"은 악장이 아니다. 잘라내면 다른 곡과 잘못 묶인다.
        var w = CompositionGrouping.Identify("Take Five: Live at Carnegie Hall", "Paul Desmond");

        Assert.Equal("Take Five: Live at Carnegie Hall", w!.Value.Title);
    }

    [Fact]
    public void ArabicNumeralMovementsAlsoSplit()
    {
        var a = CompositionGrouping.Identify("The Four Seasons, Op. 8: 1. Spring", "Vivaldi");
        var b = CompositionGrouping.Identify("The Four Seasons, Op. 8: 4. Winter", "Vivaldi");

        Assert.Equal(a!.Value.Key, b!.Value.Key);
        Assert.Equal("The Four Seasons, Op. 8", a.Value.Title);
    }

    [Fact]
    public void KeyIgnoresCaseAndSpacing()
    {
        var a = CompositionGrouping.Identify("Nocturne  in  E-flat", "Chopin");
        var b = CompositionGrouping.Identify("NOCTURNE IN E-FLAT", "chopin");

        Assert.Equal(a!.Value.Key, b!.Value.Key);
    }

    [Fact]
    public void EmptyTitleIsNotGrouped()
    {
        Assert.Null(CompositionGrouping.Identify("", "Bach"));
        Assert.Null(CompositionGrouping.Identify("   ", "Bach"));
    }
}
