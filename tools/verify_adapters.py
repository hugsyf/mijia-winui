"""Focused offline checks for adapter packages. Never connects to a device."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "backend"))
from adapters import AdapterStore, decode, inspect, resolve, validate, write_guard


class AdapterChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        source = ROOT / "examples/adapters/outlet"
        self.files = {p.name: p.read_bytes() for p in source.iterdir()}
        self.doc = decode(self.files["adapter.json"])
        self.preview = decode(self.files["preview.json"])
        self.device = dict(self.preview, id="device")

    def package(self, files=None):
        target = self.root / "sample.mihome-adapter"
        with zipfile.ZipFile(target, "w") as archive:
            for name, data in (files or self.files).items(): archive.writestr(name, data)
        return target

    def definition(self, document=None, schema=None):
        return resolve(document or self.doc, self.device, schema or self.preview["schema"])

    def test_examples_resolve(self):
        for folder in (ROOT / "examples/adapters").iterdir():
            files = {p.name: p.read_bytes() for p in folder.iterdir()}
            document, preview, definition, _, digest = inspect(self.package(files))
            self.assertEqual(definition["id"], document["id"])
            self.assertEqual(len(digest), 64)
            self.assertEqual(preview["model"], document["models"][0])

    def test_specialized_control_semantics(self):
        source = ROOT / "examples/adapters/lighting"
        document = decode((source / "adapter.json").read_bytes())
        preview = decode((source / "preview.json").read_bytes())
        def check(doc, data):
            validate(doc)
            return resolve(doc, data, data["schema"])
        self.assertEqual(len(check(document, preview)["drawer"]), 5)
        for field, value in (("unit", "hsv"), ("type", "float"), ("range", [0, 16777216, 1])):
            bad = copy.deepcopy(preview)
            prop = next(p for p in bad["schema"]["properties"] if p["siid"] == 2 and p["piid"] == 4)
            prop[field] = value
            doc = copy.deepcopy(document)
            binding = next(b for b in doc["bindings"] if b["id"] == "color")
            if field in ("unit", "type"): binding[field] = value
            with self.assertRaises(ValueError): check(doc, bad)
        for key, value in (("access", "read"), ("unit", "celsius")):
            bad = copy.deepcopy(document)
            next(b for b in bad["bindings"] if b["id"] == "temperature")[key] = value
            with self.assertRaises(ValueError): check(bad, preview)

    def test_countdown_unit_state_and_capability_validation(self):
        source = ROOT / "examples/adapters/countdown"
        document = decode((source / "adapter.json").read_bytes())
        preview = decode((source / "preview.json").read_bytes())
        resolve(validate(document), preview, preview["schema"])
        bad = copy.deepcopy(document)
        bad["bindings"][0]["propertyType"] = "urn:other-spec:property:delay-time:00000002:other:1"
        with self.assertRaises(ValueError): resolve(validate(bad), preview, preview["schema"])
        bad = copy.deepcopy(document)
        bad["bindings"][0]["propertyType"] = "urn:yeelink-spec:property:wrong-name:00000002:device:1"
        with self.assertRaises(ValueError): validate(bad)
        bad = copy.deepcopy(document); bad["requires"] = ["controls.scripts"]
        with self.assertRaises(ValueError): validate(bad)
        bad = copy.deepcopy(document); bad["views"]["drawer"][2]["activeBinding"] = "duration"
        with self.assertRaises(ValueError): resolve(validate(bad), preview, preview["schema"])
        for unit in ("", "celsius", "milliseconds"):
            bad = copy.deepcopy(document); data = copy.deepcopy(preview)
            bad["bindings"][0]["unit"] = unit
            next(p for p in data["schema"]["properties"] if p["siid"] == 3 and p["piid"] == 2)["unit"] = unit
            with self.assertRaises(ValueError): resolve(validate(bad), data, data["schema"])
        bad = copy.deepcopy(document); bad["views"]["drawer"][2]["binding"] = "duration"
        with self.assertRaises(ValueError): resolve(validate(bad), preview, preview["schema"])

    def test_choice_uses_actual_enumeration(self):
        source = ROOT / "examples/adapters/lighting"
        document = decode((source / "adapter.json").read_bytes())
        preview = decode((source / "preview.json").read_bytes())
        bad = copy.deepcopy(document); bad["views"]["drawer"][1]["presentation"] = "grid"
        with self.assertRaises(ValueError): validate(bad)
        bad = copy.deepcopy(preview)
        next(p for p in bad["schema"]["properties"] if p["siid"] == 2 and p["piid"] == 5)["values"] = []
        with self.assertRaises(ValueError): resolve(document, bad, bad["schema"])

    def test_numeric_widgets_validate_actual_limits(self):
        source = ROOT / "examples/adapters/numeric"
        document = decode((source / "adapter.json").read_bytes())
        preview = decode((source / "preview.json").read_bytes())
        resolve(validate(document), preview, preview["schema"])
        for limits in ([1, 100, 1], [1, 6, 2], [1, 5, 0], [1, 5, .5]):
            data = copy.deepcopy(preview)
            next(p for p in data["schema"]["properties"] if p["siid"] == 6 and p["piid"] == 4)["range"] = limits
            with self.assertRaises(ValueError): resolve(document, data, data["schema"])
        for kind in ("number", "level"):
            bad = copy.deepcopy(document); bad["bindings"][0]["access"] = "read"
            bad["views"]["drawer"][0]["kind"] = kind
            with self.assertRaises(ValueError): resolve(validate(bad), preview, preview["schema"])
        bad = copy.deepcopy(document); bad["views"]["drawer"][0]["kind"] = "gauge"; bad["bindings"][0]["access"] = "read"; bad["views"]["cardExpanded"] = []
        resolve(validate(bad), preview, preview["schema"])
        data = copy.deepcopy(preview)
        next(p for p in data["schema"]["properties"] if p["siid"] == 2 and p["piid"] == 1)["range"] = [0, 0, 1]
        with self.assertRaises(ValueError): resolve(bad, data, data["schema"])

    def display_example(self):
        source = ROOT / "examples/adapters/display-controls"
        return decode((source / "adapter.json").read_bytes()), decode((source / "preview.json").read_bytes())

    def test_labels_and_indicator_states_use_real_values(self):
        document, preview = self.display_example()
        definition = resolve(validate(document), preview, preview["schema"])
        self.assertEqual(definition["drawer"][2]["options"][0]["label"], "自动")
        for key, index in (("options", 2), ("states", 1)):
            bad = copy.deepcopy(document); bad["views"]["drawer"][index][key][0]["value"] = 99
            with self.assertRaises(ValueError): resolve(validate(bad), preview, preview["schema"])
            bad = copy.deepcopy(document); bad["views"]["drawer"][index][key].append(copy.deepcopy(bad["views"]["drawer"][index][key][0]))
            with self.assertRaises(ValueError): validate(bad)
        bad = copy.deepcopy(document); bad["views"]["drawer"][1]["states"][0]["tone"] = "guess-normal"
        with self.assertRaises(ValueError): validate(bad)

    def test_readings_accept_only_bounded_readouts(self):
        document, preview = self.display_example()
        for kind in ("toggle", "textInput", "group"):
            bad = copy.deepcopy(document); bad["views"]["drawer"][0]["children"][0]["kind"] = kind
            with self.assertRaises(ValueError): validate(bad)
        for count in (0, 7):
            bad = copy.deepcopy(document); bad["views"]["drawer"][0]["children"] = bad["views"]["drawer"][0]["children"][:1] * count
            with self.assertRaises(ValueError): validate(bad)

    def test_text_edit_limits_are_enforced_before_writes(self):
        document, preview = self.display_example()
        definition = resolve(validate(document), preview, preview["schema"])
        prop = definition["bindings"][0]["property"]
        readings = [{"siid": prop["siid"], "piid": prop["piid"], "code": 0, "value": "old"}]
        guard = lambda value: write_guard(preview, definition, preview["schema"]["properties"], prop, value, readings, ())
        guard("正常文本"); guard("😀" * 32)
        for value in ("x" * 65, "😀" * 33, "line\nbreak", "\x85"):
            with self.assertRaises(ValueError): guard(value)
        for maximum in (0, 257, True):
            bad = copy.deepcopy(document); bad["views"]["full"][0]["maxLength"] = maximum
            with self.assertRaises(ValueError): validate(bad)
        bad = copy.deepcopy(document); bad["bindings"][0]["access"] = "read"
        with self.assertRaises(ValueError): resolve(validate(bad), preview, preview["schema"])

    def test_actual_spec_is_authoritative(self):
        for key, value in (("writable", False), ("readable", False), ("type", "uint8"), ("unit", "percentage")):
            schema = copy.deepcopy(self.preview["schema"])
            schema["properties"][0][key] = value
            with self.assertRaises(ValueError): self.definition(schema=schema)

    def test_ambiguous_semantic_binding(self):
        schema = copy.deepcopy(self.preview["schema"])
        schema["properties"].append(dict(schema["properties"][0], siid=99))
        with self.assertRaises(ValueError): self.definition(schema=schema)
        self.doc["bindings"][0]["siid"] = 2
        self.assertEqual(self.definition(schema=schema)["bindings"][0]["property"]["siid"], 2)

    def test_unknown_widgets_and_capabilities(self):
        doc = copy.deepcopy(self.doc)
        doc["views"]["drawer"][0]["kind"] = "script"
        with self.assertRaises(ValueError): validate(doc)
        doc = copy.deepcopy(self.doc)
        doc["bindings"][0]["capability"] = "temperature"
        with self.assertRaises(ValueError): validate(doc)

    def test_invalid_package_paths_and_code(self):
        for name in ("../escape.png", "C:/evil.png", "assets/NUL.png", "payload.dll", "assets/run.js", "assets/vector.svg"):
            with self.subTest(name=name):
                files = dict(self.files); files[name] = b"payload"
                with self.assertRaises(ValueError): inspect(self.package(files))

    def test_duplicate_json_and_nonfinite_numbers(self):
        for data in (b'{"a":1,"a":2}', b'{"a":NaN}'):
            with self.assertRaises(ValueError): decode(data)

    def test_catalog_sdk_and_readme_compatibility(self):
        self.doc["sdkVersion"] = "0.13.0"
        self.doc["author"] = "Example author"
        self.files["adapter.json"] = json.dumps(self.doc).encode()
        self.files["README.md"] = "# 示例\n仅用于显示的说明。".encode()
        store = AdapterStore(self.root / "store")
        store.install(self.package())
        metadata = store.catalog()[0]
        self.assertEqual(metadata["sdkVersion"], "0.13.0")
        self.assertEqual(metadata["formatVersion"], 1)
        self.assertEqual(metadata["author"], "Example author")
        self.assertEqual(metadata["readme"], self.files["README.md"].decode())
        self.doc.pop("sdkVersion")
        self.files["adapter.json"] = json.dumps(self.doc).encode()
        store.install(self.package())
        self.assertEqual(store.catalog()[0]["sdkVersion"], "")
        self.doc["sdkVersion"] = "not-a-version"
        with self.assertRaises(ValueError): validate(self.doc)

    def test_hash_update_rollback_disable_and_default(self):
        package = self.package(); digest = inspect(package)[4]
        store = AdapterStore(self.root / "store")
        store.install(package, digest)
        choices = {"models": {self.device["model"]: self.doc["id"]}}
        self.assertIsNotNone(store.definition(self.device, choices, self.preview["schema"]))
        self.doc["version"] = "1.0.1"
        files = dict(self.files, **{"adapter.json": json.dumps(self.doc).encode()})
        self.package(files)
        with self.assertRaises(ValueError): store.install(package, digest)
        store.install(package)
        self.assertTrue(store.catalog()[0]["canRollback"])
        store.rollback(self.doc["id"])
        self.assertEqual(store.catalog()[0]["version"], "1.0.0")
        store.enable(self.doc["id"], False)
        self.assertEqual(store.chosen(self.device, choices), "")
        store.enable(self.doc["id"], True)
        choices["devices"] = {"device": "$builtin"}
        self.assertEqual(store.chosen(self.device, choices), "")
        store.remove(self.doc["id"])
        self.assertEqual(store.catalog(), [])

    def test_write_requires_declaration_fresh_value_and_clear_fault(self):
        definition = self.definition()
        properties = self.preview["schema"]["properties"]
        power = definition["bindings"][0]["property"]
        readings = [{"siid":2,"piid":1,"code":0,"value":False}, {"siid":2,"piid":3,"code":0,"value":0}]
        def guard(values, prop=power):
            return write_guard(self.device, definition, properties, prop, True, values, (), (self.device["model"],))
        guard(readings)
        with self.assertRaises(ValueError): guard([])
        with self.assertRaises(ValueError): guard([dict(readings[0]),dict(readings[1],value=1)])
        with self.assertRaises(ValueError): guard(readings, definition["bindings"][1]["property"])

    def test_wireless_and_unknown_switch_mode_block_write(self):
        definition = self.definition(); properties = copy.deepcopy(self.preview["schema"]["properties"])
        mode = dict(properties[0], piid=88, propertyType="urn:miot-spec-v2:property:mode:00000008", type="uint8")
        properties.append(mode); power = definition["bindings"][0]["property"]
        readings = [{"siid":2,"piid":1,"code":0,"value":False},{"siid":2,"piid":3,"code":0,"value":0},{"siid":2,"piid":88,"code":0,"value":1}]
        with self.assertRaises(ValueError): write_guard(self.device,definition,properties,power,True,readings,(self.device["model"],))
        with self.assertRaises(ValueError): write_guard(self.device,definition,properties,power,True,readings,())
        readings[-1]["value"] = 0
        write_guard(self.device,definition,properties,power,True,readings,(self.device["model"],))


if __name__ == "__main__": unittest.main()
