using System.Collections.Generic;
using MuseumBrowser.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace MuseumBrowser.App
{
    /// A screen on a gallery wall that plays a tour stop's films in turn, with the current
    /// film's caption and credit beneath it. Streams from the source URL when played.
    public sealed class ScreenPlayer : MonoBehaviour
    {
        const float Border = 0.05f, Depth = 0.06f;

        public int Room;
        public IReadOnlyList<Recording> Videos => videos;
        public bool IsPlaying => player && player.isPlaying;
        /// Raised when the current film changes (index into Videos).
        public event System.Action<ScreenPlayer> Changed;
        public int Current { get; private set; }

        List<Recording> videos;
        VideoPlayer player;
        TextMeshPro caption;
        Material material;
        RenderTexture frames;

        public static ScreenPlayer Build(Transform parent, WallScreen screen, Vector3 position, Quaternion rotation,
            Material frameMaterial, Material imageMaterial, bool lightWall, float spotIntensity)
        {
            float w = screen.WidthMm / 1000f, h = screen.HeightMm / 1000f;
            var root = new GameObject("Screen").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, rotation);

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Bezel";
            frame.transform.SetParent(root, false);
            frame.transform.localScale = new Vector3(w, h, Depth);
            frame.transform.localPosition = new Vector3(0, 0, -Depth / 2f);
            var bezel = frame.GetComponent<Renderer>();
            if (frameMaterial) bezel.sharedMaterial = frameMaterial;

            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Picture";
            surface.transform.SetParent(root, false);
            surface.transform.localPosition = new Vector3(0, 0, -Depth - 0.001f);
            surface.transform.localScale = new Vector3(w - 2 * Border, h - 2 * Border, 1f);
            Destroy(surface.GetComponent<Collider>());
            var material = new Material(imageMaterial);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", Texture2D.blackTexture);
            surface.GetComponent<Renderer>().material = material;

            var text = new GameObject("Caption");
            text.transform.SetParent(root, false);
            text.transform.localPosition = new Vector3(0, -h / 2f - 0.22f, -0.01f);
            var tmp = text.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(w, 0.4f);
            tmp.alignment = TextAlignmentOptions.Top;
            tmp.fontSize = 1.1f;
            tmp.color = lightWall ? new Color(0.12f, 0.11f, 0.1f) : new Color(0.93f, 0.84f, 0.62f);

            // A wash of light on the wall around the screen, for the room's title above it. The
            // picture is unlit, so it is not washed out.
            if (spotIntensity > 0)
            {
                var spot = new GameObject("Wall wash");
                spot.transform.SetParent(root, false);
                spot.transform.localPosition = new Vector3(0f, h / 2f + 1.6f, -2.6f);
                spot.transform.LookAt(root.TransformPoint(new Vector3(0f, h / 2f + 0.4f, 0f)));
                var light = spot.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 85f;
                light.innerSpotAngle = 50f;
                light.range = 7f;
                light.intensity = spotIntensity * 0.8f;
                light.color = new Color(1f, 0.95f, 0.86f);
                light.shadows = LightShadows.None;
            }

            var self = root.gameObject.AddComponent<ScreenPlayer>();
            self.videos = screen.Videos;
            self.caption = tmp;
            self.material = material;
            var player = root.gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            // Frames go to a render texture the screen's own material shows.
            self.frames = new RenderTexture(1280, 720, 0) { name = "Screen frames" };
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = self.frames;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.aspectRatio = VideoAspectRatio.FitInside;
            player.loopPointReached += _ => self.Next();
            // Playing starts only once the stream is prepared; tell listeners then.
            player.started += p =>
            {
                material.SetTexture("_BaseMap", self.frames);
                Debug.Log($"Screen playing {p.url} ({p.width}x{p.height})");
                self.Changed?.Invoke(self);
            };
            player.errorReceived += (_, message) => { Debug.LogWarning($"Video: {message}"); self.Next(); };
            self.player = player;
            self.Show(0);
            return self;
        }

        void Show(int i)
        {
            Current = Mathf.Clamp(i, 0, videos.Count - 1);
            var v = videos[Current];
            var counter = videos.Count > 1 ? $"  <size=70%>({Current + 1}/{videos.Count})</size>" : "";
            caption.text = $"<b>{v.Caption}</b>{counter}" + (string.IsNullOrEmpty(v.Credit) ? "" : $"\n<size=65%>{Shorten(v.Credit, 140)}</size>");
            Changed?.Invoke(this);
        }

        static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, s.LastIndexOf(' ', max - 1) is > 40 and var cut ? cut : max - 1) + "…";

        public void Play(int i)
        {
            if (videos.Count == 0) return;
            Show(i);
            player.url = videos[Current].Url;
            player.Play();
            Changed?.Invoke(this);
        }

        public void Play() { if (player.isPaused && !string.IsNullOrEmpty(player.url)) player.Play(); else Play(Current); Changed?.Invoke(this); }
        public void Pause() { if (player.isPlaying) player.Pause(); Changed?.Invoke(this); }
        public void Toggle() { if (player.isPlaying) Pause(); else Play(); }

        void OnDestroy()
        {
            if (frames) frames.Release();
        }

        public void Stop()
        {
            if (!player) return;
            player.Stop();
            material.SetTexture("_BaseMap", Texture2D.blackTexture);
            Changed?.Invoke(this);
        }

        /// The next film, or stop after the last one.
        void Next()
        {
            if (Current + 1 < videos.Count) Play(Current + 1);
            else { Stop(); Show(0); }
        }
    }
}
