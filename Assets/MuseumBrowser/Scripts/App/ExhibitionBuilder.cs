using System.Collections.Generic;
using System.Linq;
using MuseumBrowser.Core;
using UnityEngine;

namespace MuseumBrowser.App
{
    /// Loads an exhibition layout (from harvest, or a bundled sample) and builds it:
    /// rooms from kit pieces, then every placement hung where the layout says.
    /// Layout decisions are made in harvest; this only renders them.
    public sealed class ExhibitionBuilder : MonoBehaviour
    {
        [Header("Museado API (zm / recordia.org)")]
        [Tooltip("Base for /api/... paths while running in the Editor: local zm through the m4 dev tunnel.")]
        [SerializeField] string editorApiBase = "https://m4-zm.survos.org";
        [Tooltip("Base for /api/... paths in player builds (desktop and Web).")]
        [SerializeField] string playerApiBase = "https://recordia.org";
        [Tooltip("Folio to show in the Editor, as if the page URL had ?folio=...")]
        [SerializeField] string editorFolio = "";

        [Tooltip("Layout JSON: /api/... path, http(s) URL, or a path under StreamingAssets. A /rows "
               + "path (WallCard collection) is hung with the AutoLayout stand-in.")]
        [SerializeField] string layoutSource = "/api/smith/npg/rows?type=painting&hasSize=1&itemsPerPage=40";

        [System.Serializable]
        public sealed class RoomSource
        {
            public string title;
            [Tooltip("/api/{folio}/rows path: the filter that defines this room (a decade, a theme, a town...).")]
            public string url;
        }

        [Tooltip("If set, one room per source (stand-in for harvest's layout pass); overrides layoutSource.")]
        [SerializeField] List<RoomSource> roomSources = new();

        [Header("Room pieces")]
        [Tooltip("Wall block: inner face on local x=0, thickness along +x, length along +z.")]
        [SerializeField] GameObject wallBlock;
        [SerializeField] float wallBlockLength = 6f;
        [SerializeField] float wallBlockHeight = 12f;
        [SerializeField] float wallBlockThickness = 3f;
        [Tooltip("Gallery walls are thin; the kit block is 3 m thick.")]
        [SerializeField] float wallThickness = 0.3f;
        [Tooltip("Floor/ceiling tile with its pivot at a corner, extending +x/+z.")]
        [SerializeField] GameObject floorTile;
        [SerializeField] GameObject ceilingTile;
        [SerializeField] float tileSize = 12f;

        [Header("Works")]
        [SerializeField] Material frameMaterial;
        [Tooltip("URP/Unlit material the photos are drawn with (copied per work).")]
        [SerializeField] Material imageMaterial;
        [SerializeField] float spotIntensity = 12f;
        [Tooltip("Physical wall plaques. The on-screen label replaces them, so they are off by default.")]
        [SerializeField] bool wallPlaques;

        public IReadOnlyList<Transform> Works => works;
        public string Title { get; private set; }
        /// Folio code and metadata from the collection, when loaded by folio (else null).
        public string FolioCode { get; private set; }
        public FolioInfo Folio { get; private set; }
        public int TotalRecords { get; private set; }
        public string SiteUrl => ApiBase;
        /// 0..1 while loading: the layout/records first, then the images.
        public float Progress { get; private set; }
        public string Stage { get; private set; } = "Opening the galleries…";
        int imagesTotal, imagesDone;
        public int RoomCount { get; private set; }
        /// Raised with a human-readable message when loading fails (shown on screen).
        public event System.Action<string> Failed;
        public IReadOnlyList<WallCard> Cards => cards;
        /// Where a visit starts: just inside the first room, looking into it.
        public Vector3 Entrance { get; private set; }
        public float EntranceYaw { get; private set; }
        public event System.Action<ExhibitionBuilder> Built;

        readonly List<Transform> works = new();
        readonly List<WallCard> cards = new();

        /// In the Editor: the local zm. On the Web: the site that serves the page (zm embeds the
        /// build, so the API is same-origin). Desktop builds use playerApiBase.
        public string ApiBase
        {
            get
            {
                if (Application.isEditor) return editorApiBase;
                if (Application.platform == RuntimePlatform.WebGLPlayer && Origin(Application.absoluteURL) is { } origin)
                    return origin;
                return playerApiBase;
            }
        }

        static string Origin(string url)
        {
            if (string.IsNullOrEmpty(url) || !System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri)) return null;
            return uri.GetLeftPart(System.UriPartial.Authority);
        }

