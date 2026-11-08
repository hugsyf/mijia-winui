"""Targeted offline card selection, split routing and read batching checks. No cloud access."""
import json
import logging
import os
from pathlib import Path
import sys
import tempfile

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / "backend"))
from card_profiles import card_properties, apply_appearance
from device_data import filter_properties

specs = json.loads((root / "tools/fixtures/device-card-specs.json").read_text(encoding="utf-8"))
expected = {
    "xiaomi.switch.w2": {(2, 1), (2, 2), (2, 3), (3, 1), (3, 2)},
    "chuangmi.camera.81ac1": {(2, 1)}, "cuco.plug.v3": {(2, 1), (2, 3), (11, 2)},
    "linp.sensor_ht.ks2bb": {(2, 1001), (2, 1002), (4, 1003)}, "linp.sensor_occupy.es2": {(2, 1078), (2, 1005), (2, 1080), (2, 1079), (4, 1003)},
    "xiaomi.humidifier.600": {(2, 1), (2, 2), (2, 3), (2, 5), (3, 1), (4, 1)},
    "soocare.toothbrush.1501": {(3, 1003), (4, 1041)}, "miaomiaoce.sensor_ht.t9": {(3, 1001), (3, 1002), (2, 1003)},
    "xiaomi.fryer.maf16": {(2, 2), (2, 3), (2, 22)}, "xiaomi.router.rn07": {(2, 3)},
}
for model, keys in expected.items():
    chosen = card_properties(model, specs[model]["properties"])
    assert {(p["siid"], p["piid"]) for p in chosen} == keys, model
    assert all(p["readable"] for p in chosen)
assert not card_properties("unknown.model", specs["xiaomi.switch.w2"]["properties"])
for sid, category in [("5", "light"), ("10", "fan"), ("26", "computer"), ("999", "")]:
    model = "cuco.plug.v3" if sid == "26" else "xiaomi.switch.w2"
    assert apply_appearance({"id": "p.s2", "model": model, "subclassId": sid}, {})["cardCategory"] == category
prefs = {"p.s2": {"category": "heater", "subclassId": "5", "size": "expanded"}}
assert apply_appearance({"id": "p.s2", "model": "xiaomi.switch.w2", "subclassId": "10"}, prefs)["cardSize"] == "expanded"
assert apply_appearance({"id": "p.s2", "model": "xiaomi.switch.w2", "subclassId": "5"}, prefs)["cardCategory"] == "heater"
assert apply_appearance({"id": "p.s3", "model": "xiaomi.switch.w2", "subclassId": "5"}, prefs)["cardCategory"] == "light"
assert apply_appearance({"id": "p.s2", "model": "xiaomi.switch.w2", "subclassId": "10"}, prefs)["cardCategory"] == "fan"

# Official IDs differ from display rank. Keep model and boundary guards.
def appearance(model, sid):
    return apply_appearance({"id": "sample", "model": model, "subclassId": str(sid)}, {})
assert appearance("xiaomi.switch.w2", 5)["cardIcon"] == "ceiling-light"
assert appearance("xiaomi.switch.w2", 10)["cardIcon"] == "exhaust-fan"
assert appearance("xiaomi.switch.w2", 13)["cardCategory"] == "curtain"
assert appearance("xiaomi.switch.w2", 14)["cardCategory"] == "switch"
assert appearance("xiaomi.switch.w2", 26)["cardCategory"] == ""
assert appearance("cuco.plug.v3", 5)["cardCategory"] == ""
assert appearance("cuco.plug.v3", 20)["cardCategory"] == ""
assert appearance("cuco.plug.v3", 26)["cardCategory"] == "computer"
assert appearance("cuco.plug.v3", 28)["cardCategory"] == "fan"
assert appearance("cuco.plug.v3", 31)["cardCategory"] == "water-heater"
assert appearance("cuco.plug.v3", 43)["cardCategory"] == "washer"
assert appearance("cuco.plug.v3", 55)["cardCategory"] == "router"
assert appearance("cuco.plug.v3", 48)["cardIcon"] == "induction-cooktop"
assert appearance("other.model", 5)["cardCategory"] == ""
assert apply_appearance({"id": "p.s2", "model": "xiaomi.switch.w2", "subclassId": "5"}, prefs)["cardIcon"] == ""

with tempfile.TemporaryDirectory(prefix="mihome-card-check-") as temporary:
    os.environ["LOCALAPPDATA"] = temporary
    import worker
    props = specs["xiaomi.switch.w2"]["properties"]
    worker.devices = {ident: {"id": ident, "model": "xiaomi.switch.w2", "online": True}
                      for ident in ("p", "p.s2", "p.s3", "p.s8")}
    worker.schema = lambda ident: {"properties": filter_properties(props, ident)}
    class FakeApi:
        def __init__(self): self.keys = []
        def get_devices_prop(self, keys):
            self.keys.extend(keys)
            return [{**k, "code": 0, "value": False if k["piid"] == 1 else 0} for k in keys]
    api = worker.api = FakeApi()
    states = worker.read_card_states(list(worker.devices))
    assert all(k["did"] == "p" and k["siid"] in (2, 3) for k in api.keys)
    assert len(api.keys) == len({(k["did"], k["siid"], k["piid"]) for k in api.keys}) == 5
    assert len(states) == 3  # An unknown split channel never falls back to another service.
    assert next(s for s in states if s["deviceId"] == "p.s3")["schema"]["properties"][0]["siid"] == 3
    assert any(v.get("value") is False for s in states for v in s["values"])
    api.get_devices_prop = lambda keys: []
    assert all(s["error"] and s["readAt"] is None and s["values"] == [] for s in worker.read_card_states(["p.s2"]))
    logging.shutdown()  # Release the worker log before Windows removes the temporary directory.
