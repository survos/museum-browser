using MuseumBrowser.Core;
using UnityEngine;

namespace MuseumBrowser.App
{
    /// A hung work: lets a click find its index, and loads its image when its room is
    /// visited. Until then it shows the card's placeholder (ThumbHash or colour).
    public sealed class HungWork : MonoBehaviour
    {
        public int Index;
        public int Room;
        public WallCard Card { get; internal set; }
        internal Material Material;
        /// No known shape: fit the frame to the image's proportions within this box.
        internal Vector2? FitTo;
        internal Transform Frame, Canvas;
        internal float Border;

        public bool ImageRequested { get; private set; }

        /// Loads the image once (the URL must already be known; see CardImages.EnsureUrlsAsync).
        public async Awaitable LoadImageAsync()
        {
            if (ImageRequested) return;
            ImageRequested = true;
            if (Card.Image?.Best is not { } url) { ImageRequested = false; return; }   // URL not resolved yet
            try
            {
                var texture = await ImageLoader.LoadAsync(url);
                if (!this) return;
                Material.SetTexture("_BaseMap", texture);
                if (texture.width > 0 && texture.height > 0) Fit((float)texture.width / texture.height);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(e.Message);
                ImageRequested = false;   // try again on the next visit to the room
            }
        }

        /// Shrink the frame to these proportions inside the box it was given.
        internal void Fit(float aspect)
        {
            if (FitTo is not { } box || !Frame) return;
            float w = box.x, h = box.x / aspect;
            if (h > box.y) { h = box.y; w = box.y * aspect; }
            Frame.localScale = new Vector3(w, h, Frame.localScale.z);
            Canvas.localScale = new Vector3(w - 2 * Border, h - 2 * Border, 1f);
        }
    }
}
