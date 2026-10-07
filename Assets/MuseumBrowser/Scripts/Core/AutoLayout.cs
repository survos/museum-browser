using System.Collections.Generic;
using System.Linq;

namespace MuseumBrowser.Core
{
    /// Stand-in for harvest's layout pass (harvest task #5): a suite of rooms in a
    /// row, connected by doorways. Rooms are sized to what hangs in them. Works
    /// with physical sizes hang in one row on the 1520 mm centre line; photographs
    /// (prints) hang salon-style in two rows. Delete once
    /// /api/exhibitions/{code}/layout exists.
    public static class AutoLayout
    {
        const int CenterMm = 1520;
        const int RowLowMm = 1200, RowHighMm = 2150;   // salon rows for prints
        const int GapMm = 800, PrintGapMm = 350, MarginMm = 900;
        const int WallGapMm = 600;   // between neighbouring rooms (two thin walls)
        const int DoorMm = 2000;
        const int DoorInsetMm = 700;      // doors sit to the right, leaving the far wall for a hero work
        const int HeroMaxMm = 3000;
        const int MinDepthMm = 8000, MaxDepthMm = 18000;

        /// One list of works, split into rooms: by decade when most works have a year
        /// (thin decades merge with the next), else by count.
        public static Exhibition Suite(string title, IEnumerable<WallCard> cards)
        {
            var all = cards.ToList();
            if (all.Count(c => c.Year.HasValue) * 3 >= all.Count * 2)
            {
                var rooms = new List<(string, List<WallCard>)>();
                var pending = new List<WallCard>();
                int? from = null;
                foreach (var decade in all.Where(c => c.Year.HasValue).GroupBy(c => c.Year.Value / 10 * 10).OrderBy(g => g.Key))
                {
                    from ??= decade.Key;
                    pending.AddRange(decade);
                    if (pending.Count >= 8)
                    {
                        rooms.Add((from == decade.Key ? $"{decade.Key}s" : $"{from}s–{decade.Key}s", pending));
                        pending = new List<WallCard>();
                        from = null;
                    }
                }
                pending.AddRange(all.Where(c => !c.Year.HasValue));
                if (pending.Count > 0)
                {
                    if (rooms.Count > 0 && pending.Count < 8) rooms[^1].Item2.AddRange(pending);
                    else rooms.Add((from.HasValue ? $"{from}s" : "Undated", pending));
                }
                return Rooms(title, rooms);
            }
            return SuiteByCount(title, all);
        }

        static Exhibition SuiteByCount(string title, IEnumerable<WallCard> cards)
        {
            var queue = new Queue<WallCard>(cards);
            var groups = new List<(string, List<WallCard>)>();
            while (queue.Count > 0 && groups.Count < 10)
            {
                var room = new List<WallCard>();
                // Fill two long walls (two rows each for prints) up to the maximum depth.
                int capacity = 2 * Capacity(queue.Peek());
                int used = 0;
                while (queue.Count > 0 && used + Width(queue.Peek()) <= capacity)
                {
                    used += Width(queue.Peek());
                    room.Add(queue.Dequeue());
                }
                if (room.Count == 0) room.Add(queue.Dequeue());
                groups.Add(($"{title} · Room {Roman(groups.Count + 1)}", room));
            }
            return Rooms(title, groups);
        }

