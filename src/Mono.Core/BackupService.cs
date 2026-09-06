using System.IO.Compression;

namespace Mono.Core;

/// <summary>
/// 카탈로그·히스토리·기기·존 DB만 zip 으로 묶는다.
/// 음원 파일은 넣지 않는다 — 원본은 사용자 폴더에 있고, 백업이 수십 GB 가 될 이유가 없다.
/// </summary>
public sealed class BackupService
{
    private static readonly string[] Databases =
        ["catalog.db", "history.db", "endpoints.db", "zones.db"];

    private readonly string _dataDir;

    public BackupService(string dataDir) => _dataDir = dataDir;

    /// <summary>새 백업을 만들고 zip 경로를 돌려준다.</summary>
    public string Create()
    {
        var outDir = Path.Combine(_dataDir, "backups");
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, $"mono-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var db in Databases)
        {
            var source = Path.Combine(_dataDir, db);
            if (!File.Exists(source)) continue;

            // SQLite 가 열어 둔 파일이라 공유 읽기로 복사한다.
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var entry = zip.CreateEntry(db, CompressionLevel.Optimal).Open();
            input.CopyTo(entry);
        }

        return path;
    }
}
