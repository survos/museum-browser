using System.Collections.Generic;
using System.Linq;
using MuseumBrowser.Core;
using Survos.Folio;
using UnityEngine;

namespace MuseumBrowser.App
{
    /// Loads an exhibition layout (from harvest, or a bundled sample) and builds it:
    /// rooms from kit pieces, then every placement hung where the layout says.
    /// Layout decisions are made in harvest; this only renders them.
    ///
    /// A folio (?folio=, -folio, or the default) is read from its published SQLite file on
    /// desktop (com.survos.folio: downloaded once, cached), or from the site's WallCard API
    /// on the Web until that has a sqlite-wasm backend. Works show their ThumbHash placeholder
    /// at once; images load room by room as the visitor walks in.
    public sealed class ExhibitionBuilder : MonoBehaviour
    {
        [Header("Museado API (zm / recordia.org)")]
        [Tooltip("Base for /api/... paths while running in the Editor: local zm through the m4 dev tunnel.")]
        [SerializeField] string editorApiBase = "https://m4-zm.survos.org";
        [Tooltip("Base for /api/... paths in player builds (desktop and Web).")]
        [SerializeField] string playerApiBase = "https://recordia.org";
        [Tooltip("Folio to show in the Editor, as if the page URL had ?folio=...")]
        [SerializeField] string editorFolio = "";
        [Tooltip("Folio a desktop player opens (dataset key, e.g. mus/fpus); -folio on the command line overrides it.")]
        [SerializeField] string playerFolio = "mus/fpus";
        [Tooltip("Read a folio from its SQLite file where the platform can (desktop), instead of the WallCard API.")]
        [SerializeField] bool readFolioFile = true;
        [Tooltip("At most this many works are hung, spread over the folio's decades.")]
        [SerializeField] int maxWorks = 240;
        [Tooltip("Loads images room by room as it changes rooms (found in the scene when unset).")]
        [SerializeField] Visitor visitor;

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

        [Header("Finish")]
        [Tooltip("Walls of rooms of paintings (dark red cloth).")]
        [SerializeField] Material paintingWalls;
        [Tooltip("Walls of rooms of photographs (warm white).")]
        [SerializeField] Material printWalls;
        [Tooltip("Floor for rooms of photographs; the kit's concrete tiles are used when unset.")]
        [SerializeField] Material woodFloor;

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
        /// 0..1 while loading: download, records, then hanging (images load per room afterwards).
        public float Progress { get; private set; }
        public string Stage { get; private set; } = "Opening the galleries…";
        readonly List<List<HungWork>> roomWorks = new();
        public int RoomCount { get; private set; }
        /// Raised with a human-readable message when loading fails (shown on screen).
        public event System.Action<string> Failed;
        public IReadOnlyList<WallCard> Cards => cards;
        /// Each room: title, where a visitor stands on entering it, and its extent along the
        /// suite (for the floor plan).
        public sealed class RoomStop
        {
            public string Title;
            public Vector3 Position;
            public float Yaw;
            public float Start, Length, Width;
            public int Works;
        }

        public IReadOnlyList<RoomStop> RoomStops => roomStops;
        readonly List<RoomStop> roomStops = new();
        /// Where a visit starts: just inside the first room, looking into it.
        public Vector3 Entrance { get; private set; }
        public float EntranceYaw { get; private set; }
        public event System.Action<ExhibitionBuilder> Built;

        readonly List<Transform> works = new();
        readonly List<WallCard> cards = new();

