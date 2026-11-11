# ADR 0006: Avatar storage and validation

**Status:** accepted

## Context
User uploads are the classic way into a web app: oversized files, HTML or SVG disguised as images (stored XSS),
path traversal through the file name, and files lost on every redeploy when they live inside the container.

## Decision
- **Limits**: `[RequestSizeLimit]` / `[RequestFormLimits]` (10 MB) make Kestrel stop reading huge bodies early, and
  `AvatarStorage` checks `file.Length` against 2 MB (a 3–10 MB photo gets a friendly message, not an error page).
- **Type**: only JPEG, PNG and WebP. Three checks must agree: the extension, the declared `Content-Type`, and the
  **magic bytes** (`89 50 4E 47…`, `FF D8 FF`, `RIFF….WEBP`). The first two come from the client; the bytes decide.
  SVG is never accepted, because it is XML and can contain script.
- **Name**: `{Guid:N}.{detected extension}`. The original name is never used, not even in logs.
- **Location**: `App:DataPath/avatars`, outside `wwwroot` and outside the image, on the `appdata` Docker volume
  (`/data`, which also holds the Data Protection keys).
- **Serving**: `GET /avatars/{fileName}` accepts only `^[0-9a-f]{32}\.(jpg|png|webp)\z`, so `../` and other names are
  a 404. It sets the right `Content-Type`, `X-Content-Type-Options: nosniff` and a long private cache (a new upload always
  gets a new name, so a file never changes).
- **Lifecycle**: replacing or removing an avatar deletes the old file after the database is updated; guest cleanup
  deletes guests' files after its transaction commits (a file delete can't be rolled back).
- **No image library**: no ImageSharp or similar, to keep dependencies minimal.

## Consequences
- \+ Each check is a few lines and unit-tested (fake PNG, oversized, SVG, content-type mismatch, guid name, bad names).
- \- The stored file is exactly what was uploaded, including EXIF metadata (e.g. GPS) and any bytes after the image.
  **Next step, not built:** re-encode the image on the server, which strips metadata and hidden payloads.
