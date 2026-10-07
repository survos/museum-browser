using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Survos.Folio;

namespace MuseumBrowser.Core
{
    /// WallCards from a folio opened locally (com.survos.folio): the same mapping as
    /// folio-bundle's WallCardMapper, minus the signed image URLs, which are resolved per
    /// room from the site (MuseadoImages) when they are about to be seen.
    public static class FolioCards
    {
        /// Which works to hang: spread over the decades (round-robin, a stable shuffle within
        /// each), so a folio of 26,000 photographs gives a walk through its whole span rather
        /// than its first hundred.
        public static List<string> Choose(IReadOnlyList<FolioIndexEntry> index, int max)
        {
            var dated = index.Where(e => e.Year.HasValue).ToList();
            if (dated.Count * 3 < index.Count * 2)
                return index.OrderBy(e => Shuffle(e.LocalId)).Take(max).Select(e => e.LocalId).ToList();
            var decades = dated.GroupBy(e => e.Year.Value / 10)
                .OrderBy(g => g.Key)
                .Select(g => new Queue<FolioIndexEntry>(g.OrderBy(e => Shuffle(e.LocalId))))
                .ToList();
            var chosen = new List<FolioIndexEntry>();
            while (chosen.Count < max && decades.Any(q => q.Count > 0))
                foreach (var q in decades)
                    if (q.Count > 0 && chosen.Count < max) chosen.Add(q.Dequeue());
            return chosen.OrderBy(e => e.Year).ThenBy(e => e.LocalId).Select(e => e.LocalId).ToList();
        }

        // FNV-1a: deterministic across runs and platforms (string.GetHashCode is not).
        static uint Shuffle(string id)
        {
            uint h = 2166136261;
            foreach (char c in id) { h ^= c; h *= 16777619; }
            return h;
        }

        public static WallCard Map(FolioItem item, string folioCode)
        {
            var fields = Parse(item.ExtrasJson);
            // DTO fields win over extras (PHP: $dto + $extras).
            foreach (var p in Parse(item.DtoJson).Properties()) fields[p.Name] = p.Value;
            string Text(string key) => fields[key] is JValue { Value: not null } v ? v.ToString() : null;

            var creators = Strings(fields["creators"]);
            bool npg = folioCode == "smith/npg";
            string creator = creators.Count == 0 ? null : string.Join("; ", npg ? creators.Take(1) : creators);
            string subject = npg && creators.Count > 1 ? string.Join("; ", creators.Skip(1)) : null;
            string title = Text("title") ?? Text("sourceCaption") ?? item.Label;
            string place = string.Join(", ", new[] { Text("city"), Text("state"), Text("country") }.Where(s => !string.IsNullOrEmpty(s)));
            if (place == "") place = Text("placeOfOrigin");
            string dimensions = Text("dimensionsRaw");

            return new WallCard
            {
                Id = item.LocalId,
                Title = title,
                Year = item.Year,
                Label = new WallLabel
                {
                    Creator = creator, Title = title, Subject = subject,
                    Date = Text("date") ?? item.Year?.ToString(),
                    Place = string.IsNullOrEmpty(place) ? null : place,
                    Medium = Text("med"), Dimensions = dimensions,
                    Credit = Text("credit") ?? Text("creditline"),
                    Accession = Text("accession"), Tombstone = Text("tombstone"),
                },
                Tags = Strings(fields["tags"]),
                Subjects = Strings(fields["subjects"]),
                Size = Size(dimensions, item.Page?.Width, item.Page?.Height),
                Image = item.Page == null ? null : new CardImage { Thumbhash = item.Page.ThumbHash, Color = item.Page.Color },
                SourceUrl = Text("citationUrl") ?? Text("sourceUrl") ?? Text("url"),
                License = Text("license") ?? Text("rightsUri") ?? Text("rights"),
            };
        }

        static JObject Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new JObject();
            try { return JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return new JObject(); }
        }

        static List<string> Strings(JToken value) => value switch
        {
            JArray a => a.OfType<JValue>().Where(v => v.Value != null).Select(v => v.ToString().Trim()).Where(s => s != "").Distinct().ToList(),
            JValue { Value: not null } v => new List<string> { v.ToString().Trim() }.Where(s => s != "").ToList(),
            _ => new List<string>(),
        };

        // "58.4 x 48.3 cm", "Sight: 23 1/2 x 19 in." -- museum order is height x width.
        // A simple stand-in for the server's DimensionParser: the first H x W with a unit.
        static readonly Regex HxW = new(@"(\d+(?:\.\d+)?)\s*[x×]\s*(\d+(?:\.\d+)?)(?:\s*[x×]\s*\d+(?:\.\d+)?)?\s*(mm|cm|in)", RegexOptions.IgnoreCase);

        static CardSize Size(string dimensions, int? wPx, int? hPx)
        {
            if (dimensions != null && HxW.Match(dimensions) is { Success: true } m)
            {
                float k = m.Groups[3].Value.ToLowerInvariant() switch { "mm" => 1f, "cm" => 10f, _ => 25.4f };
                int h = (int)(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * k);
                int w = (int)(float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * k);
                if (w > 0 && h > 0) return new CardSize { WidthMm = w, HeightMm = h, WPx = wPx, HPx = hPx, Kind = "image" };
            }
            return wPx > 0 && hPx > 0 ? new CardSize { WPx = wPx, HPx = hPx, Kind = "image" } : null;
        }
    }
}
