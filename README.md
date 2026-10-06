# Scepter Media CDN API

Small ASP.NET Core media API over **Cloudflare Images** and **Cloudflare Stream**.

Images use server-side CRUD with caller-selected logical paths. Videos use Cloudflare Stream direct creator uploads: the backend creates a one-time upload URL, then the browser/app uploads the video bytes directly to Cloudflare without receiving the Cloudflare API token.

Default local development keeps the existing in-process Local image provider. Video endpoints require Cloudflare credentials.

## What this service does

- Accepts image uploads with caller-selected nested paths such as `products/nike/air-max/front`
- Creates, reads, replaces, and deletes images through a stable HTTP contract
- Creates one-time Cloudflare Stream video upload URLs and exposes video processing/playback metadata
- Returns image CDN URLs and video HLS/DASH playback URLs
- Keeps Cloudflare API tokens server-side only

## Architecture

```text
Client
  └─ ImagesController / HealthController
       └─ validation (path + file)
            └─ IImageProvider
                 ├─ LocalImageProvider      (default for local/dev)
                 └─ CloudflareImageProvider (typed HttpClient → Cloudflare Images API)
```

- Controllers stay free of provider-specific Cloudflare details
- Strongly typed options via `IOptions<T>`
- Outbound Cloudflare calls use `IHttpClientFactory`
- Centralized exception middleware maps failures to RFC 7807 ProblemDetails

## Why there is no database

Cloudflare Images already stores the image bytes and metadata. This API is a thin, authenticated CRUD façade. Adding a local DB would duplicate state and create sync problems.

## Local prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional: Docker Desktop (for container builds)
- Optional later: Cloudflare account with Images enabled

## Local setup

```powershell
git clone <this-repo-url> image-cdn-api
cd image-cdn-api
copy .env.example .env
dotnet restore
dotnet build
```

Do not commit `.env`. Keep secrets out of source control.

## Running with Local provider

```powershell
$env:IMAGE_PROVIDER = "Local"
$env:LOCAL_IMAGE_ROOT = ".local-data/images"
$env:ASPNETCORE_ENVIRONMENT = "Development"

dotnet run --project src/ImageCdn.Api --urls http://localhost:5000
```

