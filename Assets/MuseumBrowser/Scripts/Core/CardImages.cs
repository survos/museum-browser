using System.Collections.Generic;
using System.Linq;
using Survos.Folio;
using UnityEngine;

namespace MuseumBrowser.Core
{
    /// What a work shows before and while its image loads, and where the image comes from.
    /// Placeholders: the card's ThumbHash (decoded here), else its average colour.
    /// Cards from a local folio carry no image URLs; they are resolved from the site in
    /// batches (MuseadoImages) just before a room or slide needs them.
    public static class CardImages
    {
        /// Set when cards come from a local folio: the site that signs image URLs.
        public static string ApiBase;
        public static string FolioCode;

        static readonly Dictionary<string, Texture2D> placeholders = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            placeholders.Clear();
            inFlight.Clear();
            unavailable.Clear();
            ApiBase = FolioCode = null;
        }

        /// A blurred preview (ThumbHash) or a flat swatch (average colour), or null.
        public static Texture2D Placeholder(WallCard card)
        {
            var image = card?.Image;
            if (image == null) return null;
            var key = image.Thumbhash ?? image.Color;
            if (key == null) return null;
            if (placeholders.TryGetValue(key, out var cached) && cached) return cached;
            var texture = ThumbHash.ToTexture(image.Thumbhash);
            if (texture == null && ThumbHash.ParseColor(image.Color) is { } color)
            {
                texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Swatch" };
                texture.SetPixel(0, 0, color);
                texture.Apply(false, true);
            }
            if (texture) placeholders[key] = texture;
            return texture;
        }

        /// Width / height from the ThumbHash, when there is one.
        public static float? Aspect(WallCard card)
        {
            var hash = card?.Image?.Thumbhash;
            if (string.IsNullOrEmpty(hash)) return null;
            try { return ThumbHash.AspectRatio(System.Convert.FromBase64String(hash)); }
            catch (System.FormatException) { return null; }
        }

        /// Fill in signed image URLs for these cards (one request per 200), if they have none.
        /// Cards already being resolved by another caller are waited for, not requested twice.
        public static async Awaitable EnsureUrlsAsync(IEnumerable<WallCard> cards)
        {
            if (ApiBase == null || FolioCode == null) return;
            var needed = cards.Where(c => c?.Image != null && c.Image.Thumb == null && c.Image.Medium == null && !unavailable.Contains(c)).ToList();
            var mine = needed.Where(c => inFlight.Add(c)).ToList();
            if (mine.Count > 0)
            {
                try
                {
                    var urls = await MuseadoImages.ResolveAsync(ApiBase, FolioCode, mine.Select(c => c.Id).ToList());
                    foreach (var c in mine)
                        if (urls.TryGetValue(c.Id, out var u)) { c.Image.Thumb = u.Thumb; c.Image.Medium = u.Medium; }
                        else unavailable.Add(c);   // the site has no image for it: keep the placeholder
                }
                catch (System.Exception e) { Debug.LogWarning(e.Message); }   // retried on the next visit
                finally { foreach (var c in mine) inFlight.Remove(c); }
            }
            while (needed.Any(inFlight.Contains)) await Awaitable.NextFrameAsync();
        }

        static readonly HashSet<WallCard> inFlight = new();
        static readonly HashSet<WallCard> unavailable = new();
    }
}
