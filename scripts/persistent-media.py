"""Upload and verify retained Cloudflare fixtures through the running repository API."""
import json
import os
import subprocess
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

BASE = os.environ.get('API_BASE', 'http://127.0.0.1:5000')
IMAGE_ID = 'huziibee/001.jpg'
VIDEO_NAME = 'huziibee/001.mp4'
OUT = Path('artifacts/persistent-media')
OUT.mkdir(parents=True, exist_ok=True)


def request(url, *, payload=None, api=False, cloudflare=False):
    headers = {}
    if api:
        headers['X-Api-Key'] = os.environ['SERVICE_API_KEY']
    if cloudflare:
        headers['Authorization'] = 'Bearer ' + os.environ['CLOUDFLARE_API_TOKEN']
    if payload is not None:
        headers['Content-Type'] = 'application/json'
    req = urllib.request.Request(url, data=None if payload is None else json.dumps(payload).encode(), headers=headers)
    with urllib.request.urlopen(req, timeout=60) as response:
        return response.read(), response.status, response.headers.get('Content-Type', '')


def api(path, payload=None):
    return json.loads(request(BASE + path, payload=payload, api=True)[0])


def cf(path):
    envelope = json.loads(request('https://api.cloudflare.com/client/v4/accounts/' + os.environ['CLOUDFLARE_ACCOUNT_ID'] + path, cloudflare=True)[0])
    assert envelope['success'], 'Cloudflare metadata request failed'
    return envelope['result']


def save(name, value):
    (OUT / name).write_text(json.dumps(value, indent=2) + '\n')


def fetch(label, url, validate):
    assert url, label + ' URL missing'
    for attempt in range(30):
        try:
            data, status, content_type = request(url)
            assert status == 200 and data and validate(data, content_type), label + ' invalid response'
            print(f'{label}: HTTP {status}, {len(data)} bytes, {content_type}', flush=True)
            return {'status': status, 'bytes': len(data), 'contentType': content_type}
        except (urllib.error.URLError, AssertionError):
            if attempt == 29:
                raise
            time.sleep(2)


try:
    image = api('/api/images/' + IMAGE_ID)
    print('Exact image ID already exists; retaining its bytes.', flush=True)
except urllib.error.HTTPError as error:
    if error.code != 404:
        raise
    created = subprocess.check_output([
        'curl', '-fsS', '-X', 'POST', BASE + '/api/images',
        '-H', 'X-Api-Key: ' + os.environ['SERVICE_API_KEY'],
        '-F', 'file=@/tmp/001.jpg;type=image/jpeg;filename=001.jpg',
        '-F', 'path=' + IMAGE_ID,
    ])
    image = json.loads(created)
assert image['id'] == IMAGE_ID
image_url = 'https://imagedelivery.net/' + os.environ['CLOUDFLARE_ACCOUNT_HASH'] + '/' + IMAGE_ID + '/' + os.environ['CLOUDFLARE_VARIANT']
assert image['url'] == image_url, 'Unexpected image delivery URL'
save('image.json', image)
checks = {'image': fetch('JPEG delivery', image_url, lambda data, ct: ct.startswith('image/') and data.startswith(b'\xff\xd8'))}

# Reuse the retained video on reruns; Stream assigns its own UID, unlike Images.
existing = cf('/stream?creator=huziibee')
videos = [v for v in existing if v.get('meta', {}).get('name') == VIDEO_NAME and v.get('status', {}).get('state') != 'error']
if videos:
    uid = videos[0]['uid']
    print('Reusing retained Stream video: ' + uid, flush=True)
else:
    direct = api('/api/videos/direct-upload', {
        'maxDurationSeconds': 60, 'creator': 'huziibee',
        'fileName': VIDEO_NAME, 'requireSignedUrls': False,
    })
    uid = direct['uid']
    # Record UID before uploading. Never save/log the one-time upload capability URL.
    save('video-created.json', {'uid': uid, 'fileName': VIDEO_NAME})
    print('Created persistent Stream video: ' + uid, flush=True)
    subprocess.run([
        'curl', '-fsS', '-X', 'POST', direct['uploadUrl'],
        '-F', 'file=@/tmp/001.mp4;type=video/mp4;filename=' + VIDEO_NAME,
        '-o', '/tmp/stream-upload-result.json',
    ], check=True)

save('video-created.json', {'uid': uid, 'fileName': VIDEO_NAME})
for attempt in range(120):
    video = api('/api/videos/' + uid)
    print('Stream: ' + str(video.get('status')) + ', readyToStream=' + str(video['readyToStream']), flush=True)
    if video['readyToStream']:
        break
    assert (video.get('status') or {}).get('state') != 'error', 'Stream processing failed'
    time.sleep(5)
else:
    raise RuntimeError('Stream did not become ready within 10 minutes; retained UID: ' + uid)
save('video-ready.json', video)
raw_video = cf('/stream/' + uid)
assert raw_video['meta']['name'] == VIDEO_NAME, 'Stream filename metadata mismatch'
assert raw_video['readyToStream'] and not raw_video.get('requireSignedURLs', False)
assert not raw_video.get('scheduledDeletion'), 'Video has a scheduled deletion'
checks['hls'] = fetch('HLS manifest', video['playback']['hls'], lambda data, ct: data.lstrip().startswith(b'#EXTM3U'))
checks['preview'] = fetch('Preview page', video['preview'], lambda data, ct: 'text/html' in ct and b'<html' in data.lower())
if video['playback'].get('dash'):
    checks['dash'] = fetch('DASH manifest', video['playback']['dash'], lambda data, ct: b'<MPD' in data)
if video.get('thumbnail'):
    checks['thumbnail'] = fetch('Thumbnail', video['thumbnail'], lambda data, ct: ct.startswith('image/'))
# Decode actual CDN image and HLS media, rather than merely trusting metadata/HTTP 200.
subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-i', image_url, '-frames:v', '1', '-f', 'null', '-'], check=True)
subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-i', video['playback']['hls'], '-t', '1', '-f', 'null', '-'], check=True)
checks['imageDecoded'] = True
checks['videoDecoded'] = True
assert api('/api/images/' + IMAGE_ID)['id'] == IMAGE_ID
assert api('/api/videos/' + uid)['readyToStream']
result = {
    'verifiedAt': datetime.now(timezone.utc).isoformat(),
    'imageId': IMAGE_ID, 'imageUrl': image_url, 'videoFileName': VIDEO_NAME,
    'videoUid': uid, 'previewUrl': video['preview'], 'hlsUrl': video['playback']['hls'],
    'dashUrl': video['playback'].get('dash'), 'thumbnailUrl': video.get('thumbnail'),
    'readyToStream': True, 'assetsRetained': True, 'checks': checks,
}
save('verification.json', result)
print(json.dumps(result, indent=2), flush=True)
summary = os.environ.get('GITHUB_STEP_SUMMARY')
if summary:
    with open(summary, 'a') as file:
        file.write('## Persistent Cloudflare media\n\nBoth assets remain in Cloudflare. No deletion requests were made.\n\n```json\n' + json.dumps(result, indent=2) + '\n```\n')
