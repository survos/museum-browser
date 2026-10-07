# Museum Browser

Unity (6000.6.4f1, URP) browser for folio collections: a photo gallery on a wall,
with museum wall labels (tombstones) and audio guides. Plan: `~/unity/folio-demo/PLAN.md`.

## Layout

- `Assets/MuseumBrowser/Scripts/Core` — `MuseumBrowser.Core` assembly: data packs, repository, image loading, domain model (shared; may become a package).
- `Assets/MuseumBrowser/Scripts/App` — `MuseumBrowser.App` assembly: scenes, UI, input.
- `Assets/MuseumBrowser/Scenes/Gallery.unity` — the only build scene.

## Purchased assets (not in git)

`Assets/Creepy_Cat/` is git-ignored. To restore it:

1. Package Manager → My Assets → **3D Museum Showroom Kit** (Creepy Cat) → Download.
2. The download contains nested packages. Import only
   `NEW - 3D Museum Concrete Design - URP.unitypackage` (folder `ShowRoom_Vol 34_URP`).
   A copy is kept at `/Volumes/X10/unity/packages/Museum-Concrete-Design-URP.unitypackage`.

## Builds

macOS (Apple Silicon) and Web, via Build Profiles; output in `Builds/` (git-ignored).

## Editor automation

MCP for Unity (`com.coplaydev.unity-mcp`, pinned v10.3.0) — Window → MCP for Unity → Start Server (HTTP, 127.0.0.1:8080).
