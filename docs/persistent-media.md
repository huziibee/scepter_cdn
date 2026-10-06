# Persistent Cloudflare media

Verified at 2026-10-06T17:35:00.358015+00:00 using the repository API and the existing GitHub Actions secret.

Successful run: https://github.com/huziibee/scepter_cdn/actions/runs/37504557257

| Asset / endpoint | URL |
|---|---|
| JPEG (custom ID `huziibee/001.jpg`) | https://imagedelivery.net/ySTuqSaEqBvpVysl2D5VVQ/huziibee/001.jpg/public |
| Stream preview | https://customer-nas16iwt0wg861ob.cloudflarestream.com/6f44bb792c9d706113b1585ddc61e533/watch |
| HLS | https://customer-nas16iwt0wg861ob.cloudflarestream.com/6f44bb792c9d706113b1585ddc61e533/manifest/video.m3u8 |
| DASH | https://customer-nas16iwt0wg861ob.cloudflarestream.com/6f44bb792c9d706113b1585ddc61e533/manifest/video.mpd |
| Thumbnail | https://customer-nas16iwt0wg861ob.cloudflarestream.com/6f44bb792c9d706113b1585ddc61e533/thumbnails/thumbnail.jpg |

Video UID: `6f44bb792c9d706113b1585ddc61e533`.
Video metadata `meta.name`: `huziibee/001.mp4`.
Stream `readyToStream=true`; public playback; no scheduled deletion.

All five public URLs returned HTTP 200. The JPEG and one second of HLS video were
successfully decoded by ffmpeg. Both asset records were re-fetched from the repository
API at the end of verification. **Both assets remain in Cloudflare; neither was deleted.**
The workflow has no media deletion requests. The MP4 is three seconds of H.264 video
with AAC audio, 640×360; the image is a genuine 640×360 JPEG.

Full machine-readable results: [live-media.json](live-media.json).
The workflow reuses this recorded video UID and the existing image custom ID on reruns.
It restores the exact Stream metadata name after ingestion if Cloudflare normalizes the
multipart filename. The separate disposable CRUD workflow uses `live-smoke/...` image IDs.

## Application environment

Use these values in a private `.env` in the application's working directory:

```dotenv
IMAGE_PROVIDER=Cloudflare
CLOUDFLARE_ACCOUNT_ID=c3b496d2f2d933c22c92d5ecc6f6fb1f
CLOUDFLARE_ACCOUNT_HASH=ySTuqSaEqBvpVysl2D5VVQ
CLOUDFLARE_API_TOKEN=<inject-the-existing-Cloudflare-token>
CLOUDFLARE_VARIANT=public
MAX_IMAGE_SIZE_BYTES=10485760
LOCAL_IMAGE_ROOT=.local-data/images
SERVICE_API_KEY=<your-private-application-api-key>
ASPNETCORE_ENVIRONMENT=Production
```

Replace both angle-bracket placeholders with secrets on the application host, or inject
those environment variables from a secret store. GitHub cannot disclose an existing
Actions secret; the workflow consumes it directly. Do not commit the real `.env`.
No Images or Stream signing key is required for these public media URLs.

Run from the repository root:

```bash
dotnet run --project src/ImageCdn.Api --no-launch-profile --urls http://localhost:5000
```

The workflow passed all 36 existing tests with Local-provider test settings isolated from
live credentials. The real-media verification then ran separately with the Cloudflare provider.
See [README](../README.md), [.env.example](../.env.example), and [OpenAPI](../openapi.yaml).
