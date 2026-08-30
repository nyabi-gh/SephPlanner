using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Overlay;

/// <summary>
/// 플러그인이 덤프해 둔 아이콘 PNG. 없으면 null 을 돌려주고 화면은 글자로 물러선다.
/// 실패(파일 없음)는 캐시하지 않는다 - 오버레이가 떠 있는 동안 F9 로 아이콘이 새로 생길 수 있다.
/// </summary>
public static class IconStore
{
    private static readonly Dictionary<int, ImageSource> Cache = new();

    public static ImageSource? Get(int entityId)
    {
        if (entityId == 0) return null;
        if (Cache.TryGetValue(entityId, out var cached)) return cached;

        var path = Path.Combine(IpcContract.DataDirectory, "icons", entityId + ".png");
        if (!File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            Cache[entityId] = image;
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return null;
        }
    }
}
