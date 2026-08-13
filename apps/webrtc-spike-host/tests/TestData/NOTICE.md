# H.264 decoder fixture

The `.h264.b64` files are Chromium H.264 media-test access units stored as
base64 so the binary fixtures remain reviewable by text tooling. They contain
`bear-320x192-baseline-frame-{0,1,2,3}.h264` and the first complete keyframe
from `test-25fps.h264`.

- Source directory: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/media/test/data
- Chromium source license: https://chromium.googlesource.com/chromium/src/+/refs/heads/main/LICENSE
- Retained license text: `CHROMIUM-LICENSE.txt`
