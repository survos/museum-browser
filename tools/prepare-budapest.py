#!/usr/bin/env python3
"""Install a portable Budapest Folio and its referenced media into MuseumBrowser."""
import hashlib
import pathlib
import shutil
import sqlite3
import sys

root = pathlib.Path(__file__).resolve().parent.parent
fixture = pathlib.Path(sys.argv[1]).expanduser().resolve() if len(sys.argv) > 1 else pathlib.Path.home() / 'sites/harvest/var/panorama/budapest-sequence'
destination = root / 'Assets/StreamingAssets/Mapillary'
(destination / 'media').mkdir(parents=True, exist_ok=True)
with sqlite3.connect(f'file:{fixture / "budapest.folio"}?mode=ro', uri=True) as db:
    rows = db.execute("SELECT local_id, json_extract(dto_data, '$.displayImageUrl') FROM item").fetchall()
    assert rows, 'Empty Folio'
    assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
for image_id, relative in rows:
    image = (fixture / relative).resolve()
    assert image.is_relative_to(fixture / 'media') and image.is_file(), f'Invalid image {image_id}'
    target = destination / relative
    if not target.exists() or hashlib.file_digest(target.open('rb'), 'sha256').digest() != hashlib.file_digest(image.open('rb'), 'sha256').digest():
        shutil.copy2(image, target)
# Publish the Folio only after every image is installed.
shutil.copy2(fixture / 'budapest.folio', destination / 'budapest.folio.tmp')
(destination / 'budapest.folio.tmp').replace(destination / 'budapest.folio')
assert hashlib.sha256((fixture / 'budapest.folio').read_bytes()).digest() == hashlib.sha256((destination / 'budapest.folio').read_bytes()).digest()
print(f'{len(rows)} panoramas installed at {destination}')
