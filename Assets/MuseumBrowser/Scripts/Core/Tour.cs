using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Survos.Folio;

namespace MuseumBrowser.Core
{
    /// A walking tour from a tour folio (Curatescape): stops in order, each a place with a
    /// story, its photos and its recordings. Hung as an exhibition, one room per stop.
    public sealed class Tour
    {
        public string Id;
        public string Title;
        public List<TourStop> Stops = new();
    }

    public sealed class TourStop
    {
        public string Id;
        /// The obj row holding the stop's story, photos and recordings.
        public string ObjectId;
        public string Title;
        public string Subtitle;
        public string Lede;
        /// The story itself; paragraphs separated by blank lines.
        public string Text;
        public string Address;
        public string Sponsor;
        public string SourceUrl;
        public string License;
        public double? Latitude, Longitude;
        public List<WallCard> Photos = new();
        public List<Recording> Recordings = new();
    }

    /// A recording from the source (Curatescape audio or video), played in the stop's room.
    public sealed class Recording
    {
        /// "audio" or "video".
        public string Kind;
        public string Url;
        public string Caption;
        /// The source's credit line, verbatim ("1985 | WJZ-TV").
        public string Credit;

        public bool IsVideo => Kind == "video";
    }

    public static class TourReader
    {
        public sealed class Entry
        {
            public string Id;
            public string Title;
            public int Stops;
        }

        /// The folio's tours (story rows), most stops first.
        public static async Task<List<Entry>> ListAsync(FolioReader folio)
        {
            var rows = await folio.Database.QueryAsync(
                "SELECT i.local_id, i.label, (SELECT COUNT(*) FROM link l WHERE l.left_core = 'story' AND l.right_core = 'stop' "
                + "AND l.left_id = i.local_id) AS stops FROM item i WHERE i.core_id = ? ORDER BY stops DESC",
                folio.Code + ":story");
            return rows.Select(r => new Entry { Id = r.GetString("local_id"), Title = r.GetString("label"), Stops = r.GetInt32("stops") ?? 0 }).ToList();
        }

        /// A tour by id ("curatescape:baltimore:30") or by part of its title ("Sites & Sounds").
        public static async Task<Tour> LoadAsync(FolioReader folio, string tour)
        {
            var tours = await ListAsync(folio);
            var entry = tours.FirstOrDefault(t => t.Id == tour)
                        ?? tours.FirstOrDefault(t => t.Title != null && t.Title.IndexOf(tour, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? throw new System.InvalidOperationException(
                            $"{folio.Code} has no tour \"{tour}\". Tours: {string.Join("; ", tours.Take(12).Select(t => $"{t.Id} {t.Title}"))}");

            var stopRows = await folio.Database.QueryAsync(
                "SELECT s.local_id, s.label, s.dto_data, s.extras, CAST(json_extract(l.extras, '$.ordinal') AS INTEGER) AS ordinal "
                + "FROM link l JOIN item s ON s.core_id = ? AND s.local_id = l.right_id "
                + "WHERE l.left_core = 'story' AND l.right_core = 'stop' AND l.left_id = ? ORDER BY ordinal, s.local_id",
                folio.Code + ":stop", entry.Id);

            var stops = stopRows.Select(r =>
            {
                var fields = Fields(r.GetString("dto_data"), r.GetString("extras"));
                return new TourStop
                {
                    Id = r.GetString("local_id"),
                    ObjectId = Text(fields, "objectId"),
                    Title = Text(fields, "title") ?? r.GetString("label"),
                    Subtitle = Text(fields, "subtitle"),
                    Latitude = (double?)fields["latitude"],
                    Longitude = (double?)fields["longitude"],
                };
            }).ToList();

            var objects = (await folio.ItemsAsync(stops.Where(s => s.ObjectId != null).Select(s => s.ObjectId).Distinct().ToList()))
                .ToDictionary(i => i.LocalId);
            foreach (var stop in stops)
                if (stop.ObjectId != null && objects.TryGetValue(stop.ObjectId, out var item))
                    await FillAsync(folio, stop, item);

            return new Tour { Id = entry.Id, Title = entry.Title, Stops = stops };
        }

        static async Task FillAsync(FolioReader folio, TourStop stop, FolioItem item)
        {
            var fields = Fields(item.DtoJson, item.ExtrasJson);
            stop.Title = Text(fields, "title") ?? stop.Title;
            stop.Subtitle = Text(fields, "subtitle") ?? stop.Subtitle;
            stop.Lede = Text(fields, "lede");
            stop.Text = Text(fields, "description");
            stop.Sponsor = Text(fields, "sponsor");
            stop.SourceUrl = Text(fields, "citationUrl") ?? Text(fields, "sourceUrl") ?? Text(fields, "url");
            stop.License = Text(fields, "rightsUri") ?? Text(fields, "rights");
            stop.Address = fields["address"] is JArray parts
                ? string.Join(", ", parts.Select(p => p.ToString().Trim()).Where(p => p != ""))
                : Text(fields, "address");
            stop.Latitude ??= (double?)fields["latitude"];
            stop.Longitude ??= (double?)fields["longitude"];

            // media[] carries each file's caption and credit line; older builds only have pages.
            if (fields["media"] is JArray media && media.Count > 0)
            {
                foreach (var m in media.OfType<JObject>().OrderBy(m => (int?)m["seq"] ?? 0))
                {
                    var kind = (string)m["kind"];
                    var url = (string)m["url"];
                    if (string.IsNullOrEmpty(url)) continue;
                    if (kind == "image") stop.Photos.Add(Photo(stop, (int?)m["seq"] ?? stop.Photos.Count + 1, url, (string)m["caption"], (string)m["description"]));
                    else if (kind is "audio" or "video")
                        stop.Recordings.Add(new Recording { Kind = kind, Url = url, Caption = (string)m["caption"], Credit = (string)m["description"] });
                }
            }
            else
            {
                var pages = await folio.Database.QueryAsync(
                    "SELECT seq, url, dense_summary FROM page WHERE row_id = ? AND (type IS NULL OR type = 'photo') ORDER BY seq, id",
                    $"{folio.Code}:obj:{item.LocalId}");
                foreach (var p in pages)
                    stop.Photos.Add(Photo(stop, p.GetInt32("seq") ?? stop.Photos.Count + 1, p.GetString("url"), p.GetString("dense_summary"), null));
            }
        }

        static WallCard Photo(TourStop stop, int seq, string url, string caption, string credit) => new()
        {
            Id = $"{stop.ObjectId}#{seq}",
            Title = caption,
            Label = new WallLabel { Title = caption ?? stop.Title, Credit = credit },
            // Curatescape's "fullsize" is already a web-sized derivative; no proxy needed.
            Image = new CardImage { Medium = url },
            SourceUrl = stop.SourceUrl,
            License = stop.License,
        };

        /// DTO fields over extras, as folio-bundle merges them.
        static JObject Fields(string dto, string extras)
        {
            var fields = Parse(extras);
            foreach (var p in Parse(dto).Properties())
                if (p.Value.Type != JTokenType.Null || fields[p.Name] == null) fields[p.Name] = p.Value;
            return fields;
        }

        static JObject Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new JObject();
            try { return JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return new JObject(); }
        }

        static string Text(JObject fields, string key) =>
            fields[key] is JValue { Value: not null } v && v.ToString().Trim() is { Length: > 0 } s ? s : null;
    }
}