        /// One room per group (a "hall" filter: a decade, a theme, a town...).
        public static Exhibition Rooms(string title, IEnumerable<(string title, List<WallCard> cards)> groups)
        {
            var rooms = new List<Room>();
            int order = 0, y = 0;
            foreach (var (roomTitle, cards) in groups)
            {
                bool prints = cards.Count > 0 && cards.Count(c => c.IsPrint) * 2 > cards.Count;
                int rows = prints ? 2 : 1;
                int gap = prints ? PrintGapMm : GapMm;
                int tallest = cards.Count == 0 ? 1000 : cards.Max(c => c.HangMm().h);
                int widthMm = prints ? 7000 : 9000;
                int heightMm = System.Math.Max(3600, (prints ? RowHighMm : CenterMm) + tallest / 2 + 900);

                // Depth: just enough wall for the works (split over two walls and the rows).
                int wallRun = cards.Sum(c => c.HangMm().w + gap) / (2 * rows) + 2 * MarginMm;
                // Prints are grouped below, which sizes the room itself; start from the minimum.
                int depthMm = prints ? MinDepthMm : System.Math.Clamp(wallRun + 600, MinDepthMm, MaxDepthMm);

                var room = new Room
                {
                    Code = $"R{rooms.Count + 1}", Title = roomTitle, TitleWall = "N", Style = prints ? "prints" : "paintings",
                    WidthMm = widthMm, DepthMm = depthMm, HeightMm = heightMm, OriginXMm = 0, OriginYMm = y,
                };

                var west = new Wall { Code = "W", Side = "w", X0Mm = 0, Y0Mm = 0, X1Mm = 0, Y1Mm = depthMm, HeightMm = heightMm };
                var north = new Wall { Code = "N", Side = "n", X0Mm = 0, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = depthMm, HeightMm = heightMm };
                var east = new Wall { Code = "E", Side = "e", X0Mm = widthMm, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = 0, HeightMm = heightMm };
                var south = new Wall { Code = "S", Side = "s", X0Mm = widthMm, Y0Mm = 0, X1Mm = 0, Y1Mm = 0, HeightMm = heightMm };
                if (rooms.Count > 0) south.Openings.Add(SouthDoor(widthMm));
                north.Openings.Add(Door(widthMm));

                // The far wall, left of the doorway: the room's title, and for photographs a hero
                // print -- the room's highest-scoring work, enlarged like an exhibition panel.
                int farSpan = widthMm - DoorMm - DoorInsetMm - 600;
                int farCenter = 300 + farSpan / 2;
                int titleCenter = heightMm - 650;
                var groupCards = cards;
                if (prints && cards.Count >= 6)
                {
                    var hero = cards.OrderByDescending(Score).First();
                    groupCards = cards.Where(c => c != hero).ToList();
                    int maxH = titleCenter - 450 - 700;
                    var (hw, hh) = hero.Size?.HangMm(HeroMaxMm) ?? (2400, 1800);
                    float k = System.Math.Min(1f, System.Math.Min((float)(farSpan - 400) / hw, (float)maxH / hh));
                    hw = (int)(hw * k); hh = (int)(hh * k);
                    north.Placements.Add(new Placement
                    {
                        ItemId = hero.Id, Card = hero, Order = order++, XMm = farCenter,
                        CenterMm = 700 + hh / 2, WidthMm = hw, HeightMm = hh,
                    });
                    titleCenter = System.Math.Min(titleCenter, 700 + hh + 450);
                }
                north.Texts.Add(new WallText { XMm = farCenter, CenterMm = titleCenter, Text = roomTitle, Kind = "title" });

                if (prints)
                    HangGroups(groupCards, new[] { west, east }, ref depthMm, ref order);
                else
                {
                    // Works with physical sizes: one row at true size on the centre line.
                    var queue = new Queue<WallCard>(cards);
                    foreach (var wall in new[] { west, east })
                    {
                        var row = new List<WallCard>();
                        int used = 2 * MarginMm - gap;
                        while (queue.Count > 0 && used + queue.Peek().HangMm().w + gap <= depthMm)
                        {
                            used += queue.Peek().HangMm().w + gap;
                            row.Add(queue.Dequeue());
                        }
                        Spread(wall, row, depthMm, CenterMm, ref order);
                    }
                }
                room.DepthMm = depthMm;
                west.Y1Mm = depthMm; east.Y0Mm = depthMm;
                north.Y0Mm = north.Y1Mm = depthMm;
                room.Walls.AddRange(new[] { west, north, east, south });
                rooms.Add(room);
                y += room.DepthMm + WallGapMm;
            }
            if (rooms.Count > 0) rooms.Last().Walls.First(w => w.Code == "N").Openings.Clear();
            return new Exhibition { Code = title, Title = title, Rooms = rooms };
        }

