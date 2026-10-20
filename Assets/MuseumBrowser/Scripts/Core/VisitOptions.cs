using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Survos.Folio;

namespace MuseumBrowser.Core
{
    /// What the visitor asked for before the galleries were hung: how many works, from which
    /// years, about what. Null/empty means no constraint.
    public sealed class VisitOptions
    {
        public int Count;
        public int? YearFrom, YearTo;
        public string Keyword;

        public bool HasYears => YearFrom.HasValue || YearTo.HasValue;
        public bool HasKeyword => !string.IsNullOrWhiteSpace(Keyword);

        public override string ToString() =>
            $"{Count} works" + (HasYears ? $", {YearFrom}–{YearTo}" : "") + (HasKeyword ? $", \"{Keyword.Trim()}\"" : "");
    }

    /// What the opening screen needs to offer choices about a folio.
    public sealed class VisitChoice
    {
        public string Title;
        /// Works with images.
        public int Total;
        /// Span of the dated works; HasYears is false when too few are dated for a range to mean anything.
        public int MinYear, MaxYear;
        public bool HasYears;
        public int DefaultCount, MaxCount;
    }

    /// Filtering a folio's index by year range and keyword. There is no full-text index in a
    /// folio, so a keyword is a case-insensitive substring match over the row's label and its
    /// text values (every word must match), which is plenty at this size.
    public static class FolioSearch
    {
        // Years outside this are data errors, not exhibits.
        const int SaneMin = 1000, SaneMax = 2100;

        public static VisitChoice Describe(string title, IReadOnlyList<FolioIndexEntry> index, int defaultCount)
        {
            var years = index.Where(e => e.Year is >= SaneMin and <= SaneMax).Select(e => e.Year.Value).ToList();
            bool hasYears = years.Count * 3 >= index.Count * 2 && years.Count > 0;
            return new VisitChoice
            {
                Title = title,
                Total = index.Count,
                HasYears = hasYears,
                MinYear = hasYears ? years.Min() : 0,
                MaxYear = hasYears ? years.Max() : 0,
                DefaultCount = System.Math.Min(defaultCount, index.Count),
                MaxCount = System.Math.Max(defaultCount, System.Math.Min(400, index.Count)),
            };
        }

        /// The index entries that satisfy the year range and keyword. Undated works are left out
        /// whenever a year range is set.
        public static async Task<List<FolioIndexEntry>> FilterAsync(
            FolioReader folio, IReadOnlyList<FolioIndexEntry> index, VisitOptions options)
        {
            IEnumerable<FolioIndexEntry> result = index;
            if (options.HasYears)
            {
                int from = options.YearFrom ?? SaneMin, to = options.YearTo ?? SaneMax;
                result = result.Where(e => e.Year is { } y && y >= from && y <= to);
            }
            if (options.HasKeyword)
            {
                var hits = await MatchAsync(folio, options.Keyword);
                result = result.Where(e => hits.Contains(e.LocalId));
            }
            return result.ToList();
        }

        static async Task<HashSet<string>> MatchAsync(FolioReader folio, string keyword)
        {
            var words = keyword.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(w => "%" + w.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%").ToList();
            var core = folio.Code + ":obj";

            // Match on values only (json_tree), so a word like "title" does not hit every row's keys.
            // Fall back to the raw JSON text where the SQLite build has no json_tree.
            string Clause(bool values) => string.Join(" AND ", words.Select(_ => values
                ? "(i.label LIKE ? ESCAPE '\\' OR EXISTS (SELECT 1 FROM json_tree(i.dto_data) t WHERE t.type = 'text' AND t.atom LIKE ? ESCAPE '\\') "
                  + "OR EXISTS (SELECT 1 FROM json_tree(i.extras) t WHERE t.type = 'text' AND t.atom LIKE ? ESCAPE '\\'))"
                : "(i.label LIKE ? ESCAPE '\\' OR i.dto_data LIKE ? ESCAPE '\\' OR i.extras LIKE ? ESCAPE '\\')"));

            foreach (bool values in new[] { true, false })
            {
                var bound = new List<object> { core };
                foreach (var w in words) { bound.Add(w); bound.Add(w); bound.Add(w); }
                try
                {
                    var rows = await folio.Database.QueryAsync(
                        $"SELECT i.local_id FROM item i WHERE i.core_id = ? AND {Clause(values)}", bound.ToArray());
                    return rows.Select(r => r.GetString("local_id")).ToHashSet();
                }
                catch (System.Exception e) when (values)
                {
                    UnityEngine.Debug.LogWarning($"Keyword search falls back to plain text matching: {e.Message}");
                }
            }
            return new HashSet<string>();
        }
    }
}
