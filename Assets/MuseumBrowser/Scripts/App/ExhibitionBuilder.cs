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
        [Tooltip("Layout JSON: http(s) URL or a path under StreamingAssets. A harvest /rows URL "
               + "(WallCard collection) is hung with the AutoLayout stand-in.")]
        [SerializeField] string layoutSource = "https://127.0.0.1:8011/api/smith/npg/rows?type=painting&hasSize=1&itemsPerPage=40";

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
        [SerializeField] float spotIntensity = 12f;
        [Tooltip("Physical wall plaques. The on-screen label replaces them, so they are off by default.")]
        [SerializeField] bool wallPlaques;

        public IReadOnlyList<Transform> Works => works;
        public IReadOnlyList<WallCard> Cards => cards;
        /// Where a visit starts: just inside the first room, looking into it.
        public Vector3 Entrance { get; private set; }
        public float EntranceYaw { get; private set; }
        public event System.Action<ExhibitionBuilder> Built;

        readonly List<Transform> works = new();
        readonly List<WallCard> cards = new();

        async void Start()
        {
            Exhibition exhibition;
            if (layoutSource.Contains("/rows"))
            {
                var page = await JsonLoader.LoadAsync<HydraCollection<WallCard>>(layoutSource);
                exhibition = AutoLayout.Suite(page.Folio?.Title ?? "Exhibition", page.Members);
            }
            else exhibition = await JsonLoader.LoadAsync<Exhibition>(layoutSource);
            Debug.Log($"Exhibition {exhibition.Code}: {exhibition.Rooms.Count} room(s)");
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
                foreach (var p in wall.Placements.OrderBy(p => p.Order))
                {
                    if (p.Card == null) continue;
                    var local = a + right * (p.XMm / 1000f) + Vector3.up * (p.CenterMm / 1000f);
                    var work = WorkHanger.Hang(root, p.Card, root.TransformPoint(local), rotation,
                        frameMaterial, spotIntensity, wallPlaques);
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
