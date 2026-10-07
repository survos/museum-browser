using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MuseumBrowser.Core
{
    /// Async image loading with an in-memory cache. Disk caching comes later.
    public static class ImageLoader
    {
        static readonly Dictionary<string, Texture2D> Cache = new();

        // Enter Play Mode without a domain reload keeps statics alive while the
        // textures themselves are destroyed on exit, so start each session empty.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => Cache.Clear();

        public static async Awaitable<Texture2D> LoadAsync(string url)
        {
            if (Cache.TryGetValue(url, out var cached) && cached) return cached;

            using var request = UnityWebRequestTexture.GetTexture(url, nonReadable: true);
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Cannot load image {url}: {request.error}");

            var texture = DownloadHandlerTexture.GetContent(request);
            texture.wrapMode = TextureWrapMode.Clamp;
            Cache[url] = texture;
            return texture;
        }
    }
}