Open Swagger UI: [http://localhost:5000/swagger](http://localhost:5000/swagger)

Health:

```powershell
curl http://localhost:5000/health
```

Expected:

```json
{ "status": "ok", "provider": "Local" }
```

## Running tests

```powershell
dotnet test
```

All tests use the Local provider and/or stubbed Cloudflare HTTP handlers. No real Cloudflare account is required.

## Cloudflare setup

1. Create / select a Cloudflare account
2. Enable **Cloudflare Images** and **Cloudflare Stream**
3. Create an **account-scoped API token** with **Images Write** and **Stream Write** permissions
4. Note:
   - Account ID → `CLOUDFLARE_ACCOUNT_ID`
   - Images account hash → `CLOUDFLARE_ACCOUNT_HASH`
   - API token → `CLOUDFLARE_API_TOKEN`
5. Confirm the Images delivery variant name (default `public`) → `CLOUDFLARE_VARIANT`

The Images/Stream signing key is **not required** for normal public playback. It is only needed if you set `requireSignedUrls=true` and implement signed delivery tokens.

## Environment variables

See `.env.example`:

| Variable | Default | Purpose |
|---|---|---|
| `IMAGE_PROVIDER` | `Local` | `Local` or `Cloudflare` |
| `CLOUDFLARE_ACCOUNT_ID` | _(empty)_ | Cloudflare account ID |
| `CLOUDFLARE_ACCOUNT_HASH` | _(empty)_ | Delivery URL account hash |
| `CLOUDFLARE_API_TOKEN` | _(empty)_ | Bearer token (never expose to clients) |
| `CLOUDFLARE_VARIANT` | `public` | Delivery variant suffix |
| `MAX_IMAGE_SIZE_BYTES` | `10485760` | Max upload size (10 MB) |
| `LOCAL_IMAGE_ROOT` | `.local-data/images` | Local provider storage root |
| `SERVICE_API_KEY` | _(empty)_ | Optional inbound API key for `/api/*` |

The same `CLOUDFLARE_API_TOKEN` is used by the Images and Stream server-side clients.

When `IMAGE_PROVIDER=Cloudflare`, missing Cloudflare values cause a clear startup failure.

## Switching from Local to Cloudflare

```powershell
$env:IMAGE_PROVIDER = "Cloudflare"
$env:CLOUDFLARE_ACCOUNT_ID = "<account-id>"
$env:CLOUDFLARE_ACCOUNT_HASH = "<account-hash>"
$env:CLOUDFLARE_API_TOKEN = "<api-token>"
$env:CLOUDFLARE_VARIANT = "public"

dotnet run --project src/ImageCdn.Api --urls http://localhost:5000
```

No code changes required. Perform a live POST/GET smoke test after credentials arrive.

## Endpoint documentation

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/images` | Upload (`multipart/form-data`: `file`, `path`) |
| `GET` | `/api/images/{**path}` | Fetch metadata for nested path |
| `PUT` | `/api/images/{**path}` | Replace bytes at existing path (`file`) |
| `DELETE` | `/api/images/{**path}` | Delete image |
| `POST` | `/api/videos/direct-upload` | Create a one-time Stream upload URL |
| `GET` | `/api/videos/{uid}` | Video processing state + playback URLs |
| `DELETE` | `/api/videos/{uid}` | Delete a Stream video |
| `GET` | `/health` | Liveness + active image provider name |

Checked-in contract: [`openapi.yaml`](./openapi.yaml)

### Example CREATE response

```json
{
  "id": "products/nike/air-max/front",
  "url": "https://imagedelivery.net/ACCOUNT_HASH/products/nike/air-max/front/public",
  "filename": "shoe.jpg",
  "contentType": "image/jpeg",
  "uploaded": null
}
```

Local provider returns a URL like:

`http://localhost:5000/local-images/products/nike/air-max/front`

## Video upload flow

For videos up to **200 MB** on a reliable connection:

1. Call `POST /api/videos/direct-upload` with JSON such as:

```json
{
  "maxDurationSeconds": 600,
  "creator": "user-123",
  "fileName": "product-demo.mp4",
  "requireSignedUrls": false
}
```

2. The API returns:

```json
{
  "uid": "<video-uid>",
  "uploadUrl": "https://upload.videodelivery.net/..."
}
```

3. Upload the file directly to `uploadUrl` as multipart field `file`.
4. Poll `GET /api/videos/<video-uid>` until `readyToStream=true`.
5. Use the returned HLS/DASH/preview URL.

Cloudflare requires **tus resumable uploads** for files over 200 MB; that flow is not yet exposed by this API.

## curl examples

### Health

```bash
curl -s http://localhost:5000/health
```

### POST (create)

```bash
curl -s -X POST http://localhost:5000/api/images \
  -F "file=@shoe.jpg;type=image/jpeg" \
  -F "path=products/nike/air-max/front"
```

### GET

```bash
curl -s http://localhost:5000/api/images/products/nike/air-max/front
```

### PUT (replace)

```bash
curl -s -X PUT http://localhost:5000/api/images/products/nike/air-max/front \
  -F "file=@shoe-v2.png;type=image/png"
```

### DELETE

```bash
curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE http://localhost:5000/api/images/products/nike/air-max/front
```

### With API key

```bash
curl -s http://localhost:5000/api/images/products/nike/air-max/front \
  -H "X-Api-Key: your-key"
```

## PowerShell examples

```powershell
# Create
$form = @{
  file = Get-Item .\shoe.jpg
  path = "products/nike/air-max/front"
}
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/images -Form $form

# Get
Invoke-RestMethod http://localhost:5000/api/images/products/nike/air-max/front

# Replace
$replace = @{ file = Get-Item .\shoe-v2.png }
Invoke-RestMethod -Method Put -Uri http://localhost:5000/api/images/products/nike/air-max/front -Form $replace

# Delete
Invoke-WebRequest -Method Delete -Uri http://localhost:5000/api/images/products/nike/air-max/front
```

## Custom path naming rules

Valid examples:

- `products/nike/air-max/front`
- `products/coach/tabby-black/01`
- `categories/mens/banner`
- `users/123/avatar`

Rules:

- required; normalized by trimming leading/trailing `/`
- max 1,024 characters
- nested `/` allowed
- allowed characters: letters, numbers, `_`, `-`, `.`, `/`
- rejected: `..`, `\`, `%`, control characters, empty path

## File limits

- required, non-empty
- default max size: **10 MB** (`MAX_IMAGE_SIZE_BYTES`)
- oversized uploads → **HTTP 413**
- default allowlist:
  - `image/jpeg`
  - `image/png`
  - `image/webp`
  - `image/gif`
  - `image/svg+xml`

## Error responses

ProblemDetails-style JSON (`application/problem+json` where practical):

| Status | Meaning |
|---|---|
| 400 | Validation failure |
| 401 | Missing/invalid `X-Api-Key` when configured |
| 404 | Image not found |
| 409 | Duplicate custom path |
| 413 | File too large |
| 502 | Cloudflare/upstream failure |
| 503 | Provider/configuration unavailable |
| 500 | Unexpected internal error |

Secrets and Authorization headers are never returned in error bodies or logs.

## Security guidance

- Never log `CLOUDFLARE_API_TOKEN`, Authorization headers, or file contents
- Use an account-scoped Cloudflare token limited to **Images Write** and **Stream Write**
- `SERVICE_API_KEY`:
  - empty → `/api/*` unauthenticated (acceptable for local Development only)
  - set → require `X-Api-Key` on `/api/*`
  - `/health` remains open
- **Before public exposure**, enable `SERVICE_API_KEY` or replace with the hosting team's normal auth (API gateway, mTLS, etc.)
- This repository intentionally does not implement OAuth/JWT

## Docker usage

Build:

```powershell
docker build -t image-cdn-api .
```

Run (Local provider):

```powershell
docker run --rm -p 8080:8080 `
  -e IMAGE_PROVIDER=Local `
  -e LOCAL_IMAGE_ROOT=/data/images `
  -e ASPNETCORE_ENVIRONMENT=Development `
  -v ${PWD}/.local-data/images:/data/images `
  image-cdn-api
```

Run (Cloudflare provider — supply secrets at runtime, never bake into the image):

```powershell
docker run --rm -p 8080:8080 `
  -e IMAGE_PROVIDER=Cloudflare `
  -e CLOUDFLARE_ACCOUNT_ID=... `
  -e CLOUDFLARE_ACCOUNT_HASH=... `
  -e CLOUDFLARE_API_TOKEN=... `
  -e SERVICE_API_KEY=... `
  image-cdn-api
```

## Deployment handoff notes

- This repo owns the API contract and provider adapters only
- Hosting/deployment is intentionally out of scope
- Configure environment variables in the target platform
- Prefer platform secret stores for `CLOUDFLARE_API_TOKEN` and `SERVICE_API_KEY`
- Point health checks at `GET /health`
- After Cloudflare credentials are available, switch `IMAGE_PROVIDER=Cloudflare` and smoke-test CRUD

## Cloudflare costs

Approximate Cloudflare Images pricing (re-check official pricing before budgeting):

- Images storage requires **Images Paid**
- about **$5 per 100,000 stored images / month**
- about **$1 per 100,000 delivered hosted images / month**
- this API itself has **no database/storage hosting charge** from this repository
- hosting cost for the ASP.NET process is outside this repo's scope

Prices change — verify against [Cloudflare Images pricing](https://developers.cloudflare.com/images/pricing/) before production budgeting.

## Known limitations

- No database / search / listing endpoint (by design)
- No frontend
- Video direct-upload endpoint currently covers the basic POST flow (up to 200 MB); tus resumable upload provisioning is not yet implemented
- No Workers / R2 / queues
- Optional API key only (no OAuth/JWT)
- Local provider URLs are for development only
- Cloudflare custom IDs with path separators are fully URL-encoded for GET/DELETE upstream calls

## Replacement / PUT non-atomicity

Cloudflare Images does **not** provide a binary overwrite endpoint. Cloudflare `PATCH` only updates metadata/access-control fields.

This API implements replace as:

1. verify existing image
2. delete existing Cloudflare image
3. upload the new image using the **same** custom ID/path
4. return the resulting CDN URL

That sequence is **not fully atomic**. If step 3 fails after step 2 succeeds, the previous image may already be gone. Callers/operators should treat PUT as best-effort replacement and retry carefully.
