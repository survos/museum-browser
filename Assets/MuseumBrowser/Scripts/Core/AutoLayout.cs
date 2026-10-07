using System.Collections.Generic;

namespace MuseumBrowser.Core
{
    /// Stand-in for harvest's layout pass (harvest task #5): hangs cards left to
    /// right on the long walls of one rectangular room, on a shared centre line.
    /// Delete once /api/exhibitions/{code}/layout exists.
    public static class AutoLayout
    {
        const int CenterMm = 1520, GapMm = 900, MarginMm = 1500;

        public static Exhibition SingleRoom(string code, IEnumerable<WallCard> cards,
            int widthMm = 12000, int depthMm = 36000, int heightMm = 6000)
        {
            var west = new Wall { Code = "W", Side = "w", X0Mm = 0, Y0Mm = 0, X1Mm = 0, Y1Mm = depthMm, HeightMm = heightMm };
            var north = new Wall { Code = "N", Side = "n", X0Mm = 0, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = depthMm, HeightMm = heightMm };
            var east = new Wall { Code = "E", Side = "e", X0Mm = widthMm, Y0Mm = depthMm, X1Mm = widthMm, Y1Mm = 0, HeightMm = heightMm };
            var south = new Wall { Code = "S", Side = "s", X0Mm = widthMm, Y0Mm = 0, X1Mm = 0, Y1Mm = 0, HeightMm = heightMm };

            var queue = new Queue<WallCard>(cards);
            int order = 0;
            foreach (var wall in new[] { west, east })
            {
                int x = MarginMm;
                while (queue.Count > 0)
                {
                    var card = queue.Peek();
                    int w = card.Size?.WidthMm ?? 600;
                    if (x + w > depthMm - MarginMm) break;
                    queue.Dequeue();
                    wall.Placements.Add(new Placement { ItemId = card.Id, XMm = x + w / 2, CenterMm = CenterMm, Order = order++, Card = card });
                    x += w + GapMm;
                }
            }

            return new Exhibition
            {
                Code = code,
                Title = code,
                Rooms = { new Room { Code = "R1", WidthMm = widthMm, DepthMm = depthMm, HeightMm = heightMm, Walls = { west, north, east, south } } },
            };
        }
    }
}
