using MuseumBrowser.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using UnityEngine;

namespace MuseumBrowser.App
{
    [RequireComponent(typeof(Camera))]
    public sealed class FolioPanoramaViewer : MonoBehaviour
    {
        public Material SkyboxTemplate;
        public bool Ready { get; private set; }
        public string Failure { get; private set; }
        private PanoramaRecord record;
        private Dictionary<string, PanoramaRecord> tour;
        private bool loading;
        private Texture2D texture;
        private Material material;
        private Material previousSkybox;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private float yaw, pitch;

        private async void Start()
        {
            try
            {
                var records = await PanoramaRecord.LoadTourAsync(Path.Combine(Application.streamingAssetsPath, "Mapillary/budapest.folio"), lifetime.Token);
                tour = records.ToDictionary(item => item.Id);
                var initial = records.FirstOrDefault(item => item.TourStart) ?? records[0];
                if (SkyboxTemplate == null) throw new InvalidOperationException("Assign the Panoramic skybox material.");
                material = new Material(SkyboxTemplate);
                material.SetFloat("_Mapping", 1);
                material.SetFloat("_ImageType", 0);
                material.SetFloat("_Exposure", 1);
                previousSkybox = RenderSettings.skybox;
                RenderSettings.skybox = material;
                GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
                yaw = (float) initial.CompassHeading;
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                await LoadPanorama(initial.Id);
                Debug.Log("SURVOS_PANORAMA_TOUR " + tour.Count + " records " + tour.Values.Sum(item => item.Connections.Count) + " links");
                var args = Environment.GetCommandLineArgs();
                if (Array.IndexOf(args, "-panoramaStep") >= 0) {
                    var from = record.Id;
                    var next = record.Connections.Select(id => tour[id]).FirstOrDefault(item => item.SequenceIndex > record.SequenceIndex)
                        ?? record.Connections.Select(id => tour[id]).First();
                    await LoadPanorama(next.Id);
                    Debug.Log("SURVOS_PANORAMA_NAVIGATION_PASS " + from + " -> " + record.Id);
                }
                var capture = Array.IndexOf(args, "-panoramaCapture");
                if (capture >= 0 && capture + 1 < args.Length) StartCoroutine(CaptureAndQuit(args[capture + 1]));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Failure = error.Message; Debug.LogException(error);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-panoramaCapture") >= 0) Application.Quit(1); }
        }

        private async Task LoadPanorama(string id)
        {
            var next = tour[id];
            loading = true;
            Texture2D candidate = null;
            try {
                var bytes = await File.ReadAllBytesAsync(next.ImagePath, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                candidate = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!ImageConversion.LoadImage(candidate, bytes, true) || candidate.width != candidate.height * 2)
                    throw new InvalidDataException("Expected a decodable 2:1 panorama.");
                candidate.wrapModeU = TextureWrapMode.Repeat;
                candidate.wrapModeV = TextureWrapMode.Clamp;
                material.SetTexture("_MainTex", candidate);
                // Unity Panoramic texture center is +X before its counterclockwise rotation.
                material.SetFloat("_Rotation", 90 - (float) next.CompassHeading);
                if (texture != null) Destroy(texture);
                texture = candidate; candidate = null; record = next; Ready = true; Failure = null;
                Debug.Log("SURVOS_PANORAMA_LOADED " + record.Id + " " + texture.width + "x" + texture.height);
            } finally { if (candidate != null) Destroy(candidate); loading = false; }
        }

        private async void Navigate(string id)
        {
            if (loading) return;
            try { await LoadPanorama(id); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Failure = error.Message; Debug.LogException(error); }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, Math.Min(Screen.width - 20, 700), 80), "");
            GUI.Label(new Rect(20, 15, 680, 25), Failure != null ? Failure : Ready ? record.Title + " · " + record.Creator + " / Mapillary" : "Loading panorama…");
            if (!Ready) return;
            if (GUI.Button(new Rect(20, 45, 160, 30), "Open source / attribution")) Application.OpenURL(record.SourceUrl);
            GUI.Label(new Rect(190, 50, 450, 25), "Drag to look around · scroll to zoom");
            GUI.Label(new Rect(20, 90, 680, 25), loading ? "Loading panorama…" : "Stop " + (record.SequenceIndex + 1) + " / " + tour.Count);
            GUI.enabled = !loading;
            var slot = 0;
            foreach (var id in record.Connections) {
                var target = tour[id];
                var label = target.SequenceIndex > record.SequenceIndex ? "Next →" : "← Previous";
                if (GUI.Button(new Rect(20 + slot++ * 140, 120, 130, 30), label)) Navigate(id);
                var bearing = (float) record.BearingTo(target) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(bearing), -0.25f, Mathf.Cos(bearing));
                var point = GetComponent<Camera>().WorldToScreenPoint(transform.position + direction * 5);
                if (point.z > 0 && point.x > 50 && point.x < Screen.width - 50 && point.y > 50 && point.y < Screen.height - 160)
                    if (GUI.Button(new Rect(point.x - 55, Screen.height - point.y - 20, 110, 40), "↑ " + label)) Navigate(id);
            }
            GUI.enabled = true;
            var e = Event.current;
            if (e.type == EventType.MouseDrag && e.button == 0) {
                yaw += e.delta.x * 0.2f;
                pitch = Mathf.Clamp(pitch + e.delta.y * 0.2f, -85, 85);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                e.Use();
            }
            if (e.type == EventType.ScrollWheel) {
                var camera = GetComponent<Camera>();
                camera.fieldOfView = Mathf.Clamp(camera.fieldOfView + e.delta.y, 35, 100);
                e.Use();
            }
        }

        private IEnumerator CaptureAndQuit(string output)
        {
            // Capture the actual rendered URP frame, including the attribution overlay.
            yield return null;
            yield return new WaitForEndOfFrame();
            try {
                var pixels = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllBytes(output, ImageConversion.EncodeToPNG(pixels));
                Destroy(pixels);
                Debug.Log("SURVOS_PANORAMA_RENDER_PASS " + record.Id);
                Application.Quit(0);
            } catch (Exception error) { Debug.LogException(error); Application.Quit(1); }
        }

        private void OnDestroy()
        {
            lifetime.Cancel();
            if (material != null && RenderSettings.skybox == material) RenderSettings.skybox = previousSkybox;
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
        }
    }
}
