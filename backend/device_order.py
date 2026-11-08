"""Validate local per-home card ordering. Does not change cloud device order."""
def checked_order(home_id, ids, devices):
    if not isinstance(home_id, str) or not home_id or not isinstance(ids, list) or len(ids) > 2000:
        raise ValueError("设备排序设置不正确。")
    if any(not isinstance(ident, str) or ident not in devices or devices[ident].get("homeId") != home_id for ident in ids):
        raise ValueError("排序中包含其他家庭或未知设备。")
    return list(dict.fromkeys(ids))