        /// In the Editor: the local zm. On the Web: ?api= when the embedding page passes it (the
        /// player is hosted on object storage, not on the site), else the page's own origin.
        /// Desktop builds use playerApiBase.
        public string ApiBase
        {
            get
            {
                if (CommandLine("-api") is { Length: > 0 } arg) return arg.TrimEnd('/');
                if (Application.isEditor) return editorApiBase;
                if (QueryParam("api") is { Length: > 0 } api) return api.TrimEnd('/');
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

        /// ?folio=mus/fpus on the page URL (Web), editorFolio (Editor), or -folio / playerFolio (desktop).
        string RequestedFolio()
        {
            if (CommandLine("-folio") is { Length: > 0 } arg) return arg;
            if (Application.isEditor) return string.IsNullOrWhiteSpace(editorFolio) ? null : editorFolio.Trim();
            if (Application.platform == RuntimePlatform.WebGLPlayer) return QueryParam("folio");
            return string.IsNullOrWhiteSpace(playerFolio) ? null : playerFolio.Trim();
        }

        /// The value after a command-line flag ("-folio mus/fpus"), or null.
        static string CommandLine(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

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
            if (RequestedFolio() is { } fileFolio && readFolioFile && FolioDatabase.IsSupported)
                exhibition = await LoadFolioFile(fileFolio);
            else if (RequestedFolio() is { } folio)
            {
                // The WallCard API (Web): its first works with images, in time order.
                var page = await JsonLoader.LoadAsync<HydraCollection<WallCard>>(
                    Resolve($"/api/{folio}/rows?hasImage=1&itemsPerPage={Mathf.Clamp(maxWorks, 1, 200)}"));
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
            Progress = 0.8f;
            Stage = "Hanging the works…";
            Title = exhibition.Title;
            RoomCount = exhibition.Rooms.Count;
            if (exhibition.Rooms.Sum(r => r.Walls.Sum(w => w.Placements.Count)) == 0)
                throw new System.InvalidOperationException("This folio has no works with images to hang.");
            foreach (var room in exhibition.Rooms) BuildRoom(room);
            if (exhibition.Rooms.Count > 0)
            {
                var first = exhibition.Rooms[0];
                Entrance = roomStops[0].Position;
                EntranceYaw = transform.eulerAngles.y;
            }
            Progress = 1f;
            Stage = "";
            if (!visitor) visitor = FindAnyObjectByType<Visitor>();
            if (visitor) visitor.RoomChanged += LoadRoomImages;
            Built?.Invoke(this);
            // Every work already shows its placeholder; the first room's images come first.
            LoadRoomImages(0);
        }

        void OnDestroy()
        {
            if (visitor) visitor.RoomChanged -= LoadRoomImages;
        }

        /// The folio's SQLite file, from the hub's catalog (downloaded once, then cached).
        async Awaitable<Exhibition> LoadFolioFile(string key)
        {
            FolioCatalogEntry entry = null;
            try
            {
                entry = FolioCatalog.Find(await FolioCatalog.LoadAsync(ApiBase), key)
                        ?? throw new System.InvalidOperationException($"{ApiBase} publishes no folio \"{key}\".");
            }
            catch (System.IO.IOException e) when (System.IO.File.Exists(FolioArchive.PathFor(key)))
            {
                Debug.LogWarning($"Opening the cached copy of {key}: {e.Message}");
            }

            FolioReader folio;
            if (entry != null)
            {
                var size = entry.SizeBytes is { } bytes ? $" ({bytes / 1048576f:0.#} MB, once)" : "";
                Stage = $"Downloading {entry.Title ?? key}{size}…";
                folio = await FolioReader.OpenAsync(entry, new ProgressTo(p => Progress = 0.05f + 0.6f * p));
            }
            else folio = await FolioReader.OpenFileAsync(FolioArchive.PathFor(key), prepare: true);

            using (folio)
            {
                Progress = 0.7f;
                Stage = "Reading the records…";
                var index = await folio.IndexAsync("obj");
                var items = await folio.ItemsAsync(FolioCards.Choose(index, maxWorks));
                var chosen = items.Select(i => FolioCards.Map(i, folio.Code)).ToList();
                Debug.Log($"Folio {folio.Code}: {index.Count} records with images; hanging {chosen.Count}, "
                          + $"{chosen.Count(c => c.Image?.Thumbhash != null)} with ThumbHash");
                FolioCode = folio.Code;
                Folio = new FolioInfo { Code = folio.Code, Title = folio.Title };
                TotalRecords = index.Count;
                CardImages.ApiBase = ApiBase;
                CardImages.FolioCode = folio.Code;
                return AutoLayout.Suite(folio.Title, chosen);
            }
        }

        sealed class ProgressTo : System.IProgress<float>
        {
            readonly System.Action<float> report;
            public ProgressTo(System.Action<float> report) => this.report = report;
            public void Report(float value) => report(value);
        }

        /// Images for this room, then its neighbours: signed URLs first (one request per room),
        /// then the images, a few at a time.
        public async void LoadRoomImages(int room)
        {
            try
            {
                foreach (int r in new[] { room, room + 1, room - 1 })
                {
                    if (r < 0 || r >= roomWorks.Count) continue;
                    var pending = roomWorks[r].Where(w => !w.ImageRequested).ToList();
                    if (pending.Count == 0) continue;
                    await CardImages.EnsureUrlsAsync(pending.Select(w => w.Card));
                    var loads = pending.Select(w => w.LoadImageAsync()).ToList();
                    foreach (var load in loads) await load;
                    if (r == room) Debug.Log($"Room {r + 1}: {loads.Count} images loaded");
                }
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void BuildRoom(Room room)
        {
            var root = new GameObject($"Room {room.Code}").transform;
            root.SetParent(transform, false);
            var hung = new List<HungWork>();
            roomWorks.Add(hung);
            root.localPosition = new Vector3(room.OriginXMm / 1000f, 0, room.OriginYMm / 1000f);
            roomStops.Add(new RoomStop
            {
                Title = room.Title ?? room.Code,
                // Just inside the entrance doorway (doors sit toward the right-hand side).
                Position = root.TransformPoint(new Vector3(room.WidthMm / 1000f - 1.7f, 0.1f, 1.2f)),
                Yaw = root.eulerAngles.y,
                Start = room.OriginYMm / 1000f, Length = room.DepthMm / 1000f, Width = room.WidthMm / 1000f,
                Works = room.Walls.Sum(w => w.Placements.Count),
            });
            float width = room.WidthMm / 1000f, depth = room.DepthMm / 1000f, height = room.HeightMm / 1000f;

            // The floor runs a little past both end walls so doorways have a threshold.
            bool prints = room.Style == "prints";
            if (prints && woodFloor) Slab(root, woodFloor, width, depth + 2 * wallThickness, -wallThickness);
            else Tile(root, floorTile, width, depth + 2 * wallThickness, 0f, -wallThickness);
            var wallMaterial = prints ? printWalls : paintingWalls;
            Tile(root, ceilingTile, width, depth, height, 0f);

            foreach (var wall in room.Walls)
            {
                var a = new Vector3(wall.X0Mm / 1000f, 0, wall.Y0Mm / 1000f);
                var b = new Vector3(wall.X1Mm / 1000f, 0, wall.Y1Mm / 1000f);
                var right = (b - a).normalized;
                var facing = new Vector3(-right.z, 0, right.x); // looking into the wall
                float wallHeight = (wall.HeightMm > 0 ? wall.HeightMm : room.HeightMm) / 1000f;
                BuildWall(root, a, b, wallHeight, wall.Openings, wallMaterial);

                var rotation = root.rotation * Quaternion.LookRotation(facing);
                foreach (var t in wall.Texts)
                    AddWallText(root, a + right * (t.XMm / 1000f) + Vector3.up * (t.CenterMm / 1000f) - facing * 0.01f,
                        Quaternion.LookRotation(facing), t.Text, t.Kind == "title", prints);
                foreach (var p in wall.Placements.OrderBy(p => p.Order))
                {
                    if (p.Card == null) continue;
                    var local = a + right * (p.XMm / 1000f) + Vector3.up * (p.CenterMm / 1000f);
                    var work = WorkHanger.Hang(root, p.Card, p.HangMm(), root.TransformPoint(local), rotation,
                        frameMaterial, imageMaterial, spotIntensity, wallPlaques);
                    work.Index = works.Count;
                    work.Room = roomWorks.Count - 1;
                    hung.Add(work);
                    works.Add(work.transform);
                    cards.Add(p.Card);
                }
            }
        }

        void BuildWall(Transform room, Vector3 a, Vector3 b, float height, List<Opening> openings, Material finish)
        {
            wallFinish = finish;
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

        Material wallFinish;

        /// One slab floor with the material's texture tiled per metre.
        static void Slab(Transform room, Material material, float width, float depth, float z0)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Floor";
            slab.transform.SetParent(room, false);
            slab.transform.localPosition = new Vector3(width / 2f, -0.05f, z0 + depth / 2f);
            slab.transform.localScale = new Vector3(width, 0.1f, depth);
            var r = slab.GetComponent<Renderer>();
            r.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetVector("_BaseMap_ST", new Vector4(width / 2.5f, depth / 2.5f, 0, 0));
            r.SetPropertyBlock(block);
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
                if (wallFinish)
                    foreach (var r in block.GetComponentsInChildren<Renderer>())
                        r.sharedMaterials = System.Linq.Enumerable.Repeat(wallFinish, r.sharedMaterials.Length).ToArray();
            }
        }

        /// A group's title, painted on the wall above it.
        static void AddWallText(Transform room, Vector3 localPosition, Quaternion localRotation, string text,
            bool title, bool lightWall)
        {
            var go = new GameObject("Wall text");
            go.transform.SetParent(room, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(title ? 6f : 5f, title ? 0.9f : 0.5f);
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.fontSize = title ? 5.5f : 2.4f;
            tmp.characterSpacing = title ? 10f : 4f;
            // Dark lettering on warm white; pale gold on dark red cloth.
            tmp.color = lightWall ? new Color(0.07f, 0.06f, 0.05f) : new Color(0.93f, 0.84f, 0.62f);
            tmp.fontStyle = TMPro.FontStyles.SmallCaps;
            tmp.text = text;
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
