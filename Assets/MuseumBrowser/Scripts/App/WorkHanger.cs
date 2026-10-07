using System.Text;
using MuseumBrowser.Core;
using TMPro;
using UnityEngine;

namespace MuseumBrowser.App
{
    /// Builds one hung work: frame, image, wall label and spotlight. The work's
    /// forward axis points into the wall; everything visible sits on local -z.
    public static class WorkHanger
    {
        const float FrameBorder = 0.04f;
        const float FrameDepth = 0.05f;

        public static Transform Hang(Transform parent, WallCard card, Vector3 position, Quaternion rotation,
            Material frameMaterial, float spotIntensity)
        {
            float w = (card.Size?.WidthMm ?? 600) / 1000f;
            float h = (card.Size?.HeightMm ?? 800) / 1000f;

            var root = new GameObject(card.Title ?? card.Id).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, rotation);

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(root, false);
            frame.transform.localScale = new Vector3(w, h, FrameDepth);
            frame.transform.localPosition = new Vector3(0, 0, -FrameDepth / 2f);
            if (frameMaterial) frame.GetComponent<Renderer>().sharedMaterial = frameMaterial;
            Object.Destroy(frame.GetComponent<Collider>());

            var canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
            canvas.name = "Image";
            canvas.transform.SetParent(root, false);
            canvas.transform.localPosition = new Vector3(0, 0, -FrameDepth - 0.001f);
            canvas.transform.localScale = new Vector3(w - 2 * FrameBorder, h - 2 * FrameBorder, 1f);
            Object.Destroy(canvas.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            canvas.GetComponent<Renderer>().material = material;
            if (card.Image?.Best is { } url) LoadImage(url, material);

            AddLabel(root, card.Label, w);
            if (spotIntensity > 0) AddSpot(root, h, spotIntensity);
            return root;
        }

        static async void LoadImage(string url, Material material)
        {
            try { material.SetTexture("_BaseMap", await ImageLoader.LoadAsync(url)); }
            catch (System.Exception e) { Debug.LogWarning(e.Message); }
        }

        static void AddLabel(Transform work, WallLabel label, float workWidth)
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

        static void AddSpot(Transform work, float h, float intensity)
        {
            var go = new GameObject("Spot");
            go.transform.SetParent(work, false);
            // Above and in front of the work, aimed at its centre.
            go.transform.localPosition = new Vector3(0f, 2.2f, -2.0f);
            go.transform.LookAt(work.position);
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = Mathf.Clamp(Mathf.Atan2(h, 2.0f) * Mathf.Rad2Deg * 1.6f + 20f, 30f, 80f);
            light.innerSpotAngle = light.spotAngle * 0.6f;
            light.range = 6f;
            light.intensity = intensity;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.shadows = LightShadows.None;
        }

        static string Tombstone(WallLabel l)
        {
            if (l == null) return "";
            if (!string.IsNullOrEmpty(l.Tombstone)) return l.Tombstone;
            var sb = new StringBuilder();
            void Line(string s, string open = null, string close = null)
            {
                if (!string.IsNullOrEmpty(s)) sb.AppendLine(open + s + close);
            }
            Line(l.Creator, "<b>", "</b>");
            Line(l.Title, "<i>", "</i>");
            Line(l.Subject);
            Line(l.Date);
            Line(l.Medium);
            Line(l.Dimensions, "<size=80%>", "</size>");
            Line(l.Credit, "<size=70%>", "</size>");
            Line(l.Accession, "<size=70%>", "</size>");
            return sb.ToString();
        }
    }
}
