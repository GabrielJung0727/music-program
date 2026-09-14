namespace Mono.Shared;

/// <summary>
/// 사용자 데이터가 사는 곳.
///
/// 설치 루트(%LOCALAPPDATA%\Mono)와 반드시 다른 폴더여야 한다. 그쪽은 Velopack 이
/// 제 것으로 알고 설치 때 통째로 갈아엎기 때문이다. 0.4.1 이전에는 카탈로그·스트리밍
/// 토큰·백업·WebView 프로필이 전부 그 안에 있었고, 재설치 한 번에 같이 사라졌다.
/// 설치 프로그램이 옮겼다 되돌리는 임시 방편을 두긴 했지만, 남의 도구가 소유한
/// 폴더에 우리 것을 두지 않는 편이 낫다.
///
/// 옛 자리에 있던 것은 처음 물어볼 때 한 번 옮겨 온다. 업데이트로 올라온 사용자는
/// 자기 라이브러리가 그대로 있는 것만 본다.
/// </summary>
public static class UserPaths
{
    private static readonly object Gate = new();
    private static bool _migrated;

    /// <summary>Velopack 이 관리하는 설치 루트. 여기에는 아무것도 쓰지 않는다.</summary>
    public static string InstallRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono");

    /// <summary>우리 것만 있는 폴더. 설치가 무엇을 하든 살아남는다.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonoData");

    /// <summary>
    /// 0.4.2 이전에 data 가 있던 자리. 저장된 절대 경로가 이 밑을 가리키고 있으면
    /// 파일은 따라왔어도 경로는 옛 자리를 가리킨 채라, 새 자리로 되짚어야 한다.
    /// </summary>
    public static string LegacyDataRoot { get; } = Path.Combine(InstallRoot, "data");

    /// <summary>지금 data 가 있는 자리.</summary>
    public static string DataRoot => Resolve("data");

    /// <summary>
    /// 설치 루트에 있던 것들. 파일인지 폴더인지는 옮길 때 보고 판단한다.
    /// 여기 없는 것(current·packages·Update.exe)은 설치가 다시 만드는 것들이다.
    /// </summary>
    public static IReadOnlyList<string> UserOwnedEntries => LegacyEntries;

    private static readonly string[] LegacyEntries =
    [
        "data",              // 카탈로그·스트리밍 토큰·백업·받아 둔 아트워크
        "webview",           // WebView2 프로필 — localStorage 가 여기 있다
        "prefs.ini",         // 업데이트 피드와 토큰
        "credentials.env",
        "mono.log",
        "crash.log",
        "update-check.log",
    ];

    /// <summary>사용자 데이터 폴더 안의 경로. 첫 호출 때 옛 자리에서 옮겨 온다.</summary>
    public static string Resolve(string name)
    {
        EnsureMigrated();
        return Path.Combine(Root, name);
    }

    /// <summary>
    /// 옛 자리에 남아 있는 것을 새 자리로 옮긴다. 두 프로세스(Core·Control)가 거의 동시에
    /// 뜨므로 한쪽이 이미 옮겼을 수 있다 — 그때는 건너뛴다. 덮어쓰지 않는 게 중요하다.
    /// </summary>
    public static void EnsureMigrated()
    {
        lock (Gate)
        {
            if (_migrated) return;
            _migrated = true;

            try { Directory.CreateDirectory(Root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

            if (string.Equals(Path.GetFullPath(Root), Path.GetFullPath(InstallRoot),
                    StringComparison.OrdinalIgnoreCase))
                return;

            Migrate(InstallRoot, Root, LegacyEntries);
        }
    }

    /// <summary>
    /// <paramref name="from"/> 에 남아 있는 항목을 <paramref name="to"/> 로 옮긴다.
    /// 목적지에 같은 이름이 이미 있으면 건드리지 않는다 — 옮기다 만 상태에서 다시
    /// 실행됐을 때 새 데이터를 옛 데이터로 덮어쓰는 일을 막는다.
    /// </summary>
    public static void Migrate(string from, string to, IReadOnlyList<string> entries)
    {
        foreach (var name in entries)
        {
            var old = Path.Combine(from, name);
            var dest = Path.Combine(to, name);
            try
            {
                if (Directory.Exists(old) && !Directory.Exists(dest)) Directory.Move(old, dest);
                else if (File.Exists(old) && !File.Exists(dest)) File.Move(old, dest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 못 옮긴 것은 옛 자리에 남는다. 다음 실행에서 다시 시도한다 —
                // 여기서 멈추면 나머지 항목까지 옮기지 못한다.
            }
        }
    }
}
