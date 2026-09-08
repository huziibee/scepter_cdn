# Image CDN API

Small, production-oriented ASP.NET Core Web API that provides a clean CRUD interface over **Cloudflare Images**.

Callers upload an image plus a chosen logical path (custom Cloudflare image ID). The API returns a CDN URL. Cloudflare Images is the system of record — this service does not use a database.

Default local development uses an in-process **Local** provider so you can exercise the full API before Cloudflare credentials are available.

## What this service does

- Accepts image uploads with caller-selected nested paths such as `products/nike/air-max/front`
- Creates, reads, replaces, and deletes images through a stable HTTP contract
- Returns CDN URLs (Cloudflare delivery URLs in production; local static URLs in development)
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
2. Enable **Cloudflare Images** (paid Images plan required for storage)
3. Create an **account-scoped API token** with **Images Write** (and read) permission
4. Note:
   - Account ID → `CLOUDFLARE_ACCOUNT_ID`
   - Account hash used in delivery URLs → `CLOUDFLARE_ACCOUNT_HASH`
   - API token → `CLOUDFLARE_API_TOKEN`
5. Confirm the delivery variant name (default `public`) → `CLOUDFLARE_VARIANT`

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
| `GET` | `/health` | Liveness + active provider name |

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
- Use an account-scoped Cloudflare token limited to Images permissions
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
