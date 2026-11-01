# Current: complete portable Budapest sequence

Build the tour with Harvest's `php bin/prepare-budapest-sequence.php`, then run
`python3 tools/prepare-budapest.py` in MuseumBrowser. This supersedes the ten-image
smoke setup below. The complete sequence has 361 panoramas; all media is local.

The exact same Folio runs in Harvest and Unity. `item.extras` carries compassHeading
(degrees clockwise from north at the image center), sequence, sequenceIndex,
tourStart, and connections (explicit neighboring item ID strings). The DTO carries
coordinates, attribution, and a relative media path. No navigation sidecar is read.

Play starts in Budapest. Drag to look around, scroll to zoom, and use the projected
navigation buttons or Previous/Next to follow the sequence. Heading is applied to
the skybox; camera direction is retained across transitions. Only one panorama
texture is retained at a time. This remains a desktop Mono validation, not XR,
Android, WebGL or IL2CPP certification.

For a navigation smoke capture, append `-panoramaStep` to the player command below.
The player follows the starting stop's next sequence link before capturing and
logs `SURVOS_PANORAMA_NAVIGATION_PASS` and the tour's record/link counts.

---

## Historical ten-image proof (superseded)

# Budapest panorama

All panorama application code and fixtures live in MuseumBrowser. The sibling
SQLite package supplies only database access; no separate Unity project is needed.

## View

Press **Play**: Budapest is now the default Play Mode scene, even while editing
the gallery. Stop an existing Play session first and let scripts reload.
**Museum Browser → Play Starts In → Current Scene** restores normal Editor behavior;
choose **Budapest Panorama** there to restore this default.
Budapest is also first in the shared build scene list, with Hall retained afterward.
Build profiles that override the shared scene list remain independent.
To edit the panorama scene, use **Museum Browser → Open Budapest Panorama** and
save existing scene edits when prompted. Drag to look around; scroll to zoom. The overlay shows
the creator and links to the original Mapillary image. The scene is
`Assets/MuseumBrowser/Scenes/BudapestPanorama.unity`. This is rotational 360 viewing, not reconstructed geometry or XR.
North orientation has not been established from the capture.

## Data

Nearby-point navigation is not implemented yet. The ten captured records span
six Mapillary sequences and multiple dates, so they are not a continuous route.
Only one image is downloaded, and the capture has no camera-heading fields.
A first navigation increment should download the remaining images, show their
coordinates on a small map with distances/dates, and allow point-to-point jumps.
For street-aligned arrows and consistent facing direction, acquire camera
headings and ordered sequence neighbors; proximity alone cannot establish a
walkable connection. Prefer one sequence for a coherent street-view pilot.

The real Folio contains ten panorama records from the existing Budapest capture.
One full-resolution 5760×2880 JPEG is bundled locally, ID `1867374714260803`,
creator `grozsa11`. Source: https://www.mapillary.com/app/?pKey=1867374714260803.
The other nine records retain metadata but have no local image yet.

`PanoramaRecord` queries `item.dto_data` and `item.extras` using native SQLite,
then releases the database before loading the image. `displayImageUrl` is relative
to the Folio directory for this offline fixture. The viewer requires a local file
inside `media/`, attribution, and a decodable 2:1 image. This desktop-only path
does not implement WebGL or Android archive loading.

Rebuild through Harvest's shared FolioBuilder library, without changing its
registry or advancing its dataset workflow:

```sh
php tools/build-mapillary-fixture.php /Users/tac/sites/harvest \
  /Volumes/X10/vault-v3/capture/mapillary/_capture/hu/20261031T131827Z-b79867dd 10
python3 tools/prepare-budapest.py
```

The helper validates the capture hash/count, refreshes only the selected image,
and reuses its JPEG on subsequent runs. The existing Harvest credential is loaded
with Dotenv; credentials and signed CDN URLs are not stored. Generated output is
ignored under `Data/mapillary-smoke` and `Assets/StreamingAssets/Mapillary`.
The former separate smoke project is retained only under ignored `Data/retired-*`
for recovery, and is no longer the integration target.

## Render verification

Close the Editor first. From this project root:

```sh
/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD" \
  -executeMethod MuseumBrowser.EditorTools.PanoramaSetup.Build \
  -logFile "$PWD/Logs/panorama-build.log"
'Builds/Panorama/MuseumBrowser.app/Contents/MacOS/Museum Browser' \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
  -panoramaCapture "$PWD/Captures/Budapest/panorama.png" \
  -logFile "$PWD/Logs/panorama-player.log"
```

The development player uses Mono and the project's URP settings. The build helper
restores the previous scripting backend and does not change the gallery build
scene list. Require player exit 0, `SURVOS_PANORAMA_RENDER_PASS`, and visual
inspection of the captured image. This does not establish IL2CPP or XR support.

## Provenance and status

- Capture SHA-256: `7ac542956d81e24bde803e219eb7fe2885a60ec12c0c5b98a4e39d56a6e9df87`.
- JPEG SHA-256: `70c35c655c806c82279186719cd477c5eb1cd5712cb349eb709b67b2ff52f825`.
- Ten source IDs, coordinates, and creators were reconciled with the Folio;
  SQLite integrity check passed. Native macOS query verification passed.
- Unity 6000.6.4f1 build succeeded on 2026-11-01. The macOS ARM64 Mono
  development player loaded image `1867374714260803` at 5760×2880, logged
  `SURVOS_PANORAMA_RENDER_PASS`, and exited 0. Its 1280×720 URP screenshot
  was visually inspected: Budapest streetscape and attribution are visible.
  Evidence: `Captures/Budapest/panorama.png`, `Logs/panorama-build.log`, and
  `Logs/panorama-player.log` (local ignored outputs). IL2CPP and XR remain untested.

Production Mapillary acquisition, media archival and workflow publication remain
Harvest work. This is a bounded Budapest sample, not complete Hungary coverage.

Future database investigation: the owner identified
[DuckDB.NET](https://github.com/Giorgi/DuckDB.NET) as a candidate; no compatibility
claims about Unity, IL2CPP or a particular DuckDB release have been verified.

## Full-sequence validation (2026-11-01)

- Folio integrity check passed: 361 panoramas in provider sequence order, 720 directed adjacent connections, one start record, no dangling links, and all referenced JPEGs present.
- Harvest browser displayed all 361 locations. Clicking an in-panorama arrow moved from `1867374714260803` to `1130839495980986` and updated the selected map marker.
- Unity 6000.6.4f1 macOS Mono standalone build and player exited successfully. The player reported `SURVOS_PANORAMA_TOUR 361 records 720 links`, navigated from `1867374714260803` to `1848874836558460`, and reported `SURVOS_PANORAMA_RENDER_PASS` for the destination.
- Unity screenshot: `Captures/Budapest/sequence-navigation.png` in MuseumBrowser. This validates the Mono player; this tour has not been checked under IL2CPP or on Windows.
- Both applications use folio SHA-256 `6cdfdd3fe45309fef49cce9f6142a046f912290820e4593717cdc92a4212b4e8`. JPEGs remain companion media files (approximately 0.68 GiB), not SQLite blobs.

See the [portable folio panorama tour contract](folio-panorama-tour.md) for field semantics, coordinate conventions, connection rules and reuse limits.
