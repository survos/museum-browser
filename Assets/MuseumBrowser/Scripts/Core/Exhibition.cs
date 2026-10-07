using System.Collections.Generic;

namespace MuseumBrowser.Core
{
    // Exhibition layout as produced by harvest (PLAN.md, "Exhibition layout").
    // All lengths are integer millimetres. Room coordinates: x across the width,
    // y along the depth; Unity maps them to world x and z.

    public sealed class Exhibition
    {
        public string Code;
        public string Title;
        public List<Room> Rooms = new();
    }

    public sealed class Room
    {
        public string Code;
        public string Title;
        /// Wall code that carries the room title (usually the wall facing the entrance).
        public string TitleWall;
        public int WidthMm;
        public int DepthMm;
        public int HeightMm;
        public int OriginXMm;
        public int OriginYMm;
        public List<Wall> Walls = new();
    }

    /// A straight wall from (X0,Y0) to (X1,Y1): left end to right end as seen
    /// when facing it from inside the room.
    public sealed class Wall
    {
        public string Code;
        public string Side;
        public int X0Mm, Y0Mm, X1Mm, Y1Mm;
        public int HeightMm;
        public List<Opening> Openings = new();
        public List<Placement> Placements = new();
        /// Text on the wall: a group's title above its works ("Cedar Falls · 1951–58").
        public List<WallText> Texts = new();
    }

    public sealed class WallText
    {
        public int XMm;
        public int CenterMm;
        public string Text;
    }

    /// A doorway in a wall, measured along the wall from its left end.
    public sealed class Opening
    {
        public int FromMm;
        public int ToMm;
        public int HeightMm = 3000;
    }

    public sealed class Placement
    {
        public string ItemId;
        /// Centre of the work, measured along the wall from its left end.
        public int XMm;
        /// Height of the work's centre above the floor (hanging line, usually 1520).
        public int CenterMm = 1520;
        public int Order;
        /// Size to hang at, chosen by the layout (e.g. a group's feature print is larger).
        /// Null: the card's own size.
        public int? WidthMm;
        public int? HeightMm;
        public WallCard Card;

        public (int w, int h) HangMm() =>
            WidthMm is > 0 && HeightMm is > 0 ? (WidthMm.Value, HeightMm.Value) : Card?.HangMm() ?? (600, 800);
    }

    /// One object as served by harvest's /api/{folioCode}/rows (WallCard).
    public sealed class WallCard
    {
        public string Id;
        public string Title;
        public WallLabel Label;
        public int? Year;
        public List<string> Tags = new();
        public List<string> Subjects = new();
        public CardSize Size;
        public CardImage Image;
        public List<AudioStop> Audio = new();
        public string SourceUrl;
        public string License;

        /// Size to hang at; unknown sizes hang as a 60 x 80 cm default.
        public (int w, int h) HangMm() => Size?.HangMm() ?? (600, 800);
    }

    /// The museum wall label (tombstone). Every field is optional.
    public sealed class WallLabel
    {
        public string Creator;
        public string Title;
        public string Subject;
        public string Date;
        public string Place;
        public string Medium;
        public string Dimensions;
        public string Credit;
        public string Accession;
        public string Tombstone;
    }

    public sealed class CardSize
    {
        public int? WidthMm;
        public int? HeightMm;
        public int? DepthMm;
        public string Kind;
        public int? WPx;
        public int? HPx;

        /// Physical size to hang at. Photographs known only in pixels are hung as
        /// exhibition prints with the given long side, keeping their proportions.
        /// True when only a pixel size is known (photographs), i.e. hung as prints.
        public bool IsPrint => !(WidthMm is > 0 && HeightMm is > 0) && WPx is > 0 && HPx is > 0;

        public (int w, int h) HangMm(int printLongSideMm = 1000)
        {
            if (WidthMm is > 0 && HeightMm is > 0) return (WidthMm.Value, HeightMm.Value);
            if (WPx is > 0 && HPx is > 0)
            {
                float scale = (float)printLongSideMm / System.Math.Max(WPx.Value, HPx.Value);
                return ((int)(WPx.Value * scale), (int)(HPx.Value * scale));
            }
            return (600, 800);
        }
    }

    public sealed class CardImage
    {
        public string Thumb;
        public string Medium;
        public string Full;

        /// Low-res first: thumbnails are what this phase uses.
        public string Best => Thumb ?? Medium ?? Shrink(Full);

        // Until harvest serves JPEG thumbnails, ask the Smithsonian image server for a
        // small rendition rather than the full-resolution original.
        static string Shrink(string url) =>
            url != null && url.Contains("ids.si.edu") && !url.Contains("max=") ? url + "&max=400" : url;
    }

    public sealed class AudioStop
    {
        public string Url;
        public int? DurationSec;
        public string Tour;
        public string TranscriptUrl;
    }
}
