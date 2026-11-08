"""Offline adapter bridge checks with isolated storage and a fake API."""
import json
import logging
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "backend"))
with tempfile.TemporaryDirectory() as storage:
    os.environ["LOCALAPPDATA"] = storage
    import worker
    worker.api = SimpleNamespace(auth_data={"userId":"test-a"})
    worker.devices = {
        "sensor": {"id":"sensor","homeId":"home-a","roomId":"room-a","model":"example.sensor.one","specType":"urn:miot-spec-v2:device:sensor:00000000:test:1"},
        "other": {"id":"other","homeId":"home-b","roomId":"room-b"},
    }
    preferences = {"pins":["sensor"],"sources":{"room-a":"sensor"}}
    worker.home_preferences({"homeId":"home-a","preferences":preferences},True)
    assert worker.home_preferences({"homeId":"home-a"}) == preferences
    assert worker.home_preferences({"homeId":"home-b"}) == {"pins":[],"sources":{}}
    worker.api.auth_data = {"userId":"test-b"}
    assert worker.home_preferences({"homeId":"home-a"}) == {"pins":[],"sources":{}}
    for invalid in ({"pins":["other"]},{"pins":["sensor","sensor"]},{"sources":{"wrong":"sensor"}}):
        try: worker.home_preferences({"homeId":"home-a","preferences":invalid},True)
        except ValueError: pass
        else: raise AssertionError("Cross-home, duplicate or wrong-room preference accepted")
    props=[{"siid":2,"piid":1,"type":"float","propertyType":"urn:miot-spec-v2:property:temperature:00000020"},
           {"siid":3,"piid":1,"type":"string","propertyType":"urn:miot-spec-v2:property:ssid:00000000"}]
    readings=[{"did":"private-id","siid":2,"piid":1,"code":0,"value":25},
              {"did":"private-id","siid":3,"piid":1,"code":0,"value":"private-network"}]
    with patch.object(worker,"schema",return_value={"properties":props}), patch.object(worker.requests,"get",return_value=SimpleNamespace(raise_for_status=lambda:None,json=lambda:{"type":worker.devices["sensor"]["specType"]})), patch.object(worker,"dispatch",return_value=readings):
        result=worker.adapter_dispatch("ExportAdapterContext",{"deviceId":"sensor","includeReadings":True})
    serialized=json.dumps(result)
    assert "private-id" not in serialized and "private-network" not in serialized
    assert "agentGuide" in result and len(result["readings"])==1
    urn = "urn:miot-spec-v2:device:humidifier:0000A00E:xiaomi-600:2:0000D061"
    worker.devices["localized"] = {"id":"localized","model":"xiaomi.humidifier.600","specType":urn}
    fixture = json.loads((ROOT/"tools/fixtures/device-card-specs.json").read_text(encoding="utf-8"))["xiaomi.humidifier.600"]["properties"]
    cache = worker.cache_dir() / ("spec-v3-" + worker.hashlib.sha256(urn.encode()).hexdigest() + ".json")
    cache.write_text(json.dumps({"properties":fixture,"actions":[]}),encoding="utf-8")
    with patch.object(worker.spec_language,"labels",return_value={"service:002:property:003":"模式"}), patch.object(worker.requests,"get",side_effect=AssertionError("No network in offline bridge check")):
        localized = worker.schema("localized")["properties"]
    mode = next(p for p in localized if p["siid"]==2 and p["piid"]==3)
    assert mode["displayName"]=="模式" and mode["name"]==next(p["name"] for p in fixture if p["siid"]==2 and p["piid"]==3)
    print("Passed: home/account isolation, preference validation, adapter export redaction and guide.")
    print("Passed: cached schema applies display labels without changing raw names or contacting devices.")
    logging.shutdown()