        // ---- Museum-style groups for photographs (prints) -------------------------------------

        const int FeatureLongMm = 1300, SupportLongMm = 700, InGroupGapMm = 140, GroupGapMm = 1300;
        const int GroupCenterMm = 1550;

        /// How strongly a work deserves to be featured. Placeholder: more text = more to say.
        /// Later: likes, curation score, ...
        public static System.Func<WallCard, float> Score = c => (c.Label?.Title ?? c.Title ?? "").Length;

        sealed class Group
        {
            public readonly List<(WallCard card, int dx, int dy, int w, int h)> Items = new();
            public int Width, Top;
            public string Title;
        }

        /// How works are grouped on a wall, and what the group is called: the work's subject
        /// that is most shared within the room ("Leisure", "Fairs and Festivals"), else its most
        /// shared tag, else its town; plus the years. Set per room by HangGroups.
        static System.Func<WallCard, string> GroupKey = c => City(c.Label?.Place) ?? "";

        static System.Func<WallCard, string> SharedKey(List<WallCard> cards)
        {
            var subjects = cards.SelectMany(c => c.Subjects ?? new List<string>()).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var tags = cards.SelectMany(c => c.Tags ?? new List<string>()).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            string Best(List<string> values, Dictionary<string, int> counts) =>
                values?.Where(v => counts.TryGetValue(v, out var n) && n >= 3).OrderByDescending(v => counts[v]).FirstOrDefault();
            return c => Best(c.Subjects, subjects) ?? Best(c.Tags, tags) ?? City(c.Label?.Place) ?? "";
        }

        static string City(string place) => string.IsNullOrWhiteSpace(place) ? null : place.Split(',')[0].Trim();

        static string Title(List<WallCard> members)
        {
            var key = members.GroupBy(GroupKey).OrderByDescending(g => g.Count()).First().Key;
            var years = members.Where(c => c.Year.HasValue).Select(c => c.Year.Value).ToList();
            string span = years.Count == 0 ? null
                : years.Min() == years.Max() ? years.Min().ToString()
                : $"{years.Min()}–{years.Max() % 100:00}";
            return string.Join(" · ", new[] { key, span }.Where(s => !string.IsNullOrEmpty(s)));
        }

        /// Photographs are hung as groups: related prints (same year, then place) together,
        /// the highest-scoring one larger as the group's feature, the others in a two-row grid
        /// beside it; groups are spaced well apart. Sizes are the layout's choice.
        static void HangGroups(List<WallCard> cards, Wall[] walls, ref int depthMm, ref int order)
        {
            GroupKey = SharedKey(cards);
            // Related works together: by group key (subject/tag/place), in time order; big sets are split
            // into groups of up to six, and leftovers of one or two join a neighbouring group.
            var buckets = cards.GroupBy(GroupKey)
                .Select(b => b.OrderBy(c => c.Year ?? int.MaxValue).ToList())
                .OrderBy(b => b.Min(c => c.Year ?? int.MaxValue))
                .ToList();
            var sets = new List<List<WallCard>>();
            var small = new List<WallCard>();
            foreach (var b in buckets)
            {
                if (b.Count <= 2) { small.AddRange(b); continue; }
                for (int i = 0; i < b.Count; i += 6) sets.Add(b.Skip(i).Take(6).ToList());
            }
            for (int i = 0; i < small.Count; i += 5) sets.Add(small.Skip(i).Take(5).ToList());
            var groups = sets.Select(BuildGroup).ToList();

            // Balance groups across the walls, then size the room to the longer wall.
            var perWall = walls.Select(_ => new List<Group>()).ToArray();
            var lengths = new int[walls.Length];
            foreach (var g in groups)
            {
                int k = System.Array.IndexOf(lengths, lengths.Min());
                perWall[k].Add(g);
                lengths[k] += g.Width + GroupGapMm;
            }
            depthMm = System.Math.Max(depthMm, lengths.Max() - GroupGapMm + 2 * MarginMm);

            for (int k = 0; k < walls.Length; k++)
            {
                var list = perWall[k];
                if (list.Count == 0) continue;
                int total = list.Sum(g => g.Width);
                float gap = list.Count > 1 ? (depthMm - 2f * MarginMm - total) / (list.Count - 1) : 0;
                float x = list.Count == 1 ? (depthMm - total) / 2f : MarginMm;
                foreach (var g in list)
                {
                    walls[k].Texts.Add(new WallText { XMm = (int)(x + g.Width / 2f), CenterMm = GroupCenterMm + g.Top + 260, Text = g.Title });
                    foreach (var (card, dx, dy, w, h) in g.Items)
                        walls[k].Placements.Add(new Placement
                        {
                            ItemId = card.Id, Card = card, Order = order++,
                            XMm = (int)(x + dx), CenterMm = GroupCenterMm + dy, WidthMm = w, HeightMm = h,
                        });
                    x += g.Width + gap;
                }
            }
        }

