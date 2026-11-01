# Portable folio panorama tour contract

This documents the current shared profile used by Harvest, panorama-bundle and
MuseumBrowser. It is an application convention over a normal SQLite folio, not
a change to the SQLite wrapper or a formally versioned interchange standard.

## Storage and identity

Each panorama is an `item` row. `local_id` and `dto_data.id` identify the
same stop. Keep IDs and connection targets as strings, including numeric provider
IDs: JavaScript numbers cannot safely represent all Mapillary IDs.

`dto_data` contains title, creators, citationUrl, date, latitude, longitude and
displayImageUrl. Coordinates are WGS84 decimal degrees. The portable media path
is `media/<id>.jpg`, relative to the folio directory. Ship that folder with the
folio; image bytes are not stored as SQLite BLOBs. Resolve paths inside the media
root and reject traversal. Attribution and source links travel with every stop.

## item.extras

| Field | Meaning |
| --- | --- |
| mediaKind | `equirectangular`; current images are full 360-degree, 2:1 JPEGs |
| compassHeading | Finite degrees clockwise from geographic north at the image center; normalize modulo 360 when rendering |
| headingSource | Provider provenance, currently `computed_compass_angle` or `compass_angle` |
| sequence | Provider sequence identifier, as a string |
| sequenceIndex | Zero-based index in the provider's ordered sequence |
| tourStart | Boolean; exactly one stop is the initial view |
| connections | Array of destination item ID strings |
| width, height | Source image dimensions in pixels |

Example extras for a three-stop sequence's middle stop (illustrative values):

```json
{
  "mediaKind": "equirectangular",
  "compassHeading": 5.4,
  "headingSource": "computed_compass_angle",
  "sequence": "example-sequence",
  "sequenceIndex": 1,
  "tourStart": true,
  "connections": ["example-stop-0", "example-stop-2"],
  "width": 5760,
  "height": 2880
}
```

The Budapest exporter preserves provider order and connects only adjacent stops:
previous, then next. Endpoints have one connection; a single-stop tour has none.
Both directions are written explicitly. A sequence of N > 1 stops therefore has
2(N-1) directed links. Nearby map points are a separate discovery feature, not
permission to invent graph edges.

The current Unity reader requires connections to adjacent indices in the same
sequence. Curated branches, cross-sequence links and multi-tour archives need a
deliberate reader/profile extension before export; they are not implied by this
array format.

## Rendering adapters

Prefer Mapillary's computed heading and coordinates, falling back to original
values. Pitch and roll are not applied. Headings are orientation metadata, not
bearings to the next stop.

Compute an arrow's geographic bearing from the current stop to its destination.
Use the same north-zero, clockwise frame for the panorama and arrows. Preserve
the user's world viewing direction during transitions rather than resetting it
to each image's center.

Harvest maps DTO coordinates to scene `coordinates: [longitude, latitude]`,
resolves displayImageUrl to its image-serving route, and passes compassHeading
and connections to `survos:streetview`. Explicit compassHeading overrides the
bundle's legacy Kronofoto heading convention; do not interchange these fields.
Unity reads the same DTO/extras directly and resolves media beside the folio.

## Export validation and reuse

Require unique IDs, finite headings, valid coordinates, one initial stop,
contiguous sequence indices, existing connection targets, reciprocal adjacent
links and all referenced media. Check SQLite integrity before distributing.
No navigation sidecar, Mapillary token or live provider API is needed at playback.
Do not distribute credentials or temporary signed download URLs.

The Budapest fixture contains 361 stops and 720 directed links. Harvest browser
arrow/map synchronization and a Unity 6000.6.4f1 macOS Mono player transition were
verified. Windows and IL2CPP tour validation remain pending. Downloaded archives,
media, raw caches and build output are intentionally excluded from Git; use the
export/install commands in each application's Budapest notes.
