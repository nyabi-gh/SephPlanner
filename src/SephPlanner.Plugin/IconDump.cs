using System;
using System.Collections.Generic;
using System.IO;
using SephPlanner.Core.Ipc;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 석판/아티팩트 아이콘을 사용자 PC에 PNG 로 덤프한다. 텍스트 데이터와 같은 원칙이다:
    /// 게임 저작물을 배포물에 넣는 대신 각자의 게임 설치본에서 생성한다 (docs/LEGAL.md).
    ///
    /// 아이콘은 아틀라스에 묶여 있고 텍스처가 읽기 불가일 수 있어, 아틀라스를 렌더 텍스처로
    /// 한 번 복사해 읽을 수 있게 만든 뒤 스프라이트 영역만 잘라낸다.
    /// </summary>
    internal static class IconDump
    {
        public static string Directory => Path.Combine(IpcContract.DataDirectory, "icons");

        public static int Write()
        {
            System.IO.Directory.CreateDirectory(Directory);

            var readableAtlases = new Dictionary<Texture2D, Texture2D>();
            var written = 0;
            try
            {
                foreach (var entity in Resources.LoadAll<ItemEntity>("Item"))
                {
                    if (entity.activeType == EItemActiveType.Disabled) continue;
                    if (entity.type != EItemType.Charm && entity.type != EItemType.StoneTablet) continue;
                    if (entity.icon == null || entity.icon.texture == null) continue;

                    try
                    {
                        var png = ExtractPng(entity.icon, readableAtlases);
                        if (png == null) continue;

                        File.WriteAllBytes(Path.Combine(Directory, entity.id + ".png"), png);
                        written++;
                    }
                    catch (Exception)
                    {
                        // 아이콘 하나가 못 나와도 나머지는 계속한다. 못 나온 것은 글자로 보인다.
                    }
                }
            }
            finally
            {
                foreach (var atlas in readableAtlases.Values) UnityEngine.Object.Destroy(atlas);
            }
            return written;
        }

        private static byte[] ExtractPng(Sprite sprite, Dictionary<Texture2D, Texture2D> readableAtlases)
        {
            var atlas = GetReadable(sprite.texture, readableAtlases);
            var rect = sprite.textureRect;
            var width = (int)rect.width;
            var height = (int)rect.height;
            if (width <= 0 || height <= 0) return null;

            var cut = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cut.SetPixels(atlas.GetPixels((int)rect.x, (int)rect.y, width, height));
            cut.Apply();

            var png = cut.EncodeToPNG();
            UnityEngine.Object.Destroy(cut);
            return png;
        }

        private static Texture2D GetReadable(Texture2D texture, Dictionary<Texture2D, Texture2D> cache)
        {
            if (cache.TryGetValue(texture, out var readable)) return readable;

            var temporary = RenderTexture.GetTemporary(
                texture.width, texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, temporary);

            var previous = RenderTexture.active;
            RenderTexture.active = temporary;
            readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readable.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);

            cache[texture] = readable;
            return readable;
        }
    }
}
