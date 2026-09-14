using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Mono.Control.Services;

/// <summary>
/// 폴더·IR 파일 선택 대화상자. StorageProvider는 TopLevel에만 있으므로
/// Window에서든 UserControl에서든 호출한 쪽의 TopLevel을 찾아 쓴다.
/// </summary>
public static class FilePickers
{
    public static async Task<string?> PickLibraryFolderAsync(Visual from)
    {
        if (TopLevel.GetTopLevel(from) is not { } top) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "음악 라이브러리 폴더",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickImpulseResponseAsync(Visual from)
    {
        if (TopLevel.GetTopLevel(from) is not { } top) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "룸 IR (WAV / ZIP)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Impulse response") { Patterns = ["*.wav", "*.zip"] },
                FilePickerFileTypes.All
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
