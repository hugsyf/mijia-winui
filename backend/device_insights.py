"""Map cloud consumables and statistics to read-only device details."""

from datetime import datetime
import json
import math

from device_data import split_device


def consumable_groups(payload):
    if not isinstance(payload, dict):
        raise ValueError("耗材数据格式不正确。")
    return [item for group in payload.get("items") or [] if isinstance(group, dict)
            for item in group.get("consumes_data") or [] if isinstance(item, dict)]


def _text(value, limit):
    return value.strip()[:limit] if isinstance(value, str) else ""


def _number(value):
    return type(value) in (int, float) and abs(value) <= 1e15 and math.isfinite(value)


def _json(value):
    if not isinstance(value, str):
        return value
    try:
        return json.loads(value) if len(value) <= 1024 else None
    except (ValueError, RecursionError):
        return None


def consumable_readings(items, device_id):
    target, channel = split_device(device_id)
    accepted = {target, device_id} if channel is not None else {target}
    readings = {}
    for item in items:
        if not isinstance(item, dict) or str(item.get("did")) not in accepted:
            continue
        details = item.get("details")
        for detail in details if isinstance(details, list) else [details]:
            if not isinstance(detail, dict):
                continue
            label = _text(detail.get("description"), 256) or _text(detail.get("consumable_type"), 256)
            if not label:
                continue
            raw = detail.get("value")
            value = str(raw) if _number(raw) else _text(raw, 64)
            threshold = _json(detail.get("inadeq"))
            number = _json(raw)
            if (isinstance(threshold, dict) and threshold.get("unit") == "percentage" and
                    _number(number) and 0 <= number <= 100):
                value = f"{number:g}%"
            identity = str(detail.get("id") or detail.get("prop") or label)
            readings[identity] = {"label": label, "value": value or None}
    return list(readings.values())


def energy_profile(device):
    # Upstream verifies the monthly series for this key on the AC Companion Pro.
    _, channel = split_device(device["id"])
    if device["model"] == "lumi.acpartner.mcn04" and channel in (None, 7):
        return {"key": "7.1", "data_type": "stat_month_v3"}
    return None


def energy_readings(api, device):
    profile = energy_profile(device)
    if profile is None:
        raise ValueError("此设备暂不支持用电历史。")
    target, _ = split_device(device["id"])
    now = datetime.now()
    year, month = divmod(now.year * 12 + now.month - 6, 12)
    start = int(datetime(year, month + 1, 1).timestamp())
    end = int(now.timestamp())
    points = api.get_statistics({**profile, "did": target, "limit": 6, "time_start": start, "time_end": end})
    if not isinstance(points, list):
        raise ValueError("用电历史数据格式不正确。")
    readings = {}
    for point in points:
        if not isinstance(point, dict):
            continue
        time = point.get("time")
        if type(time) is not int or not start <= time <= end:
            continue
        value = _json(point.get("value"))
        if isinstance(value, list):
            value = value[0] if value else None
        if _number(value) and value >= 0:
            readings[time] = value
    return [{"time": time, "kilowattHours": readings[time]} for time in sorted(readings, reverse=True)[:6]]
