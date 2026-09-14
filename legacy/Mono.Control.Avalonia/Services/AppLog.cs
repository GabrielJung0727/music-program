namespace Mono.Control.Services;

/// <summary>
/// Control·Core·Output 세 프로세스의 로그를 한 파일로 모은다.
///
/// Core 와 Output 은 창 없는 WinExe 라 콘솔 출력이 갈 곳이 없었다. 그렇다고 파이프만 열어두고
/// 읽지 않으면 더 나쁘다 — Windows 파이프 버퍼(4KB)가 차는 순간 자식 프로세스가
/// Console.WriteLine 안에서 영구히 멈춘다. Output 은 5초마다 클럭 상태를 찍으므로
/// 재생 1~2분이면 렌더 루프가 통째로 정지했다.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mono", "mono.log");

    public static void Write(string source, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                Roll();
                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:HH:mm:ss.fff} [{source}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로그를 못 써도 재생은 계속돼야 한다.
        }
    }

    /// <summary>파일이 커지면 한 세대만 남기고 넘긴다. 무한히 자라지 않게 한다.</summary>
    private static void Roll()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes) return;

        var previous = FilePath + ".1";
        try { File.Delete(previous); } catch { /* 이전 세대가 잠겨 있으면 그냥 덮어쓴다 */ }
        try { File.Move(FilePath, previous); } catch { /* 옮기지 못하면 계속 덧붙인다 */ }
    }
}
