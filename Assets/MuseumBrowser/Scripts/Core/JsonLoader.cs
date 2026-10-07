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
            // One retry: a request that times out or drops is usually fine the second time.
            try { return await LoadOnceAsync<T>(pathOrUrl); }
            catch (IOException e)
            {
                Debug.LogWarning($"Retrying after: {e.Message}");
                return await LoadOnceAsync<T>(pathOrUrl);
            }
        }

        static async Awaitable<T> LoadOnceAsync<T>(string pathOrUrl)
        {
            var url = pathOrUrl.StartsWith("http") ? pathOrUrl : StreamingUrl(pathOrUrl);
            using var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", "application/json, application/ld+json");
            request.timeout = 8;
            Diag.Begin(url);
            await request.SendWebRequest();
            Diag.End(url, request.result == UnityWebRequest.Result.Success ? "OK" : $"FAIL {request.responseCode} {request.error}",
                (long)request.downloadedBytes);
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Cannot load {url}: {request.responseCode} {request.error}");

            var json = request.downloadHandler.text;
#if UNITY_WEBGL && !UNITY_EDITOR
            // The Web player is single-threaded: there is no background thread to move to.
            return JsonConvert.DeserializeObject<T>(json);
#else
            await Awaitable.BackgroundThreadAsync();
            var value = JsonConvert.DeserializeObject<T>(json);
            await Awaitable.MainThreadAsync();
            return value;
#endif
        }

        static string StreamingUrl(string relative)
        {
            var path = Path.Combine(Application.streamingAssetsPath, relative);
            return path.Contains("://") ? path : "file://" + path;
        }
    }
}
