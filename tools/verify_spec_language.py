"""Offline display-label and cache checks. No account or device access."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import os

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "backend"))
from spec_language import SpecLanguage, chinese_labels, localize


class LanguageChecks(unittest.TestCase):
    def setUp(self):
        self.urn = "urn:miot-spec-v2:device:humidifier:0000A00E:xiaomi-600:2:0000D061"
        self.props = json.loads((ROOT / "tools/fixtures/device-card-specs.json").read_text(encoding="utf-8"))["xiaomi.humidifier.600"]["properties"]
        self.labels = chinese_labels(json.loads((ROOT / "tools/fixtures/humidifier-translations.json").read_text(encoding="utf-8")))

    def test_public_response_matches_instances(self):
        mode = next(p for p in localize(self.props, self.labels) if p["siid"] == 2 and p["piid"] == 3)
        self.assertEqual(mode["displayName"], "模式")
        self.assertEqual([v["displayDescription"] for v in mode["values"]], ["恒湿", "睡眠", "强力", "风干"])

    def test_labels_do_not_change_protocol(self):
        before = copy.deepcopy(self.props)
        localized = localize(self.props, self.labels)
        self.assertEqual(before, self.props)
        for old, new in zip(before, localized):
            for key in ("siid", "piid", "serviceType", "propertyType", "name", "service", "type", "unit", "readable", "writable", "range"):
                self.assertEqual(old[key], new[key])
            self.assertEqual([v["value"] for v in old["values"]], [v["value"] for v in new["values"]])

    def test_value_list_uses_index_not_value(self):
        prop = {"siid": 9, "piid": 2, "values": [{"value": 3, "description": "A"}, {"value": 8, "description": "B"}]}
        result = localize([prop], {"service:009:property:002:valuelist:000": "甲", "service:009:property:002:valuelist:001": "乙"})[0]
        self.assertEqual([v["displayDescription"] for v in result["values"]], ["甲", "乙"])
        self.assertEqual([v["value"] for v in result["values"]], [3, 8])

    def test_missing_or_invalid_translations_are_not_guessed(self):
        self.assertEqual(chinese_labels({"data": {"en": {"key": "English"}}}), {})
        self.assertEqual(chinese_labels({"data": {"zh_cn": {"a": "", "b": "bad\nlabel", "c": 1, "d": "正常"}}}), {"d": "正常"})
        self.assertEqual(localize(self.props, {})[0]["displayName"], "")

    def test_success_cache_is_shared_and_persistent(self):
        calls = []
        with tempfile.TemporaryDirectory() as folder:
            cache = SpecLanguage(folder, lambda urn: calls.append(urn) or self.labels)
            self.assertEqual(cache.labels(self.urn), self.labels)
            cache.labels(self.urn)
            other = SpecLanguage(folder, lambda _: self.fail("Cached public labels should not refetch"))
            self.assertEqual(other.labels(self.urn), self.labels)
        self.assertEqual(calls, [self.urn])

    def test_network_failure_keeps_stale_labels_and_has_cooldown(self):
        calls = []
        def fail(urn):
            calls.append(urn)
            raise ValueError("offline")
        with tempfile.TemporaryDirectory() as folder:
            cache = SpecLanguage(folder, fail)
            cache.memory[self.urn] = {"until": 0, "labels": self.labels}
            self.assertEqual(cache.labels(self.urn), self.labels)
            self.assertEqual(cache.labels(self.urn), self.labels)
            self.assertEqual(cache.labels("private-device-id"), {})
        self.assertEqual(calls, [self.urn])

    def test_english_language_and_cache_isolation(self):
        with tempfile.TemporaryDirectory() as folder:
            with patch.dict(os.environ,{"MIHOME_UI_LANGUAGE":"en-US"}):
                self.assertEqual(chinese_labels({"data":{"en":{"name":"Mode"},"zh_cn":{"name":"模式"}}}),{"name":"Mode"})
                english=SpecLanguage(folder,lambda _: {"name":"Mode"})
                self.assertEqual(english.labels(self.urn),{"name":"Mode"})
            with patch.dict(os.environ,{"MIHOME_UI_LANGUAGE":"zh-CN"}):
                chinese=SpecLanguage(folder,lambda _: {"name":"模式"})
                self.assertEqual(chinese.labels(self.urn),{"name":"模式"})
            self.assertNotEqual(english.directory,chinese.directory)


if __name__ == "__main__": unittest.main()