        /// ?folio=mus/fpus on the page URL (Web) or editorFolio (Editor).
        string RequestedFolio() =>
            Application.isEditor ? (string.IsNullOrWhiteSpace(editorFolio) ? null : editorFolio.Trim()) : QueryParam("folio");

        /// A query-string parameter of the page URL (Web builds), or null.
        public static string QueryParam(string name)
        {
            var url = Application.absoluteURL;
            int q = url?.IndexOf('?') ?? -1;
            if (q < 0) return null;
            foreach (var pair in url.Substring(q + 1).Split('&'))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && kv[0] == name) return System.Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
            return null;
        }

        /// "/api/..." paths are resolved against the Museado API base; anything else is used as is.
        string Resolve(string source) => source.StartsWith("/") ? ApiBase.TrimEnd('/') + source : source;

        async void Start()
        {
            try { await Load(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(e.Message);
            }
        }

        async Awaitable Load()
        {
            Progress = 0.05f;
            Stage = "Fetching the collection…";
            Exhibition exhibition;
            if (RequestedFolio() is { } folio)
            {
                // A folio passed in by the embedding page: hang its first works with images.
                var page = await JsonLoader.LoadAsync<HydraCollection<WallCard>>(
                    Resolve($"/api/{folio}/rows?hasImage=1&itemsPerPage=48"));
                FolioCode = folio;
                Folio = page.Folio;
                TotalRecords = page.TotalItems;
                exhibition = AutoLayout.Suite(page.Folio?.Title ?? folio, page.Members);
            }
            else if (roomSources.Count > 0)
            {
                var groups = new List<(string, List<WallCard>)>();
                foreach (var source in roomSources)
                {
                    var page = await JsonLoader.LoadAsync<HydraCollection<WallCard>>(Resolve(source.url));
                    groups.Add((source.title, page.Members));
                }
                exhibition = AutoLayout.Rooms(name, groups);
            }
            else if (layoutSource.Contains("/rows"))
            {
                var page = await JsonLoader.LoadAsync<HydraCollection<WallCard>>(Resolve(layoutSource));
                Folio = page.Folio;
                FolioCode = page.Folio?.Code;
                TotalRecords = page.TotalItems;
                exhibition = AutoLayout.Suite(page.Folio?.Title ?? "Exhibition", page.Members);
            }
            else exhibition = await JsonLoader.LoadAsync<Exhibition>(Resolve(layoutSource));
            Debug.Log($"Exhibition {exhibition.Code}: {exhibition.Rooms.Count} room(s)");
            Progress = 0.3f;
            Stage = "Hanging the works…";
            Title = exhibition.Title;
            RoomCount = exhibition.Rooms.Count;
            if (exhibition.Rooms.Sum(r => r.Walls.Sum(w => w.Placements.Count)) == 0)
                throw new System.InvalidOperationException("This folio has no works with images to hang.");
            foreach (var room in exhibition.Rooms) BuildRoom(room);
            if (exhibition.Rooms.Count > 0)
            {
                var first = exhibition.Rooms[0];
                Entrance = transform.TransformPoint(new Vector3((first.OriginXMm + first.WidthMm / 2) / 1000f, 0.1f,
                    first.OriginYMm / 1000f + 1.2f));
                EntranceYaw = transform.eulerAngles.y;
            }
            Built?.Invoke(this);
        }

        void ImageDone()
        {
            imagesDone++;
            Progress = 0.3f + 0.7f * imagesDone / Mathf.Max(1, imagesTotal);
            Stage = imagesDone < imagesTotal ? $"Loading images… {imagesDone} of {imagesTotal}" : "";
        }

        void BuildRoom(Room room)
        {
            var root = new GameObject($"Room {room.Code}").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(room.OriginXMm / 1000f, 0, room.OriginYMm / 1000f);
            float width = room.WidthMm / 1000f, depth = room.DepthMm / 1000f, height = room.HeightMm / 1000f;

            // The floor runs a little past both end walls so doorways have a threshold.
            Tile(root, floorTile, width, depth + 2 * wallThickness, 0f, -wallThickness);
            Tile(root, ceilingTile, width, depth, height, 0f);

            foreach (var wall in room.Walls)
            {
                var a = new Vector3(wall.X0Mm / 1000f, 0, wall.Y0Mm / 1000f);
                var b = new Vector3(wall.X1Mm / 1000f, 0, wall.Y1Mm / 1000f);
                var right = (b - a).normalized;
                var facing = new Vector3(-right.z, 0, right.x); // looking into the wall
                float wallHeight = (wall.HeightMm > 0 ? wall.HeightMm : room.HeightMm) / 1000f;
                BuildWall(root, a, b, wallHeight, wall.Openings);
                if (wall.Code == room.TitleWall && !string.IsNullOrEmpty(room.Title))
                    AddRoomTitle(root, a, b, facing, room.Title);

                var rotation = root.rotation * Quaternion.LookRotation(facing);
                foreach (var t in wall.Texts)
                    AddWallText(root, a + right * (t.XMm / 1000f) + Vector3.up * (t.CenterMm / 1000f) - facing * 0.01f,
                        Quaternion.LookRotation(facing), t.Text);
                foreach (var p in wall.Placements.OrderBy(p => p.Order))
                {
                    if (p.Card == null) continue;
                    var local = a + right * (p.XMm / 1000f) + Vector3.up * (p.CenterMm / 1000f);
                    imagesTotal++;
                    var work = WorkHanger.Hang(root, p.Card, p.HangMm(), root.TransformPoint(local), rotation,
                        frameMaterial, imageMaterial, spotIntensity, wallPlaques, ImageDone);
                    work.gameObject.AddComponent<HungWork>().Index = works.Count;
                    works.Add(work);
                    cards.Add(p.Card);
                }
            }
        }

        void BuildWall(Transform room, Vector3 a, Vector3 b, float height, List<Opening> openings)
        {
            if (!wallBlock) return;
            float length = Vector3.Distance(a, b);
            var dir = (b - a).normalized;
            // Solid runs between doorways; a lintel spans each doorway above its height.
            float from = 0f;
            foreach (var o in openings.OrderBy(o => o.FromMm))
            {
                float o0 = o.FromMm / 1000f, o1 = o.ToMm / 1000f, oh = Mathf.Min(o.HeightMm / 1000f, height);
                WallRun(room, a, dir, from, o0, 0f, height);
                WallRun(room, a, dir, o0, o1, oh, height - oh);
                from = o1;
            }
            WallRun(room, a, dir, from, length, 0f, height);
        }

        void WallRun(Transform room, Vector3 a, Vector3 dir, float from, float to, float bottom, float height)
        {
            float length = to - from;
            if (length < 0.01f || height < 0.01f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(length / wallBlockLength - 0.01f));
            float segment = length / n;
            // The block's length runs along local +z; aiming it at -dir puts its thickness behind the wall.
            var rotation = Quaternion.LookRotation(-dir);
            for (int i = 0; i < n; i++)
            {
                var block = Instantiate(wallBlock, room);
                block.transform.localPosition = a + dir * (from + segment * (i + 1)) + Vector3.up * bottom;
                block.transform.localRotation = rotation;
                block.transform.localScale = new Vector3(wallThickness / wallBlockThickness,
                    height / wallBlockHeight, segment / wallBlockLength);
            }
        }

        /// A group's title, painted on the wall above it.
        static void AddWallText(Transform room, Vector3 localPosition, Quaternion localRotation, string text)
        {
            var go = new GameObject("Wall text");
            go.transform.SetParent(room, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(5f, 0.5f);
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.fontSize = 2.4f;
            tmp.characterSpacing = 4f;
            tmp.color = new Color(0.07f, 0.06f, 0.05f);
            tmp.fontStyle = TMPro.FontStyles.SmallCaps;
            tmp.text = text;
        }

        void AddRoomTitle(Transform room, Vector3 a, Vector3 b, Vector3 facing, string title)
        {
            var go = new GameObject("Room title");
            go.transform.SetParent(room, false);
            go.transform.localPosition = (a + b) / 2f + Vector3.up * 3.6f - facing * 0.02f;
            go.transform.localRotation = Quaternion.LookRotation(facing);
            var text = go.AddComponent<TMPro.TextMeshPro>();
            text.rectTransform.sizeDelta = new Vector2(Vector3.Distance(a, b) * 0.8f, 0.8f);
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.fontSize = 3f;
            text.characterSpacing = 8f;
            text.color = new Color(0.15f, 0.14f, 0.13f);
            text.text = title.ToUpperInvariant();
        }

        void Tile(Transform room, GameObject tile, float width, float depth, float y, float z0)
        {
            if (!tile) return;
            int nx = Mathf.Max(1, Mathf.CeilToInt(width / tileSize - 0.01f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(depth / tileSize - 0.01f));
            float sx = width / (nx * tileSize), sz = depth / (nz * tileSize);
            for (int x = 0; x < nx; x++)
            for (int z = 0; z < nz; z++)
            {
                var t = Instantiate(tile, room);
                t.transform.localPosition = new Vector3(x * tileSize * sx, y, z0 + z * tileSize * sz);
                t.transform.localScale = new Vector3(sx, 1f, sz);
            }
        }
    }
}
