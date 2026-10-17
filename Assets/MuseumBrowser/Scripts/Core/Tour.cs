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
        /// The tour's own introduction, read before the first stop.
        public string Description;
        public string CoverUrl;
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

        /// The folio's tours, most stops first. Two shapes are read: the agreed model, a `tour`
        /// core linked `has_stop` straight to `obj` places (AIC; Curatescape once rebuilt), and
        /// the older Curatescape one, `story` rows linked to `stop` rows that point at the place.
        public static async Task<List<Entry>> ListAsync(FolioReader folio)
        {
            var rows = await folio.Database.QueryAsync(
                "SELECT i.local_id, i.label, (SELECT COUNT(*) FROM link l WHERE l.left_core IN ('tour', 'story') AND l.right_core IN ('obj', 'stop') "
                + "AND l.link_type_id LIKE '%:has_stop:%' AND l.left_id = i.local_id) AS stops "
                + "FROM item i WHERE i.core_id IN (?, ?) ORDER BY stops DESC",
                folio.Code + ":tour", folio.Code + ":story");
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

            var tourRows = await folio.Database.QueryAsync(
                "SELECT core_id, dto_data, extras FROM item WHERE core_id IN (?, ?) AND local_id = ?",
                folio.Code + ":tour", folio.Code + ":story", entry.Id);
            var tourFields = tourRows.Count > 0 ? Fields(tourRows[0].GetString("dto_data"), tourRows[0].GetString("extras")) : new JObject();
            var tourCore = tourRows.Count > 0 ? tourRows[0].GetString("core_id").Substring(folio.Code.Length + 1) : "story";

            // has_stop points at the place itself (tour -> obj) or at a stop row (story -> stop),
            // whose objectId (or, in older builds, its id) names the place.
            var stopRows = await folio.Database.QueryAsync(
                "SELECT l.right_core, l.extras AS link_extras, s.local_id, s.label, s.dto_data, s.extras, "
                + "CAST(json_extract(l.extras, '$.ordinal') AS INTEGER) AS ordinal "
                + "FROM link l JOIN item s ON s.core_id = ? || l.right_core AND s.local_id = l.right_id "
                + "WHERE l.left_core = ? AND l.left_id = ? AND l.right_core IN ('obj', 'stop') AND l.link_type_id LIKE '%:has_stop:%' "
                + "ORDER BY ordinal, s.local_id",
                folio.Code + ":", tourCore, entry.Id);

            var sounds = new Dictionary<TourStop, string>();
            var stops = stopRows.Select(r =>
            {
                var fields = Fields(r.GetString("dto_data"), r.GetString("extras"));
                var id = r.GetString("local_id");
                var stop = new TourStop
                {
                    Id = id,
                    ObjectId = r.GetString("right_core") == "obj" ? id : Text(fields, "objectId") ?? ObjectFromStopId(id),
                    Title = Text(fields, "title") ?? r.GetString("label"),
                    Subtitle = Text(fields, "subtitle"),
                    Latitude = (double?)fields["latitude"],
                    Longitude = (double?)fields["longitude"],
                };
                // AIC: the stop's audio-guide recording is a sound row named on the link.
                if (Text(Parse(r.GetString("link_extras")), "soundId") is { } sound) sounds[stop] = sound;
                return stop;
            }).ToList();

            var objects = (await folio.ItemsAsync(stops.Where(s => s.ObjectId != null).Select(s => s.ObjectId).Distinct().ToList()))
                .ToDictionary(i => i.LocalId);
            foreach (var stop in stops)
                if (stop.ObjectId != null && objects.TryGetValue(stop.ObjectId, out var item))
                    await FillAsync(folio, stop, item);
            foreach (var (stop, sound) in sounds)
                await AddSoundAsync(folio, stop, sound);

            return new Tour
            {
                Id = entry.Id,
                Title = entry.Title,
                // AIC keeps the long introduction in `intro`; its description is a one-line dek.
                Description = Longest(Text(tourFields, "intro"), Text(tourFields, "description")),
                CoverUrl = Text(tourFields, "thumbnailUrl") ?? Text(tourFields, "imageUrl"),
                Stops = stops,
            };
        }

        static async Task AddSoundAsync(FolioReader folio, TourStop stop, string soundId)
        {
            var rows = await folio.Database.QueryAsync(
                "SELECT label, dto_data, extras FROM item WHERE core_id = ? AND local_id = ?", folio.Code + ":sound", soundId);
            if (rows.Count == 0) return;
            var fields = Fields(rows[0].GetString("dto_data"), rows[0].GetString("extras"));
            if (Text(fields, "url") is not { } url) return;
            stop.Recordings.Add(new Recording { Kind = "audio", Url = url, Caption = "Audio guide", Credit = Text(fields, "rights") });
        }

        static string Longest(string a, string b) => (a?.Length ?? 0) >= (b?.Length ?? 0) ? a : b;

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

        // Older builds have no objectId on stops; the stop id names the object's source id:
        // "curatescape:baltimore:30:block-129" -> "curatescape_baltimore_129".
        static readonly System.Text.RegularExpressions.Regex StopId = new(@"^([a-z0-9]+):([a-z0-9_-]+):[^:]+:block-(\d+)$");

        static string ObjectFromStopId(string stopId) =>
            stopId != null && StopId.Match(stopId) is { Success: true } m ? $"{m.Groups[1].Value}_{m.Groups[2].Value}_{m.Groups[3].Value}" : null;

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
