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
        public List<Placement> Placements = new();
    }

    public sealed class Placement
    {
        public string ItemId;
        /// Centre of the work, measured along the wall from its left end.
        public int XMm;
        /// Height of the work's centre above the floor (hanging line, usually 1520).
        public int CenterMm = 1520;
        public int Order;
        public WallCard Card;
    }

    /// One object as served by harvest's /api/{folioCode}/rows (WallCard).
    public sealed class WallCard
    {
        public string Id;
        public string Title;
        public WallLabel Label;
        public CardSize Size;
        public CardImage Image;
        public List<AudioStop> Audio = new();
        public string SourceUrl;
        public string License;
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
    }

    public sealed class CardImage
    {
        public string Thumb;
        public string Medium;
        public string Full;

        /// Low-res first: thumbnails are what this phase uses.
        public string Best => Thumb ?? Medium ?? Full;
    }

    public sealed class AudioStop
    {
        public string Url;
        public int? DurationSec;
        public string Tour;
        public string TranscriptUrl;
    }
}
