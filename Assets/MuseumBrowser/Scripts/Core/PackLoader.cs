using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MuseumBrowser.Core
{
    public static class PackLoader
    {
        /// Loads a pack from StreamingAssets (relative path) or any http(s) URL.
        /// UnityWebRequest is required for StreamingAssets on Web builds.
        public static async Awaitable<AppDataPack> LoadAsync(string pathOrUrl)
        {
            var url = pathOrUrl.StartsWith("http") ? pathOrUrl : StreamingUrl(pathOrUrl);
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Cannot load pack {url}: {request.error}");

            var json = request.downloadHandler.text;
            await Awaitable.BackgroundThreadAsync();
            var pack = JsonConvert.DeserializeObject<AppDataPack>(json);
            await Awaitable.MainThreadAsync();
            return pack;
        }

        static string StreamingUrl(string relative)
        {
            var path = Path.Combine(Application.streamingAssetsPath, relative);
            return path.Contains("://") ? path : "file://" + path;
        }
    }
}
