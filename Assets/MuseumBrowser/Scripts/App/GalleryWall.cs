using System.Collections.Generic;
using System.Text;
using MuseumBrowser.Core;
using TMPro;
using UnityEngine;

namespace MuseumBrowser.App
{
    /// Hangs a pack's works along this transform's right axis, at true scale,
    /// centered at eye height, each with a wall label beside it.
    public sealed class GalleryWall : MonoBehaviour
    {
        [SerializeField] string packPath = "packs/npg-paintings/app-data.json";
        [SerializeField] int maxWorks = 24;
        [SerializeField] float centerHeight = 1.55f;
        [SerializeField] float gap = 0.9f;
        [SerializeField] float frameBorder = 0.04f;
        [SerializeField] float frameDepth = 0.05f;
        [SerializeField] Material frameMaterial;

        public IReadOnlyList<Transform> Works => works;
        public event System.Action<GalleryWall> Hung;

        readonly List<Transform> works = new();

        async void Start()
        {
            var pack = await PackLoader.LoadAsync(packPath);
            Debug.Log($"Loaded pack {pack.Folio}: {pack.Items.Count} items");

            float x = 0f;
            foreach (var item in pack.Items)
            {
                if (works.Count >= maxWorks) break;
                float w = item.SizeCm.W / 100f, h = item.SizeCm.H / 100f;
                var work = Hang(item, x + w / 2f, w, h);
                works.Add(work);
                x += w + gap;
            }
            Hung?.Invoke(this);
        }

        Transform Hang(PackItem item, float centerX, float w, float h)
        {
            var root = new GameObject(item.Title).transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(centerX, centerHeight, 0f);

            // The frame is the measured outer size; the image sits inside its border.
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(root, false);
            frame.transform.localScale = new Vector3(w, h, frameDepth);
            frame.transform.localPosition = new Vector3(0, 0, -frameDepth / 2f);
            if (frameMaterial) frame.GetComponent<Renderer>().sharedMaterial = frameMaterial;
            Destroy(frame.GetComponent<Collider>());

            var canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
            canvas.name = "Image";
            canvas.transform.SetParent(root, false);
            canvas.transform.localPosition = new Vector3(0, 0, -frameDepth - 0.001f);
            canvas.transform.localScale = new Vector3(w - 2 * frameBorder, h - 2 * frameBorder, 1f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            canvas.GetComponent<Renderer>().material = material;
            LoadImage(item.Img, material);

            AddLabel(root, item.Label, w);
            return root;
        }

        static async void LoadImage(string url, Material material)
        {
            try { material.SetTexture("_BaseMap", await ImageLoader.LoadAsync(url)); }
            catch (System.Exception e) { Debug.LogWarning(e.Message); }
        }

        void AddLabel(Transform work, WallLabel label, float workWidth)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(work, false);
            go.transform.localPosition = new Vector3(workWidth / 2f + 0.25f, -0.25f, -0.01f);

            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = new Vector2(0.32f, 0.4f);
            text.rectTransform.pivot = new Vector2(0f, 1f);
            text.fontSize = 0.16f;
            text.color = new Color(0.12f, 0.12f, 0.12f);
            text.text = Tombstone(label);
        }

        static string Tombstone(WallLabel l)
        {
            if (l == null) return "";
            if (!string.IsNullOrEmpty(l.Tombstone)) return l.Tombstone;
            var sb = new StringBuilder();
            void Line(string s, string style = null)
            {
                if (string.IsNullOrEmpty(s)) return;
                sb.AppendLine(style == null ? s : $"<{style}>{s}</{style.Split('=')[0]}>");
            }
            Line(l.Creator, "b");
            Line(l.Title, "i");
            Line(l.Subject);
            Line(l.Date);
            Line(l.Medium);
            Line(l.Dimensions, "size=80%");
            Line(l.Credit, "size=70%");
            Line(l.Accession, "size=70%");
            return sb.ToString();
        }
    }
}
