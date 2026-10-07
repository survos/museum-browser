using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MuseumBrowser.Core
{
    public static class JsonLoader
    {
        /// Loads JSON from an http(s) URL (normally harvest) or a StreamingAssets-relative
        /// path (bundled samples). UnityWebRequest is required for StreamingAssets on Web.
        public static async Awaitable<T> LoadAsync<T>(string pathOrUrl)
        {
            var url = pathOrUrl.StartsWith("http") ? pathOrUrl : StreamingUrl(pathOrUrl);
            using var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", "application/json, application/ld+json");
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Cannot load {url}: {request.error}");

            var json = request.downloadHandler.text;
            await Awaitable.BackgroundThreadAsync();
            var value = JsonConvert.DeserializeObject<T>(json);
            await Awaitable.MainThreadAsync();
            return value;
        }

        static string StreamingUrl(string relative)
        {
            var path = Path.Combine(Application.streamingAssetsPath, relative);
            return path.Contains("://") ? path : "file://" + path;
        }
    }
}
