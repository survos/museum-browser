using System.Collections.Generic;
using System.Linq;

namespace MuseumBrowser.Core
{
    /// Stand-in for harvest's layout pass (harvest task #5): a suite of rooms in a
    /// row, connected by doorways, works hung on each room's side walls along a
    /// shared centre line. Delete once /api/exhibitions/{code}/layout exists.
    public static class AutoLayout
    {
        const int CenterMm = 1520, GapMm = 900, MarginMm = 1200;
        const int WallGapMm = 600;   // between neighbouring rooms (two thin walls)
        const int DoorMm = 2400;

        /// One list of works, split across as many rooms as it fills.
        public static Exhibition Suite(string title, IEnumerable<WallCard> cards,
            int widthMm = 10000, int depthMm = 16000, int heightMm = 5000)
        {
            var queue = new Queue<WallCard>(cards);
            var groups = new List<(string, List<WallCard>)>();
            while (queue.Count > 0 && groups.Count < 8)
                groups.Add(($"{title} · Room {Roman(groups.Count + 1)}", Take(queue, depthMm)));
            return Rooms(title, groups, widthMm, depthMm, heightMm);
        }

        /// One room per group (a "hall" filter: a decade, a theme, a town...).
        public static Exhibition Rooms(string title, IEnumerable<(string title, List<WallCard> cards)> groups,
            int widthMm = 10000, int depthMm = 16000, int heightMm = 5000)
        {
            var rooms = new List<Room>();
            int order = 0;
            foreach (var (roomTitle, cards) in groups)
            {
                int y = rooms.Count * (depthMm + WallGapMm);
                var room = new Room
                {
                    Code = $"R{rooms.Count + 1}", Title = roomTitle, TitleWall = "N",
                    WidthMm = widthMm, DepthMm = depthMm, HeightMm = heightMm, OriginXMm = 0, OriginYMm = y,
                };
                var west = new Wall { Code = "W", Side = "w", X0Mm = 0, Y0Mm = 0, X1Mm = 0, Y1Mm = depthMm, HeightMm = heightMm };
                var north = new Wall { Code = "N", Side = "n", X0Mm = 0, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = depthMm, HeightMm = heightMm };
                var east = new Wall { Code = "E", Side = "e", X0Mm = widthMm, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = 0, HeightMm = heightMm };
                var south = new Wall { Code = "S", Side = "s", X0Mm = widthMm, Y0Mm = 0, X1Mm = 0, Y1Mm = 0, HeightMm = heightMm };
                // Doorways on the centre line: in from the south, on to the north.
                if (rooms.Count > 0) south.Openings.Add(Door(widthMm));
                north.Openings.Add(Door(widthMm));

                var queue = new Queue<WallCard>(cards);
                foreach (var wall in new[] { west, east })
                    foreach (var card in Take(queue, depthMm))
                        wall.Placements.Add(new Placement { ItemId = card.Id, Order = order++, Card = card, CenterMm = CenterMm });
                foreach (var wall in new[] { west, east }) Space(wall, depthMm);

                room.Walls.AddRange(new[] { west, north, east, south });
                rooms.Add(room);
            }
            // The last room is a dead end.
            if (rooms.Count > 0) rooms.Last().Walls.First(w => w.Code == "N").Openings.Clear();
            return new Exhibition { Code = title, Title = title, Rooms = rooms };
        }

        static Opening Door(int widthMm) => new() { FromMm = (widthMm - DoorMm) / 2, ToMm = (widthMm + DoorMm) / 2 };

        /// As many works as fit along one wall.
        static List<WallCard> Take(Queue<WallCard> queue, int wallMm)
        {
            var taken = new List<WallCard>();
            int used = MarginMm * 2 - GapMm;
            while (queue.Count > 0 && used + queue.Peek().Size.HangMm().w + GapMm <= wallMm)
            {
                var card = queue.Dequeue();
                used += card.Size.HangMm().w + GapMm;
                taken.Add(card);
            }
            return taken;
        }

        /// Spread a wall's works evenly between its end margins.
        static void Space(Wall wall, int wallMm)
        {
            var ps = wall.Placements;
            if (ps.Count == 0) return;
            int widths = ps.Sum(p => p.Card.Size.HangMm().w);
            float gap = (wallMm - 2f * MarginMm - widths) / System.Math.Max(1, ps.Count - 1);
            if (ps.Count == 1) { ps[0].XMm = wallMm / 2; return; }
            float x = MarginMm;
            foreach (var p in ps)
            {
                int w = p.Card.Size.HangMm().w;
                p.XMm = (int)(x + w / 2f);
                x += w + gap;
            }
        }

        static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", _ => "VIII" };
    }
}