print("PASS: 10 public model mappings, category overrides, exact split routing, deduplicated read-only batching, false values and missing responses")

# New verified families must select real properties, never indicator-light relays.
from card_profiles import DEVICE_PROFILES
for model, profile in DEVICE_PROFILES.items():
    selected = card_properties(model, specs[model]["properties"])
    assert selected, model
    assert all(p["readable"] for p in selected)
    if profile.get("channels"):
        relays = [p for p in selected if ":on:" in p["propertyType"]]
        assert [p["siid"] for p in relays] == profile["channels"], model
        for siid in profile["channels"]:
            split = card_properties(model, filter_properties(specs[model]["properties"], f"p.s{siid}"))
            assert split and all(p["siid"] == siid for p in split), model
        assert not filter_properties(specs[model]["properties"], "p.s999")
for model in ("xiaomi.switch.w1", "xiaomi.switch.w2", "xiaomi.switch.w3"):
    assert appearance(model, 5)["cardIcon"] == "ceiling-light"
    assert appearance(model, 10)["cardCategory"] == "fan"
assert appearance("zimi.switch.dhkg05", 5)["cardIcon"] == ""  # No evidence that this model shares icon IDs.
print(f"PASS: {len(DEVICE_PROFILES)} profiles, real property selection and all verified switch channels")

from copy import deepcopy
capabilities = json.loads((root / "tools/fixtures/capability-device-specs.json").read_text(encoding="utf-8"))
for model, spec in capabilities.items():
    selected = card_properties(model, spec["properties"], spec["specType"])
    assert selected == spec["properties"], model
lamp = capabilities["example.light.unlisted"]
assert not card_properties("example.light.unlisted", [{**lamp["properties"][0], "type":"uint8"}], lamp["specType"])
assert not card_properties("example.light.unlisted", lamp["properties"] + [{**lamp["properties"][0], "siid":18}], lamp["specType"])
assert len(card_properties("example.light.unlisted", lamp["properties"][:1], lamp["specType"])) == 1
sensor = capabilities["example.sensor.unlisted"]
assert not card_properties("example.sensor.unlisted", [{**p,"unit":"fahrenheit"} for p in sensor["properties"]], sensor["specType"])
assert not card_properties("example.sensor.unlisted", [{**p,"siid":20} if p["piid"]==42 else p for p in sensor["properties"]], sensor["specType"])
plug = capabilities["example.plug.unlisted"]
mode = deepcopy(specs["xiaomi.switch.w1"]["properties"][1]); mode.update(siid=23,piid=90,readable=False)
selected = card_properties("example.plug.unlisted", plug["properties"]+[mode], plug["specType"])
assert mode in selected
# Exercise the actual read dispatcher with an unlisted split device and unreadable metadata.
worker.devices={"auto.s23":{"id":"auto.s23","model":"example.plug.unlisted","specType":plug["specType"],"online":True}}
worker.schema=lambda ident:{"properties":plug["properties"]+[mode]}
worker.api=FakeApi()
states=worker.read_card_states(["auto.s23"])
assert [(k["did"],k["siid"],k["piid"]) for k in worker.api.keys]==[("auto",23,41)]
assert mode in states[0]["schema"]["properties"]
print("PASS: unlisted capability selection, type/unit/ambiguity checks, guard metadata and exact split read routing")

from device_order import checked_order
order_devices={"a":{"homeId":"h"},"b":{"homeId":"h"},"c":{"homeId":"other"}}
assert checked_order("h",["b","a","b"],order_devices)==["b","a"]
for ids in (["c"],["missing"],[None]):
    try: checked_order("h",ids,order_devices)
    except ValueError: pass
    else: raise AssertionError("Cross-home/invalid order accepted")
with tempfile.TemporaryDirectory(prefix="mihome-order-check-") as directory:
    worker.devices=order_devices
    worker.api.auth_data={"test":True}
    worker.cache_dir=lambda:Path(directory)
    assert worker.dispatch("SetCardOrder",{"homeId":"h","deviceIds":["b","a"]})==["b","a"]
    assert worker.dispatch("GetCardOrder",{"homeId":"h"})==["b","a"]
    assert worker.dispatch("GetCardOrder",{"homeId":"other"})==[]
print("PASS: per-home ordering validation, deduplication and persistence; no cloud writes")
