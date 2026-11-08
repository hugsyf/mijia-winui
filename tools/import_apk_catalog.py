"""Inspect reference APK metadata and import exact-model public product images.

Does not execute APK code, access accounts, or copy device plugin implementations.
"""
import concurrent.futures
import hashlib
import json
from pathlib import Path
import sys
import zipfile
import requests
sys.path.insert(0, str(Path(__file__).resolve().parent))
from fetch_device_images import trusted

ROOT = Path(__file__).resolve().parents[1]
apk = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/com.xiaomi.smarthome.apk"
catalog_path = "assets/device_config/plugin_config_cn-zh_cn"
dest = ROOT / "src/MiHome.WinUI/Assets/Devices"
with zipfile.ZipFile(apk) as archive:
    catalog = json.loads(archive.read(catalog_path))["result"]["devices"]["list"]
    tiny = json.loads(archive.read("assets/cardControl/home_tiny_icon"))["result"]
    tiny = json.loads(tiny["content"])["download_url"]
    sdk = json.loads(archive.read("assets/plugin_rn_sdk/project.json"))
    card_types = list(json.loads(archive.read("assets/mijia-card-config.json")))
models = set(json.loads((ROOT / "backend/device_profiles.json").read_text(encoding="utf-8")))
models |= {"xiaomi.tv.v1", "yunmai.scales.ms107", "xiaomi.controller.oh4w"}
selected = [r for r in catalog if r.get("model") in models]
apk_hash = hashlib.sha256(apk.read_bytes()).hexdigest()
report = {"apkSha256": apk_hash, "catalog": catalog_path, "catalogDeviceCount": len(catalog),
          "sdk": sdk, "cardTypes": card_types,
          "tinyIconHost": tiny["CardItemIcons_host"]["cn"], "tinyIconCount": len(tiny["CardItemIcons_map"]),
          "models": [{"model": r["model"], "name": r["name"], "productId": r["pd_id"],
                      "icon": r["icon_real"].split("?")[0]} for r in selected]}
(ROOT / "docs/reference-apk-catalog.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
index = json.loads((dest / "index.json").read_text(encoding="utf-8"))

def fetch(row):
    model, url = row["model"], row["icon_real"]
    if not trusted(url): raise ValueError("Untrusted image host")
    # Use the exact publisher-provided public URL. No account/session data is sent.
    with requests.get(url, timeout=25, stream=True, allow_redirects=False) as response:
        response.raise_for_status()
        data = b""
        for chunk in response.iter_content(65536):
            data += chunk
            if len(data) > 2*1024*1024: raise ValueError("Image too large")
    ext = ".png" if data.startswith(b"\x89PNG\r\n\x1a\n") else ".jpg" if data.startswith(b"\xff\xd8\xff") else None
    if ext is None: raise ValueError("Unsupported image content")
    name = model + ext
    (dest / name).write_bytes(data)
    return model, {"file": name, "page": "https://home.miot-spec.com/spec/" + model,
        "url": url.split("?")[0], "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data),
        "source": "reference-apk-catalog", "apkSha256": apk_hash, "catalog": catalog_path, "productId": row["pd_id"]}

pending = [r for r in selected if index.get(r["model"], {}).get("apkSha256") != apk_hash
           or not (dest / index.get(r["model"], {}).get("file", "missing")).is_file()]
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
    jobs = {pool.submit(fetch, row): row["model"] for row in pending}
    for job in concurrent.futures.as_completed(jobs):
        try:
            model, record = job.result(); index[model] = record
            print("IMPORTED", model)
        except Exception as exc:
            print("UNAVAILABLE", jobs[job], type(exc).__name__)
(dest / "index.json").write_text(json.dumps(index, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print(f"Catalog: {len(catalog)} devices; matched {len(selected)} exact models; SDK {sdk['version']}")
