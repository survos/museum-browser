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

        public static HungWork Hang(Transform parent, WallCard card, (int w, int h) sizeMm, Vector3 position,
            Quaternion rotation, Material frameMaterial, Material imageMaterial, float spotIntensity, bool plaque)
        {
            var (wMm, hMm) = sizeMm;
            float w = wMm / 1000f, h = hMm / 1000f;

            var root = new GameObject(card.Title ?? card.Id).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, rotation);

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(root, false);
            frame.transform.localScale = new Vector3(w, h, FrameDepth);
            frame.transform.localPosition = new Vector3(0, 0, -FrameDepth / 2f);
            if (frameMaterial) frame.GetComponent<Renderer>().sharedMaterial = frameMaterial;
            // The frame's collider stays: clicking a work walks the visitor to it.

            var canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
            canvas.name = "Image";
            canvas.transform.SetParent(root, false);
            canvas.transform.localPosition = new Vector3(0, 0, -FrameDepth - 0.001f);
            canvas.transform.localScale = new Vector3(w - 2 * FrameBorder, h - 2 * FrameBorder, 1f);
            Object.Destroy(canvas.GetComponent<Collider>());
            // A copy of a material asset, not Shader.Find: a build only contains shaders that a
            // material references, so a shader found by name at runtime is missing on the Web.
            var material = new Material(imageMaterial);
            canvas.GetComponent<Renderer>().material = material;
            // Paint the placeholder now; the image itself loads when the room is visited.
            if (CardImages.Placeholder(card) is { } placeholder) material.SetTexture("_BaseMap", placeholder);

            var work = root.gameObject.AddComponent<HungWork>();
            work.Card = card;
            work.Material = material;
            work.Frame = frame.transform;
            work.Canvas = canvas.transform;
            work.Border = FrameBorder;
            // No known shape (no physical or pixel size): the ThumbHash gives the proportions
            // now, the loaded image exactly; the frame shrinks to fit its allotted box.
            bool unknownShape = card.Size == null || !(card.Size.WidthMm > 0 || card.Size.WPx > 0);
            if (unknownShape)
            {
                work.FitTo = new Vector2(w, h);
                if (CardImages.Aspect(card) is { } aspect) work.Fit(aspect);
            }

            if (plaque) AddLabel(root, card.Label, w);
            if (spotIntensity > 0) AddSpot(root, h, spotIntensity);
            return work;
        }

        /// A white wall plaque beside the work, its centre at museum label height.
        static void AddLabel(Transform work, WallLabel label, float workWidth)
        {
            const float PlaqueW = 0.30f, PlaqueH = 0.24f, LabelHeight = 1.35f;
            var plaque = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plaque.name = "Label";
            plaque.transform.SetParent(work, false);
            float y = LabelHeight - work.position.y; // local y, so the plaque sits at 1.35 m whatever the work's size
            plaque.transform.localPosition = new Vector3(workWidth / 2f + 0.3f + PlaqueW / 2f, y, -0.006f);
            plaque.transform.localScale = new Vector3(PlaqueW, PlaqueH, 0.012f);
            plaque.GetComponent<Renderer>().material.SetColor("_BaseColor", new Color(0.96f, 0.95f, 0.92f));
            Object.Destroy(plaque.GetComponent<Collider>());

            var go = new GameObject("Text");
            go.transform.SetParent(work, false);
            go.transform.localPosition = new Vector3(workWidth / 2f + 0.3f + PlaqueW / 2f, y, -0.014f);
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = new Vector2(PlaqueW - 0.04f, PlaqueH - 0.04f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.1f;
            text.fontSizeMax = 0.42f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = new Color(0.1f, 0.1f, 0.1f);
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

        public static string Tombstone(WallLabel l)
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
