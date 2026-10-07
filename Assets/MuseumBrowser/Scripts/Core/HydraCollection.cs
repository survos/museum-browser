using System.Collections.Generic;
using Newtonsoft.Json;

namespace MuseumBrowser.Core
{
    /// A JSON-LD/Hydra collection page from API Platform. Newer API Platform
    /// versions drop the "hydra:" prefix, so both spellings are accepted.
    public sealed class HydraCollection<T>
    {
        [JsonProperty("hydra:member")] List<T> hydraMember;
        [JsonProperty("member")] List<T> member;
        [JsonProperty("hydra:totalItems")] int? hydraTotal;
        [JsonProperty("totalItems")] int? total;

        [JsonProperty("folio")] public FolioInfo Folio;

        [JsonIgnore] public List<T> Members => hydraMember ?? member ?? new List<T>();
        [JsonIgnore] public int TotalItems => hydraTotal ?? total ?? Members.Count;
    }

    public sealed class FolioInfo
    {
        public string Code;
        public string Title;
        public string License;
        public string Credit;
    }
}
