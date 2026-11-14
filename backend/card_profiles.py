"""Read-only card property selection. Resolve keys from each device's actual MIoT schema."""

import json
from pathlib import Path

DEVICE_PROFILES = json.loads(Path(__file__).with_name("device_profiles.json").read_text(encoding="utf-8"))
PROFILES = {model: {tuple(key.split(".")) for key in profile["properties"]} for model, profile in DEVICE_PROFILES.items()}
PURPOSE_SWITCHES = {model for model, profile in DEVICE_PROFILES.items() if profile.get("purposeIcons")}
TEMPLATES = json.loads(Path(__file__).with_name("device_templates.json").read_text(encoding="utf-8"))

CARD_SIZES = {"", "compact", "standard", "expanded"}
CATEGORIES = {"", "light", "fan", "heater", "television", "computer", "outlet", "switch", "appliance"}

# Verified official subclass IDs; display rank is not the subclass ID.
PURPOSES = json.loads(Path(__file__).with_name("device_purposes.json").read_text(encoding="utf-8"))
SWITCH_ICONS = {key:(value["category"],value["icon"]) for key,value in PURPOSES.items() if value["pluginType"]=="switch"}
PLUG_ICONS = {key:(value["category"],value["icon"]) for key,value in PURPOSES.items() if value["pluginType"]=="socket"}


def semantic(urn):
    parts = urn.split(":")
    return parts[3] if len(parts) > 3 else ""


def is_urn(urn, kind, name):
    return urn.startswith(f"urn:miot-spec-v2:{kind}:{name}:")


def template_for(spec_type):
    return next((t for t in TEMPLATES if any(is_urn(spec_type, "device", name) for name in t["deviceTypes"])), None)


def template_properties(spec_type, properties):
    template = template_for(spec_type)
    if not template:
        return []

    def matches(prop, rule):
        return (prop["readable"] and is_urn(prop.get("serviceType", ""), "service", rule["service"]) and
                is_urn(prop.get("propertyType", ""), "property", rule["property"]) and
                prop["type"] in rule["types"] and (not rule["units"] or prop["unit"] in rule["units"]))

    groups = {p["siid"] for p in properties}
    groups = [siid for siid in groups if all(
        sum(p["siid"] == siid and matches(p, rule) for p in properties) == 1 for rule in template["required"])]
    if len(groups) != 1:
        return []
    siid = groups[0]
    selected = []
    for rule in template["required"] + template["optional"]:
        candidates = [p for p in properties if matches(p, rule) and
                      (rule["service"] in ("battery", "single-use-battery", "power-consumption") or p["siid"] == siid)]
        if len(candidates) == 1:
            selected.extend(candidates)
    selected.extend(p for p in properties if p["siid"] == siid and (
        is_urn(p.get("propertyType", ""), "property", "fault") or
        template["category"] == "outlet" and is_urn(p.get("propertyType", ""), "property", "mode")))
    return list({(p["siid"], p["piid"]): p for p in selected}.values())


def card_properties(model, properties, spec_type=""):
    if model not in PROFILES:
        return template_properties(spec_type, properties)
    allowed = PROFILES.get(model, set()) | {
        ("quick-countdown","left-time"),("quick-countdown","on"),("light","brightness"),("light","color-temperature"),("light","color"),
        ("humidifier","target-humidity"),("humidifier","mode"),("speaker","volume"),
        ("fan","fan-level"),("fan","horizontal-swing"),("air-purifier","fan-level"),("air-purifier","mode"),
        ("curtain","target-position"),("curtain","current-position"),("curtain","motor-control")}
    allowed |= {(semantic(p.get("serviceType","")), semantic(p.get("propertyType",""))) for p in properties if semantic(p.get("propertyType",""))=="fault"}
    return [p for p in properties if (p["readable"] and
            (semantic(p.get("serviceType", "")), semantic(p.get("propertyType", ""))) in allowed) or semantic(p.get("propertyType", ""))=="fault"]


def apply_appearance(device, preferences):
    choice = preferences.get(device["id"], {})
    if not isinstance(choice, dict):
        choice = {}
    device["cardSize"] = choice.get("size", "") if choice.get("size", "") in CARD_SIZES else ""
    # A cloud category change invalidates the local override. Split channels use their own ID.
    table = SWITCH_ICONS if device.get("model") in PURPOSE_SWITCHES else PLUG_ICONS if device.get("model") == "cuco.plug.v3" else {}
    device["cardCategory"], device["cardIcon"] = table.get(str(device.get("subclassId", "")), ("", ""))
    device["cardAppearance"] = choice.get("category", "") if choice.get("subclassId", "") == device.get("subclassId", "") else ""
    device["cardCategory"] = choice.get("category", "") if (
        choice.get("subclassId", "") == device.get("subclassId", "") and
        choice.get("category", "") in CATEGORIES) else device["cardCategory"]
    if device["cardAppearance"] in CATEGORIES and device["cardAppearance"]:
        device["cardIcon"] = ""
    return device
