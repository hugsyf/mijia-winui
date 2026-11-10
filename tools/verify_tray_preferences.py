"""Offline checks for account isolation and tray device selection validation."""
import hashlib
import json
import logging
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "backend"))


class TrayPreferencesChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        (ROOT / "tmp").mkdir(exist_ok=True)
        cls.storage = tempfile.TemporaryDirectory(prefix="tray-check-", dir=ROOT / "tmp")
        assert ROOT in Path(cls.storage.name).resolve().parents
        cls.old_local = os.environ.get("LOCALAPPDATA")
        os.environ["LOCALAPPDATA"] = cls.storage.name
        global worker
        import worker

    @classmethod
    def tearDownClass(cls):
        logging.shutdown()
        if cls.old_local is None:
            os.environ.pop("LOCALAPPDATA", None)
        else:
            os.environ["LOCALAPPDATA"] = cls.old_local
        cls.storage.cleanup()

    def setUp(self):
        worker.api = SimpleNamespace(auth_data={"userId": self.id()})
        worker.account_dir = None
        worker.devices = {"lamp": {"homeId": "home-a"}, "fan": {"homeId": "home-a"},
                          "other": {"homeId": "home-b"}}

    def save(self, pins, home="home-a"):
        return worker.dispatch("SetTrayPreferences", {"homeId": home, "pins": pins})

    def read(self, home="home-a"):
        return worker.dispatch("GetTrayPreferences", {"homeId": home})

    def test_selection_and_order_survive_reload(self):
        self.save(["fan", "lamp"])
        worker.account_dir = None
        self.assertEqual(self.read(), {"pins": ["fan", "lamp"]})

    def test_account_and_home_are_isolated(self):
        self.save(["lamp"])
        self.assertEqual(self.read("home-b"), {"pins": []})
        worker.api.auth_data = {"userId": "another-test-account"}
        self.assertEqual(self.read(), {"pins": []})

    def test_rejects_cross_home_or_unknown_devices(self):
        for pins in (["other"], ["missing"]):
            with self.subTest(pins=pins), self.assertRaises(ValueError):
                self.save(pins)
        self.assertEqual(self.read(), {"pins": []})

    def test_rejects_duplicates_invalid_types_and_excess_devices(self):
        for pins in (["lamp", "lamp"], [{"id": "lamp"}], "lamp", [str(i) for i in range(13)]):
            with self.subTest(pins=pins), self.assertRaises(ValueError):
                self.save(pins)

    def test_corrupt_cache_returns_empty_selection(self):
        directory = worker.cache_dir()
        path = directory / ("tray-" + hashlib.sha256(b"home-a").hexdigest() + ".json")
        for content in ("not json", json.dumps({"pins": [{}]}), json.dumps([])):
            path.write_text(content, encoding="utf-8")
            self.assertEqual(self.read(), {"pins": []})

    def test_requires_login(self):
        worker.api.auth_data = {}
        with self.assertRaises(Exception):
            self.read()

    def test_speaker_preference_preserves_pins_and_is_home_scoped(self):
        worker.devices["speaker"] = {"homeId": "home-a", "model": "xiaomi.wifispeaker.l15a"}
        self.save(["lamp"])
        worker.dispatch("SetTraySpeaker", {"homeId": "home-a", "speakerId": "speaker"})
        self.assertEqual(self.read(), {"pins": ["lamp"], "speakerId": "speaker"})
        self.save(["fan"])
        self.assertEqual(self.read(), {"pins": ["fan"], "speakerId": "speaker"})
        for home, speaker in (("home-b", "speaker"), ("home-a", "lamp")):
            with self.assertRaises(ValueError):
                worker.dispatch("SetTraySpeaker", {"homeId": home, "speakerId": speaker})

    def test_speaker_commands_follow_the_actual_spec(self):
        from action_support import normalize

        model = "xiaomi.wifispeaker.l15a"
        fixture = json.loads((ROOT / "tools/fixtures/action-specs.json").read_text(encoding="utf-8"))[model]
        props = [{"siid": service["iid"], "piid": prop["iid"], "type": prop["format"], "propertyType": prop["type"]}
                 for service in fixture["services"] for prop in service.get("properties", [])]
        worker.home_rooms = {"home-a": [], "home-b": []}
        worker.devices["speaker"] = {"id": "speaker", "homeId": "home-a", "online": True, "model": model,
                                     "specType": fixture["type"]}
        worker.schemas["speaker"] = {"actions": normalize(fixture, props)}
        calls = []
        worker.api.run_action = lambda payload: calls.append(payload) or {"code": 0}
        params = {"homeId": "home-a", "deviceId": "speaker", "requestId": "1" * 32,
                  "command": "execute-text-directive", "text": " 播放音乐 ", "quiet": True}
        result = worker.dispatch("RunSpeakerAction", params)
        self.assertEqual(result["status"], "accepted")
        self.assertEqual(worker.dispatch("RunSpeakerAction", params), result)
        self.assertEqual(calls, [{"did": "speaker", "siid": 7, "aiid": 4, "in": ["播放音乐", True]}])
        for invalid in ({"homeId": "home-b"}, {"text": " "}, {"text": "😀" * 129},
                        {"quiet": 1}, {"command": "wake-up"}):
            with self.assertRaises(ValueError):
                worker.dispatch("RunSpeakerAction", params | invalid)
        self.assertEqual(len(calls), 1)
        worker.dispatch("RunSpeakerAction", params | {"command": "play-text", "quiet": False, "requestId": "2" * 32})
        worker.dispatch("RunSpeakerAction", params | {"command": "pause", "text": None, "quiet": False, "requestId": "3" * 32})
        self.assertEqual(calls[1:], [{"did": "speaker", "siid": 7, "aiid": 3, "in": ["播放音乐"]},
                                     {"did": "speaker", "siid": 3, "aiid": 3, "in": []}])


if __name__ == "__main__":
    unittest.main()