        static Group BuildGroup(List<WallCard> members)
        {
            var g = new Group();
            var feature = members.OrderByDescending(Score).First();
            var (fw, fh) = feature.Size?.HangMm(FeatureLongMm) ?? (FeatureLongMm, FeatureLongMm);
            g.Items.Add((feature, fw / 2, 0, fw, fh));
            int x = fw + InGroupGapMm;

            // The rest: a two-row grid to the right of the feature, centred on it vertically.
            var rest = members.Where(c => c != feature).ToList();
            for (int i = 0; i < rest.Count; i += 2)
            {
                var top = rest[i];
                var (tw, th) = top.Size?.HangMm(SupportLongMm) ?? (SupportLongMm, SupportLongMm);
                if (i + 1 < rest.Count)
                {
                    var bottom = rest[i + 1];
                    var (bw, bh) = bottom.Size?.HangMm(SupportLongMm) ?? (SupportLongMm, SupportLongMm);
                    int col = System.Math.Max(tw, bw);
                    int half = (th + InGroupGapMm + bh) / 2;
                    g.Items.Add((top, x + col / 2, half - th / 2, tw, th));
                    g.Items.Add((bottom, x + col / 2, -(half - bh / 2), bw, bh));
                    x += col + InGroupGapMm;
                }
                else
                {
                    g.Items.Add((top, x + tw / 2, 0, tw, th));
                    x += tw + InGroupGapMm;
                }
            }
            g.Width = x - InGroupGapMm;
            g.Top = g.Items.Max(it => it.dy + it.h / 2);
            g.Title = Title(members);
            return g;
        }

        static int Width(WallCard c) => c.HangMm().w + (c.IsPrint ? PrintGapMm : GapMm);

        /// Usable length of one long wall (all rows) at the maximum room depth.
        static int Capacity(WallCard sample) =>
            (MaxDepthMm - 2 * MarginMm) * (sample.IsPrint ? 2 : 1);

        /// Doorways sit toward the right side (as seen walking in), so both rooms' far walls have
        /// space on the left. The south wall runs right to left, so its offset mirrors.
        static Opening Door(int widthMm) => new() { FromMm = widthMm - DoorInsetMm - DoorMm, ToMm = widthMm - DoorInsetMm };
        static Opening SouthDoor(int widthMm) => new() { FromMm = DoorInsetMm, ToMm = DoorInsetMm + DoorMm };

        /// Spread one row evenly between the wall's end margins.
        static void Spread(Wall wall, List<WallCard> row, int wallMm, int centerMm, ref int order)
        {
            if (row.Count == 0) return;
            int widths = row.Sum(c => c.HangMm().w);
            float gap = row.Count > 1 ? (wallMm - 2f * MarginMm - widths) / (row.Count - 1) : 0;
            float x = row.Count == 1 ? (wallMm - widths) / 2f : MarginMm;
            foreach (var c in row)
            {
                int w = c.HangMm().w;
                wall.Placements.Add(new Placement { ItemId = c.Id, XMm = (int)(x + w / 2f), CenterMm = centerMm, Order = order++, Card = c });
                x += w + gap;
            }
        }

        static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", _ => "X" };
    }
}
