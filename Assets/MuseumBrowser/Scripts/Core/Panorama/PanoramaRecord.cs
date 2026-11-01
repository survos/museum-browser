using Survos.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MuseumBrowser.Core
{
    // Portable Folio navigation contract: extras stores degrees clockwise from north
    // and ordered, explicit neighboring item IDs. No provider API is needed by Unity.
    public sealed class PanoramaRecord
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Creator { get; private set; }
        public string SourceUrl { get; private set; }
        public string ImagePath { get; private set; }
        public double CompassHeading { get; private set; }
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public string Sequence { get; private set; }
        public long SequenceIndex { get; private set; }
        public bool TourStart { get; private set; }
        public List<string> Connections { get; } = new List<string>();

        public static async Task<List<PanoramaRecord>> LoadTourAsync(string folioPath, CancellationToken cancellation = default)
        {
            await using var db = await SqliteDatabase.OpenAsync(folioPath, SqliteOpenMode.ReadOnly, cancellation);
            const string sql = @"SELECT local_id,
                json_extract(dto_data, '$.title') AS title,
                json_extract(dto_data, '$.creators[0]') AS creator,
                json_extract(dto_data, '$.citationUrl') AS source,
                json_extract(dto_data, '$.displayImageUrl') AS image,
                json_extract(dto_data, '$.latitude') AS latitude,
                json_extract(dto_data, '$.longitude') AS longitude,
                json_extract(extras, '$.compassHeading') AS heading,
                json_extract(extras, '$.sequence') AS sequence,
                json_extract(extras, '$.sequenceIndex') AS sequence_index,
                json_extract(extras, '$.tourStart') AS tour_start
                FROM item WHERE json_extract(extras, '$.mediaKind') = 'equirectangular'
                ORDER BY sequence_index";
            var records = new List<PanoramaRecord>();
            var byId = new Dictionary<string, PanoramaRecord>();
            var directory = Path.GetDirectoryName(Path.GetFullPath(folioPath));
            var mediaRoot = Path.Combine(directory, "media") + Path.DirectorySeparatorChar;
            await foreach (var row in db.QueryAsync(sql, cancellation))
            {
                if (row.IsNull("heading") || row.IsNull("sequence") || row.IsNull("sequence_index"))
                    throw new InvalidDataException("Folio lacks the portable tour navigation fields.");
                var record = new PanoramaRecord {
                    Id = row.GetString("local_id"), Title = row.GetString("title"), Creator = row.GetString("creator"),
                    SourceUrl = row.GetString("source"), CompassHeading = row.GetDouble("heading"),
                    Latitude = row.GetDouble("latitude"), Longitude = row.GetDouble("longitude"),
                    Sequence = row.GetString("sequence"), SequenceIndex = row.GetInt64("sequence_index"),
                    TourStart = !row.IsNull("tour_start") && row.GetInt64("tour_start") != 0
                };
                var relative = row.GetString("image");
                if (string.IsNullOrWhiteSpace(relative)) throw new InvalidDataException("Missing panorama image path.");
                var image = Path.GetFullPath(Path.Combine(directory, relative));
                if (Path.IsPathRooted(relative) || !image.StartsWith(mediaRoot, StringComparison.Ordinal) || !File.Exists(image))
                    throw new InvalidDataException("Panorama must be a local file inside the Folio's media directory.");
                if (string.IsNullOrWhiteSpace(record.Creator) || !Uri.TryCreate(record.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https")
                    throw new InvalidDataException("Panorama attribution is missing.");
                record.ImagePath = image;
                records.Add(record); byId.Add(record.Id, record);
            }
            // The first query has released its lease before reading the adjacency list.
            await foreach (var row in db.QueryAsync(@"SELECT item.local_id, link.value AS target
                FROM item, json_each(item.extras, '$.connections') AS link
                WHERE json_extract(item.extras, '$.mediaKind') = 'equirectangular'", cancellation))
            {
                var from = byId[row.GetString("local_id")]; var id = row.GetString("target");
                if (!byId.TryGetValue(id, out var target) || from.Sequence != target.Sequence || Math.Abs(from.SequenceIndex-target.SequenceIndex) != 1)
                    throw new InvalidDataException("Invalid sequence connection.");
                from.Connections.Add(id);
            }
            if (records.Count == 0) throw new InvalidDataException("Empty panorama folio.");
            return records;
        }

        public double BearingTo(PanoramaRecord target)
        {
            const double r = Math.PI / 180;
            var a = Latitude*r; var b = target.Latitude*r; var d = (target.Longitude-Longitude)*r;
            return Math.Atan2(Math.Sin(d)*Math.Cos(b), Math.Cos(a)*Math.Sin(b)-Math.Sin(a)*Math.Cos(b)*Math.Cos(d))/r;
        }
    }
}
