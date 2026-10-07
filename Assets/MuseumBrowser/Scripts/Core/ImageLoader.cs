using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using WebP;

namespace MuseumBrowser.Core
{
    /// Async image loading with an in-memory cache. Disk caching comes later.
    /// imgproxy presets deliver WebP, which Unity cannot decode, so WebP bytes go
    /// through libwebp (unity.webp); JPEG/PNG use Unity's own decoder.
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

            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Cannot load image {url}: {request.error}");

            var texture = Decode(request.downloadHandler.data, url);
            texture.wrapMode = TextureWrapMode.Clamp;
            Cache[url] = texture;
            return texture;
        }

        static Texture2D Decode(byte[] bytes, string url)
        {
            if (IsWebP(bytes))
            {
                var webp = Texture2DExt.CreateTexture2DFromWebP(bytes, lMipmaps: true, lLinear: false, out var error);
                if (error != Error.Success) throw new IOException($"Cannot decode WebP {url}: {error}");
                return webp;
            }

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(bytes, markNonReadable: true))
                throw new IOException($"Cannot decode image {url}");
            return texture;
        }

        // RIFF container with a WEBP form type.
        static bool IsWebP(byte[] b) =>
            b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
            && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P';
    }
}
