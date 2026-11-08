"""Pure mapping helpers shared by device listing and property requests."""
import re


def split_device(device_id):
    match = re.fullmatch(r"(.+)\.s([0-9]+)", str(device_id))
    return (match[1], int(match[2])) if match else (str(device_id), None)


def normalize_device(raw, home, rooms):
    device_id = str(raw["did"])
    parent, _ = split_device(device_id)
    room_id = str(raw.get("room_id") or "")
    match = next((r for r in rooms if str(r["id"]) == room_id), None) if room_id else None
    if match is None:
        match = next((r for r in rooms if device_id in {str(d) for d in r.get("dids", [])}), None)
    if match is None and parent != device_id:
        match = next((r for r in rooms if parent in {str(d) for d in r.get("dids", [])}), None)
    return {"id": device_id, "name": raw.get("name") or raw.get("model", "设备"),
            "model": raw.get("model", ""), "specType": raw.get("spec_type") or "",
            "homeId": str(home), "roomId": str(match["id"]) if match else "",
            "room": match.get("name", "未命名房间") if match else "未分配房间",
            "online": bool(raw.get("isOnline", False)),
            "subclassId": str(raw["subclass_id"]) if raw.get("subclass_id") is not None else ""}


def filter_properties(properties, device_id):
    _, channel = split_device(device_id)
    # A split device may only expose its exact service. Never fall back to another channel.
    return properties if channel is None else [p for p in properties if p["siid"] == channel]
