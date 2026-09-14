using System.Text.Json.Nodes;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 첫 실행 설정 저장소. 마법사를 한 번만 띄우고, 그때 고른 값을 다음 실행에 돌려주는 것이
/// 전부이므로 검사할 것도 그 두 가지다 — 안 끝냈으면 completed=false, 끝냈으면 그대로.
/// </summary>
public class SetupStoreTests
{
    private static SetupStore NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-setup-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return new SetupStore(dir);
    }

    [Fact]
    public void FirstRunIsNotCompleted()
    {
        Assert.False(NewStore().Read()["completed"]!.GetValue<bool>());
    }

    [Fact]
    public void MergedFieldsSurviveAReread()
    {
        var store = NewStore();
        store.Merge(new JsonObject
        {
            ["completed"] = true,
            ["theme"] = "light",
            ["audio"] = new JsonObject { ["deviceName"] = "Holo May L3", ["driverType"] = "ASIO" },
        });

        var read = store.Read();
        Assert.True(read["completed"]!.GetValue<bool>());
        Assert.Equal("light", read["theme"]!.GetValue<string>());
        Assert.Equal("Holo May L3", read["audio"]!["deviceName"]!.GetValue<string>());
    }

    [Fact]
    public void MergeOnlyTouchesTheFieldsItWasGiven()
    {
        var store = NewStore();
        store.Merge(new JsonObject { ["completed"] = true, ["theme"] = "light" });
        store.Merge(new JsonObject { ["theme"] = "dark" });

        var read = store.Read();
        Assert.True(read["completed"]!.GetValue<bool>());
        Assert.Equal("dark", read["theme"]!.GetValue<string>());
    }

    [Fact]
    public void ClearBringsTheWizardBack()
    {
        var store = NewStore();
        store.Merge(new JsonObject { ["completed"] = true });
        store.Clear();

        Assert.False(store.Read()["completed"]!.GetValue<bool>());
    }

    [Fact]
    public void BrokenFileFallsBackToFirstRunRatherThanThrowing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-setup-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "setup.json"), "{ this is not json");

        // 파일 하나가 깨졌다고 앱이 못 뜨는 것보다, 마법사를 한 번 더 보는 편이 낫다.
        Assert.False(new SetupStore(dir).Read()["completed"]!.GetValue<bool>());
    }
}
