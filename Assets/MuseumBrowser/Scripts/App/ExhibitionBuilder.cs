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
        [Tooltip("http(s) URL of a layout JSON, or a path under StreamingAssets.")]
        [SerializeField] string layoutSource = "exhibitions/npg-sample.json";

        [Header("Room pieces")]
        [Tooltip("Wall block: inner face on local x=0, thickness along +x, length along +z.")]
        [SerializeField] GameObject wallBlock;
        [SerializeField] float wallBlockLength = 6f;
        [SerializeField] float wallBlockHeight = 12f;
        [Tooltip("Floor/ceiling tile with its pivot at a corner, extending +x/+z.")]
        [SerializeField] GameObject floorTile;
        [SerializeField] GameObject ceilingTile;
        [SerializeField] float tileSize = 12f;

        [Header("Works")]
        [SerializeField] Material frameMaterial;
        [SerializeField] float spotIntensity = 12f;

        public IReadOnlyList<Transform> Works => works;
        public event System.Action<ExhibitionBuilder> Built;

        readonly List<Transform> works = new();

        async void Start()
        {
            var exhibition = await JsonLoader.LoadAsync<Exhibition>(layoutSource);
            Debug.Log($"Exhibition {exhibition.Code}: {exhibition.Rooms.Count} room(s)");
            foreach (var room in exhibition.Rooms) BuildRoom(room);
            Built?.Invoke(this);
        }

        void BuildRoom(Room room)
        {
            var root = new GameObject($"Room {room.Code}").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(room.OriginXMm / 1000f, 0, room.OriginYMm / 1000f);
            float width = room.WidthMm / 1000f, depth = room.DepthMm / 1000f, height = room.HeightMm / 1000f;

            Tile(root, floorTile, width, depth, 0f);
            Tile(root, ceilingTile, width, depth, height);

            foreach (var wall in room.Walls)
            {
                var a = new Vector3(wall.X0Mm / 1000f, 0, wall.Y0Mm / 1000f);
                var b = new Vector3(wall.X1Mm / 1000f, 0, wall.Y1Mm / 1000f);
                var right = (b - a).normalized;
                var facing = new Vector3(-right.z, 0, right.x); // looking into the wall
                BuildWall(root, a, b, (wall.HeightMm > 0 ? wall.HeightMm : room.HeightMm) / 1000f);

                var rotation = root.rotation * Quaternion.LookRotation(facing);
                foreach (var p in wall.Placements.OrderBy(p => p.Order))
                {
                    if (p.Card == null) continue;
                    var local = a + right * (p.XMm / 1000f) + Vector3.up * (p.CenterMm / 1000f);
                    works.Add(WorkHanger.Hang(root, p.Card, root.TransformPoint(local), rotation,
                        frameMaterial, spotIntensity));
                }
            }
        }

        void BuildWall(Transform room, Vector3 a, Vector3 b, float height)
        {
            if (!wallBlock) return;
            float length = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(length / wallBlockLength - 0.01f));
            float segment = length / n;
            var dir = (b - a).normalized;
            // The block's length runs along local +z; aiming it at -dir puts its thickness behind the wall.
            var rotation = Quaternion.LookRotation(-dir);
            for (int i = 0; i < n; i++)
            {
                var block = Instantiate(wallBlock, room);
                block.transform.localPosition = a + dir * (segment * (i + 1));
                block.transform.localRotation = rotation;
                block.transform.localScale = new Vector3(1f, height / wallBlockHeight, segment / wallBlockLength);
            }
        }

        void Tile(Transform room, GameObject tile, float width, float depth, float y)
        {
            if (!tile) return;
            int nx = Mathf.Max(1, Mathf.CeilToInt(width / tileSize - 0.01f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(depth / tileSize - 0.01f));
            float sx = width / (nx * tileSize), sz = depth / (nz * tileSize);
            for (int x = 0; x < nx; x++)
            for (int z = 0; z < nz; z++)
            {
                var t = Instantiate(tile, room);
                t.transform.localPosition = new Vector3(x * tileSize * sx, y, z * tileSize * sz);
                t.transform.localScale = new Vector3(sx, 1f, sz);
            }
        }
    }
}
