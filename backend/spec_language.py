"""Display-only labels from the public MIOT multilingual API. No account data."""
import copy
import os
import hashlib
import json
import time
from pathlib import Path
import requests


def clean(value):
    return value.strip() if isinstance(value, str) and 0 < len(value.strip()) <= 160 and not any(ord(c) < 32 for c in value) else ""


def chinese_labels(payload):
    data = payload.get("data", {}) if isinstance(payload, dict) else {}
    language=os.environ.get("MIHOME_UI_LANGUAGE","zh-CN").lower().replace("-","_")
    labels = data.get(language,data.get(language.split("_")[0],{})) if isinstance(data, dict) else {}
    if not isinstance(labels, dict) or len(labels) > 10000:
        return {}
    return {key: clean(value) for key, value in labels.items() if isinstance(key, str) and len(key) < 100 and clean(value)}


def localize(properties, labels):
    result = copy.deepcopy(properties)
    for prop in result:
        service = f"service:{prop['siid']:03d}"
        key = f"{service}:property:{prop['piid']:03d}"
        prop["displayService"] = clean(labels.get(service))
        prop["displayName"] = clean(labels.get(key))
        for index, value in enumerate(prop.get("values", [])):
            # The API identifies the value-list entry by its index, not its value.
            value["displayDescription"] = clean(labels.get(f"{key}:valuelist:{index:03d}"))
    return result


class SpecLanguage:
    def __init__(self, directory, fetch=None):
        self.directory = Path(directory) / os.environ.get("MIHOME_UI_LANGUAGE","zh-CN")
        self.fetch = fetch or self.download
        self.memory = {}

    @staticmethod
    def download(urn):
        with requests.get("https://miot-spec.org/instance/v2/multiLanguage", params={"urn": urn},
                          timeout=(2, 4), allow_redirects=False, stream=True) as response:
            response.raise_for_status()
            data = bytearray()
            for chunk in response.iter_content(16384):
                data.extend(chunk)
                if len(data) > 512 * 1024:
                    raise ValueError("Translation response too large")
            return chinese_labels(json.loads(data.decode("utf-8")))

    def labels(self, urn):
        if not isinstance(urn, str) or not urn.startswith("urn:miot-spec-v2:device:") or len(urn) > 240:
            return {}
        now = time.time()
        saved = self.memory.get(urn)
        filename = self.directory / (hashlib.sha256(urn.encode()).hexdigest() + ".json")
        if saved is None:
            try:
                if filename.stat().st_size <= 512 * 1024:
                    candidate = json.loads(filename.read_text(encoding="utf-8"))
                    if isinstance(candidate, dict) and isinstance(candidate.get("labels"), dict):
                        saved = candidate
            except (OSError, ValueError):
                pass
        saved = saved or {"until": 0, "labels": {}}
        if not isinstance(saved.get("until"), (int, float)):
            saved["until"] = 0
        if saved["until"] <= now:
            try:
                saved = {"until": now + 7 * 86400, "labels": self.fetch(urn)}
            except (requests.RequestException, OSError, ValueError, TypeError):
                # Retain old labels when offline; avoid retrying for each device.
                saved["until"] = now + 3600
            try:
                self.directory.mkdir(parents=True, exist_ok=True)
                temp = filename.with_suffix(".tmp")
                temp.write_text(json.dumps(saved, ensure_ascii=False), encoding="utf-8")
                temp.replace(filename)
            except OSError:
                pass
        self.memory[urn] = saved
        return saved["labels"]
