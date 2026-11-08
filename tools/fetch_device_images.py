"""Fetch public, exact-model product images. No account or device IDs are used."""
import concurrent.futures
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlsplit
import requests

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "src/MiHome.WinUI/Assets/Devices"
MODELS = list(json.loads((ROOT / "backend/device_profiles.json").read_text(encoding="utf-8")))

def trusted(url):
    parsed = urlsplit(url)
    return parsed.scheme == "https" and not parsed.username and parsed.hostname and any(
        parsed.hostname.endswith(suffix) for suffix in (".fds.api.xiaomi.com", ".fds.api.mi-img.com", ".fds.api.io.mi.com"))

class Images(HTMLParser):
    def __init__(self): super().__init__(); self.images = []
    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "img" and trusted(values.get("src", "")): self.images.append(values["src"])

def fetch(model):
    page = "https://home.miot-spec.com/spec/" + model
    response = requests.get(page, timeout=20); response.raise_for_status()
    response.encoding = "utf-8"
    if model not in response.text: raise ValueError("Model missing from source page")
    parser = Images(); parser.feed(response.text)
    if not parser.images: raise ValueError("No Xiaomi CDN image")
    url = parser.images[0]
    with requests.get(url, timeout=20, stream=True, allow_redirects=False) as image:
        image.raise_for_status()
        data = b""
        for chunk in image.iter_content(65536):
            data += chunk
            if len(data) > 2 * 1024 * 1024: raise ValueError("Image too large")
    extension = ".png" if data.startswith(b"\x89PNG\r\n\x1a\n") else ".jpg" if data.startswith(b"\xff\xd8\xff") else None
    if extension is None: raise ValueError("Unsupported image content")
    filename = model + extension
    (DEST / filename).write_bytes(data)
    return model, {"file": filename, "page": page, "url": url, "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}

if __name__ == "__main__":
    DEST.mkdir(parents=True, exist_ok=True)
    result = json.loads((DEST / "index.json").read_text(encoding="utf-8")) if (DEST / "index.json").exists() else {}
    missing = [m for m in MODELS if m not in result or not (DEST / result[m]["file"]).is_file()]
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        jobs = {pool.submit(fetch, model): model for model in missing}
        for job in concurrent.futures.as_completed(jobs):
            try:
                model, record = job.result(); result[model] = record
                print("PASS", model, record["file"], record["bytes"])
            except Exception as exc: print("UNAVAILABLE", jobs[job], type(exc).__name__)
    (DEST / "index.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
