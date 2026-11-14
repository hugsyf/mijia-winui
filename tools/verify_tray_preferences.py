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
from unittest.mock import patch

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
        worker.home_rooms = {"home-a": [], "home-b": []}

    def test_selected_home_survives_reload_and_is_account_scoped(self):
        worker.dispatch("SetSelectedHome", {"homeId": "home-b"})
        worker.account_dir = None
        self.assertEqual(worker.dispatch("GetSelectedHome", {}), {"homeId": "home-b"})
        worker.api.auth_data = {"userId": "another-home-test-account"}
        self.assertEqual(worker.dispatch("GetSelectedHome", {}), {"homeId": None})

    def test_selected_home_is_empty_when_removed_or_cache_is_invalid(self):
        worker.dispatch("SetSelectedHome", {"homeId": "home-b"})
        worker.home_rooms.pop("home-b")
        self.assertEqual(worker.dispatch("GetSelectedHome", {}), {"homeId": None})
        for content in ("not json", "[]", '{"homeId": {}}'):
            (worker.cache_dir() / "selected-home.json").write_text(content, encoding="utf-8")
            self.assertEqual(worker.dispatch("GetSelectedHome", {}), {"homeId": None})

    def test_selected_home_rejects_unknown_or_invalid_values(self):
        for home in ("missing", None, {}, 1):
            with self.subTest(home=home), self.assertRaises(ValueError):
                worker.dispatch("SetSelectedHome", {"homeId": home})

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

    def test_native_card_schema_exposes_rgb_and_only_transport_actions(self):
        speaker=json.loads((ROOT / "examples/adapters/media/preview.json").read_text(encoding="utf-8"))
        lamp=json.loads((ROOT / "examples/adapters/color-presets/preview.json").read_text(encoding="utf-8"))
        worker.devices={ident:{"id":ident,"model":data["model"],"specType":data["specType"],"online":True} for ident,data in (("speaker",speaker),("lamp",lamp))}
        fixtures={"speaker":speaker["schema"],"lamp":lamp["schema"]}
        worker.api.get_devices_prop=lambda keys:[dict(key,code=0,value=0) for key in keys]
        with patch.object(worker,"schema",side_effect=lambda ident:fixtures[ident]):
            states={state["deviceId"]:state for state in worker.read_card_states(["speaker","lamp"])}
        actions=states["speaker"]["schema"]["actions"]
        self.assertEqual({action["actionType"].split(":")[3] for action in actions},{"previous","play","pause","next"})
        self.assertTrue(all(not action["inputs"] for action in actions))
        self.assertTrue(any(prop["propertyType"].split(":")[3]=="volume" for prop in states["speaker"]["schema"]["properties"]))
        self.assertTrue(any(prop["propertyType"].split(":")[3]=="color" for prop in states["lamp"]["schema"]["properties"]))
        fixtures["speaker"]=dict(fixtures["speaker"],actions=[{}])
        with patch.object(worker,"schema",side_effect=lambda ident:fixtures[ident]):
            states={state["deviceId"]:state for state in worker.read_card_states(["speaker","lamp"])}
        self.assertIsNotNone(states["speaker"]["error"])
        self.assertIsNone(states["lamp"]["error"])


if __name__ == "__main__":
    unittest.main()
