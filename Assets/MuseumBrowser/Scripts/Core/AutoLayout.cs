using System.Collections.Generic;
using System.Linq;

namespace MuseumBrowser.Core
{
    /// Stand-in for harvest's layout pass (harvest task #5): a suite of rooms in a
    /// row, connected by doorways, works hung on each room's side walls along a
    /// shared centre line. Delete once /api/exhibitions/{code}/layout exists.
    public static class AutoLayout
    {
        const int CenterMm = 1520, GapMm = 1000, MarginMm = 1200;
        const int WallGapMm = 600;   // between neighbouring rooms (two thin walls)
        const int DoorMm = 2400;

        public static Exhibition Suite(string title, IEnumerable<WallCard> cards,
            int widthMm = 10000, int depthMm = 16000, int heightMm = 5000)
        {
            var queue = new Queue<WallCard>(cards);
            var rooms = new List<Room>();
            int order = 0;
            while (queue.Count > 0 && rooms.Count < 8)
            {
                int y = rooms.Count * (depthMm + WallGapMm);
                var room = new Room
                {
                    Code = $"R{rooms.Count + 1}", Title = $"{title} · Room {Roman(rooms.Count + 1)}", TitleWall = "N",
                    WidthMm = widthMm, DepthMm = depthMm, HeightMm = heightMm, OriginXMm = 0, OriginYMm = y,
                };
                var west = new Wall { Code = "W", Side = "w", X0Mm = 0, Y0Mm = 0, X1Mm = 0, Y1Mm = depthMm, HeightMm = heightMm };
                var north = new Wall { Code = "N", Side = "n", X0Mm = 0, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = depthMm, HeightMm = heightMm };
                var east = new Wall { Code = "E", Side = "e", X0Mm = widthMm, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = 0, HeightMm = heightMm };
                var south = new Wall { Code = "S", Side = "s", X0Mm = widthMm, Y0Mm = 0, X1Mm = 0, Y1Mm = 0, HeightMm = heightMm };
                // Doorways on the centre line: in from the south, on to the north.
                var door = new Opening { FromMm = (widthMm - DoorMm) / 2, ToMm = (widthMm + DoorMm) / 2 };
                if (rooms.Count > 0) south.Openings.Add(door);
                north.Openings.Add(new Opening { FromMm = door.FromMm, ToMm = door.ToMm });

                foreach (var wall in new[] { west, east })
                {
                    int x = MarginMm;
                    while (queue.Count > 0)
                    {
                        int w = queue.Peek().Size?.WidthMm ?? 600;
                        if (x + w > depthMm - MarginMm) break;
                        var card = queue.Dequeue();
                        wall.Placements.Add(new Placement { ItemId = card.Id, XMm = x + w / 2, CenterMm = CenterMm, Order = order++, Card = card });
                        x += w + GapMm;
                    }
                }
                room.Walls.AddRange(new[] { west, north, east, south });
                rooms.Add(room);
            }
            // The last room is a dead end.
            rooms.Last().Walls.First(w => w.Code == "N").Openings.Clear();
            return new Exhibition { Code = title, Title = title, Rooms = rooms };
        }

        static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", _ => "VIII" };
    }
}
