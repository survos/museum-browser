using System.Collections.Generic;

namespace MuseumBrowser.Core
{
    /// One folio's app-data pack (PLAN.md, "App-data pack format"). Generated from
    /// the folio by the exporter; never edited by hand.
    public sealed class AppDataPack
    {
        public int Version;
        public string Folio;
        public string Title;
        public string GeneratedAt;
        public List<PackItem> Items = new();
    }

    public sealed class PackItem
    {
        public string Id;
        public string Title;
        public WallLabel Label;
        public SizeCm SizeCm;
        public string Img;
        public string Thumb;
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

    public sealed class SizeCm
    {
        public float W;
        public float H;
        public string Kind;
    }
}
