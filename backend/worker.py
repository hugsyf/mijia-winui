"""Version 1 JSON-lines bridge. stdout is reserved for protocol messages."""
from datetime import datetime, timezone
import hashlib
import json
import logging
from logging.handlers import RotatingFileHandler
import math
import os
from pathlib import Path
import queue
import sys
import threading
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))

import requests
from mijiaAPI import mijiaAPI
from mijiaAPI.errors import LoginError
from mijiaAPI.logger import logger as upstream_logger
from secure_store import load, save
from device_data import normalize_device, split_device, filter_properties
from device_insights import consumable_groups, consumable_readings, energy_profile, energy_readings
from device_order import checked_order
from adapters import AdapterStore, inspect as inspect_adapter, write_guard, condition_guard
from spec_language import SpecLanguage, localize
from action_support import normalize as normalize_actions, ActionExecutor, ActionUnknown, arguments as action_arguments
from speaker_actions import is_speaker, run_speaker_action
from card_profiles import card_properties, template_for, apply_appearance, CATEGORIES, PROFILES, CARD_SIZES, PURPOSE_SWITCHES

PROTOCOL = 1
ROOT = Path(os.environ["LOCALAPPDATA"]) / "MiHomeWinUI"
ROOT.mkdir(parents=True, exist_ok=True)
logging.basicConfig(level=logging.INFO, handlers=[RotatingFileHandler(
    ROOT / "backend.log", maxBytes=262144, backupCount=2, encoding="utf-8")],
    format="%(asctime)s %(levelname)s %(message)s")
# Upstream logs can contain tokens, request bodies and device identifiers.
upstream_logger.disabled = True
_request = requests.sessions.Session.request


def timed_request(self, method, url, **kwargs):
    kwargs.setdefault("timeout", (10, 25))
    return _request(self, method, url, **kwargs)


requests.sessions.Session.request = timed_request
output_lock = threading.Lock()
state_lock = threading.RLock()
pending = queue.PriorityQueue()
sequence = 0
generation = 0
devices = {}
schemas = {}
account_dir = None
home_rooms = {}
cancelled_runs = {}
adapter_store = AdapterStore(ROOT / "adapters")
spec_language = SpecLanguage(ROOT / "spec-language")
action_executor = ActionExecutor()


def emit(message):
    with output_lock:
        print(json.dumps({"version": PROTOCOL, **message}, ensure_ascii=False,
                         allow_nan=False), flush=True)


def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")
    os.replace(temp, path)


class Api(mijiaAPI):
    def __init__(self, auth=None, login_generation=None):
        # Pass a path that is never read or written by this subclass.
        super().__init__(str(ROOT / "unused-auth"))
        self.locale = "zh_CN"
        self.login_generation = login_generation
        self.auth_data = auth or {}
        if self.auth_data:
            self._init_session()

    def _get_consumable_items(self, home_id):
        # The pinned library reads only the first status group from this response.
        result = self._request("/v2/home/standard_consumable_items", {
            "home_id": int(home_id), "owner_id": self._get_home_owner(home_id), "filter_ignore": True})
        return self._add_home_id(consumable_groups(result), home_id)

    def _save_auth_data(self):
        with state_lock:
            if self.login_generation is not None and self.login_generation != generation:
                return
            save(ROOT / "auth.dat", self.auth_data)


try:
    api = Api(load(ROOT / "auth.dat"))
except Exception as exc:
    logging.warning("credential_load_failed %s", type(exc).__name__)
    api = Api()


def require_auth():
    if not api.auth_data:
        raise LoginError(-1, "需要登录")


def account_key():
    require_auth()
    uid = api.auth_data.get("userId")
    if not uid:
        raise LoginError(-1, "需要登录")
    return hashlib.sha256(str(uid).encode()).hexdigest()[:20]


def check_automation(params, device=None):
    scope = params.get("automation")
    if scope is None:
        return
    with state_lock:
        cancelled = scope["runId"] in cancelled_runs
    if cancelled or time.time() >= scope["expiresAt"]:
        raise ValueError("自动化已取消或请求已过期。")
    if scope["account"] != account_key():
        raise ValueError("自动化账号已变更。")
    if "homeId" in params and params["homeId"] != scope["homeId"]:
        raise ValueError("此场景不属于规则家庭。")
    if device is not None and (device["homeId"] != scope["homeId"] or device["specType"] != scope["specType"] or not device["online"]):
        raise ValueError("设备离线或规格已变更。")


def list_scenes(home_id):
    if home_id not in home_rooms:
        raise ValueError("此家庭不可用，请刷新家庭列表。")
    return [{"id": str(scene["scene_id"]), "name": str(scene["name"]), "homeId": home_id}
            for scene in api.get_scenes_list(home_id) if scene.get("scene_id") and scene.get("name")]


def cache_dir():
    global account_dir
    require_auth()
    uid = str(api.auth_data.get("userId", ""))
    account_dir = ROOT / "cache" / hashlib.sha256(uid.encode()).hexdigest()[:20]
    account_dir.mkdir(parents=True, exist_ok=True)
    return account_dir


def login(my_generation):
    global api
    try:
        candidate = Api(login_generation=my_generation)
        data = candidate._get_qr_login_data()
        with state_lock:
            if generation != my_generation:
                return
            emit({"event": "Login", "sessionId": my_generation,
                  "data": {"status": "waiting", "qr": data["loginUrl"]}})
        candidate._complete_qr_login(data)
        with state_lock:
            if generation != my_generation:
                return
            candidate.login_generation = None
            api = candidate
            emit({"event": "Login", "sessionId": my_generation,
                  "data": {"status": "authenticated"}})
    except Exception as exc:
        logging.info("login_failed %s", type(exc).__name__)
        with state_lock:
            if generation == my_generation:
                emit({"event": "Login", "sessionId": my_generation,
                      "data": {"status": "failed", "message": "二维码已过期或连接失败，请重试。"}})


def schema(device_id):
    if device_id in schemas:
        return schemas[device_id]
    device = devices.get(device_id)
    if not device:
        raise ValueError("请刷新设备列表后重试。")
    urn = device["specType"]
    if not urn:
        return {"properties": []}
    filename = cache_dir() / ("spec-v3-" + hashlib.sha256(urn.encode()).hexdigest() + ".json")
    def present(result):
        labels = spec_language.labels(urn)
        properties = localize(result["properties"], labels)
        lookup = {(p["siid"],p["piid"]):p for p in properties}
        _, channel = split_device(device_id)
        actions = []
        for original in result.get("actions", []):
            if channel is not None and original["siid"] != channel: continue
            action = dict(original)
            action["displayName"] = labels.get(f"service:{action['siid']:03d}:action:{action['aiid']:03d}", "")
            action["inputs"] = [lookup[(p["siid"],p["piid"])] for p in action["inputs"]]
            action["outputs"] = [lookup[(p["siid"],p["piid"])] for p in action["outputs"]]
            actions.append(action)
        return {"properties":filter_properties(properties,device_id), "actions":actions}
    if filename.exists():
        try:
            result = present(json.loads(filename.read_text(encoding="utf-8")))
            schemas[device_id] = result
            return result
        except (ValueError, OSError, KeyError): pass
    try:
        response = requests.get("https://miot-spec.org/miot-spec-v2/instance", params={"type": urn},timeout=20)
        response.raise_for_status()
        raw = response.json()
        if raw.get("type") != urn: raise ValueError("设备规格不匹配。")
    except (requests.RequestException, ValueError):
        legacy = cache_dir() / ("spec-v2-" + hashlib.sha256(urn.encode()).hexdigest() + ".json")
        if not legacy.exists(): raise
        result = present(json.loads(legacy.read_text(encoding="utf-8")))
        schemas[device_id] = result
        return result
    props = []
    for service in raw.get("services", []):
        if ":device-information:" in service.get("type", ""):
            continue
        for p in service.get("properties", []):
            props.append({"siid": service["iid"], "piid": p["iid"],
                          "service": service.get("description", "服务"),
                          "serviceType": service.get("type", ""),
                          "propertyType": p.get("type", ""),
                          "name": p.get("description", p.get("type", "属性")),
                          "type": p.get("format", "unknown"), "unit": p.get("unit", ""),
                          "readable": "read" in p.get("access", []),
                          "writable": "write" in p.get("access", []),
                          "range": p.get("value-range") or [],
                          "values": p.get("value-list") or []})
    result = {"properties": props, "actions":normalize_actions(raw, props)}
    atomic_json(filename, result)
    result = present(result)
    schemas[device_id] = result
    return result


def adapter_choices():
    try: return json.loads((cache_dir()/"adapter-choices.json").read_text(encoding="utf-8"))
    except (OSError, ValueError): return {"devices":{},"models":{}}


def decorate_adapters(items):
    choices=adapter_choices()
    for device in items:
        device.pop("adapter",None); device.pop("adapterIssue",None)
        if adapter_store.chosen(device,choices):
            try: device["adapter"]=adapter_store.definition(device,choices,schema(device["id"]))
            except (OSError,ValueError,KeyError,TypeError): device["adapterIssue"]="适配不兼容，已使用默认面板"
    return items


def present_devices(items):
    for device in items:
        device["supportsEnergyHistory"] = energy_profile(device) is not None
    return decorate_adapters(items)


def adapter_dispatch(method, params):
    if method=="ListAdapters": return adapter_store.catalog()
    if method=="InspectAdapter":
        doc,preview,definition,_,package_hash=inspect_adapter(params["path"])
        return {"document":doc,"preview":preview,"definition":definition,"packageHash":package_hash}
    if method=="InstallAdapter":
        if not isinstance(params.get("expectedHash"),str) or len(params["expectedHash"])!=64: raise ValueError("请先检查适配包。")
        doc=adapter_store.install(params["path"],params["expectedHash"])
        choices=adapter_choices()
        if params.get("applyModels"):
            for model in doc["models"]: choices.setdefault("models",{})[model]=doc["id"]
            atomic_json(cache_dir()/"adapter-choices.json",choices)
        adapter_store.enable(doc["id"],True)
        return {"id":doc["id"]}
    if method in ("EnableAdapter","RemoveAdapter","RollbackAdapter"):
        if method=="EnableAdapter": adapter_store.enable(params["id"],params["enabled"])
        elif method=="RemoveAdapter": adapter_store.remove(params["id"])
        else: adapter_store.rollback(params["id"])
        return {}
    if method=="SetDeviceAdapter":
        device=devices[params["deviceId"]];ident=params["id"]
        choices=adapter_choices()
        choices.setdefault("devices",{})[device["id"]]=ident
        if ident!="$builtin" and adapter_store.definition(device,choices,schema(device["id"])) is None: raise ValueError("适配包已停用。")
        atomic_json(cache_dir()/"adapter-choices.json",choices)
        return {}
    if method=="ExportAdapterContext":
        device=devices[params["deviceId"]]
        response=requests.get("https://miot-spec.org/miot-spec-v2/instance",params={"type":device["specType"]},timeout=20)
        response.raise_for_status()
        raw=response.json()
        if not isinstance(raw,dict) or raw.get("type")!=device["specType"]: raise ValueError("设备规格不匹配，未导出资料。")
        result={"formatVersion":1,"model":device["model"],"specType":device["specType"],"schema":schema(device["id"]),"miotSpec":raw,"agentGuide":(Path(__file__).with_name("adapter-agent-guide.md")).read_text(encoding="utf-8")}
        if params.get("includeReadings"):
            safe_names={"on","mode","fault","temperature","relative-humidity","target-humidity","battery-level","filter-life-level","electric-power","download-speed","upload-speed","connected-device-number","illumination","occupancy-status","contact-state","brightness","color-temperature"}
            allowed={(p["siid"],p["piid"]) for p in result["schema"]["properties"] if p["type"]!="string" and p["propertyType"].split(":")[3] in safe_names}
            result["readings"]=[{k:v[k] for k in ("siid","piid","code","value") if k in v} for v in dispatch("ReadProperties",{"deviceId":device["id"]}) if (v.get("siid"),v.get("piid")) in allowed and type(v.get("value")) in (bool,int,float)]
        return result
    raise ValueError("不支持的适配操作。")


def home_preferences(params, save=False):
    home=str(params["homeId"])
    path=cache_dir()/("home-"+hashlib.sha256(home.encode()).hexdigest()+".json")
    if not save:
        try: return json.loads(path.read_text(encoding="utf-8"))
        except (OSError,ValueError): return {"pins":[],"sources":{}}
    value=params["preferences"]
    if not isinstance(value,dict) or set(value)-{"pins","sources"}: raise ValueError("首页设置不正确。")
    pins=value.get("pins",[]);sources=value.get("sources",{})
    if not isinstance(pins,list) or len(pins)>12 or len(set(pins))!=len(pins) or not isinstance(sources,dict): raise ValueError("首页设置不正确。")
    for ident in pins+list(sources.values()):
        if ident not in devices or devices[ident]["homeId"]!=home: raise ValueError("设备不属于当前家庭。")
    for room,ident in sources.items():
        if devices[ident]["roomId"]!=room: raise ValueError("传感器不属于此房间。")
    atomic_json(path,{"pins":pins,"sources":sources})
    return value


def card_preferences():
    try:
        result = json.loads((cache_dir() / "card-appearance.json").read_text(encoding="utf-8"))
        return result if isinstance(result, dict) else {}
    except (OSError, ValueError):
        return {}


def tray_preferences(params, method):
    home = params["homeId"]
    if not isinstance(home, str) or not home or len(home) > 512:
        raise ValueError("家庭标识不正确。")
    path = cache_dir() / ("tray-" + hashlib.sha256(home.encode()).hexdigest() + ".json")
    try:
        saved = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        saved = {}
    if not isinstance(saved, dict):
        saved = {}
    pins = saved.get("pins")
    if not isinstance(pins, list) or len(pins) > 12 or any(not isinstance(ident, str) for ident in pins):
        pins = []
    preferences = {"pins": list(dict.fromkeys(pins))}
    speaker = saved.get("speakerId")
    if isinstance(speaker, str) and 0 < len(speaker) <= 512:
        preferences["speakerId"] = speaker
    if method == "SetTrayPreferences":
        pins = params.get("pins")
        if (not isinstance(pins, list) or len(pins) > 12 or
                any(not isinstance(ident, str) for ident in pins) or len(set(pins)) != len(pins)):
            raise ValueError("最多选择 12 台设备，且不能重复。")
        if any(ident not in devices or devices[ident]["homeId"] != home for ident in pins):
            raise ValueError("设备不属于当前家庭。")
        preferences["pins"] = pins
    if method == "SetTraySpeaker":
        speaker = params.get("speakerId")
        device = devices.get(speaker) if isinstance(speaker, str) and 0 < len(speaker) <= 512 else None
        if not device or device["homeId"] != home or not is_speaker(device):
            raise ValueError("音箱不属于当前家庭或已移除。")
        preferences["speakerId"] = speaker
    if method != "GetTrayPreferences":
        atomic_json(path, preferences)
    return preferences


def read_card_states(device_ids):
    if not isinstance(device_ids, list) or len(device_ids) > 200:
        raise ValueError("卡片请求数量不正确。")
    result, requests_by_key = [], {}
    for ident in dict.fromkeys(device_ids):
        device = devices.get(ident)
        if not device or not device["online"] or (not device.get("adapter") and device["model"] not in PROFILES and not template_for(device.get("specType", ""))):
            continue
        try:
            props = card_properties(device["model"], schema(ident)["properties"], device.get("specType", ""))
            if device.get("adapter"):
                props = list({(p["siid"],p["piid"]):p for p in [b["property"] for b in device["adapter"]["bindings"]]+[p for p in schema(ident)["properties"] if p["readable"] and p["propertyType"].split(":")[3] in ("mode","fault")]}.values())
        except LoginError:
            raise
        except Exception:
            result.append({"deviceId": ident, "schema": {"properties": []}, "values": [],
                           "readAt": None, "error": "状态暂不可用"})
            continue
        if not props and not (device.get("adapter") or {}).get("actions"):
            continue
        target, _ = split_device(ident)
        state = {"deviceId": ident, "schema": {"properties": props,"actions":[a["action"] for a in (device.get("adapter") or {}).get("actions",[])]}, "values": [],
                 "readAt": None, "error": None}
        result.append(state)
        for p in props:
            if not p["readable"]:
                continue
            key = (target, p["siid"], p["piid"])
            requests_by_key.setdefault(key, []).append(state)
    keys = list(requests_by_key)
    for start in range(0, len(keys), 20):
        batch = keys[start:start + 20]
        try:
            values = api.get_devices_prop([{"did": k[0], "siid": k[1], "piid": k[2]} for k in batch])
        except LoginError:
            raise
        except Exception:
            values = []
        received = {(str(v.get("did")), v.get("siid"), v.get("piid")): v for v in values}
        for key in batch:
            value = received.get(key)
            for state in requests_by_key[key]:
                if value is not None:
                    # Do not copy unrelated upstream metadata into the UI contract.
                    state["values"].append({k: value[k] for k in ("siid", "piid", "code", "value") if k in value})
                if value is None or value.get("code") != 0 or "value" not in value:
                    state["error"] = "部分状态暂不可用"
                else:
                    state["readAt"] = datetime.now(timezone.utc).isoformat()
    return result


def checked_property(params, write=False):
    prop = next((p for p in schema(params["deviceId"])["properties"]
                 if p["siid"] == params["siid"] and p["piid"] == params["piid"]), None)
    if not prop or not prop["writable" if write else "readable"]:
        raise ValueError("此属性不支持该操作。")
    return prop


def validate(prop, value):
    kind = prop["type"]
    if kind == "bool":
        valid = type(value) is bool
    elif kind.startswith(("uint", "int")):
        valid = type(value) is int
        if valid and kind.startswith("uint"):
            valid = value >= 0
    elif kind in ("float", "double"):
        valid = type(value) in (int, float) and math.isfinite(value)
    elif kind == "string":
        valid = isinstance(value, str)
    else:
        valid = False
    if not valid:
        raise ValueError("属性值的类型不正确。")
    limits = prop["range"]
    if limits:
        if not limits[0] <= value <= limits[1]:
            raise ValueError("属性值超出允许范围。")
        if len(limits) > 2 and limits[2] > 0:
            steps = (value - limits[0]) / limits[2]
            if not math.isclose(steps, round(steps), abs_tol=1e-6):
                raise ValueError("属性值不符合步长。")
    if prop["values"] and value not in [v["value"] for v in prop["values"]]:
        raise ValueError("请选择列表中的值。")


def run_declared_action(params):
    device = devices.get(params.get("deviceId"))
    if not device or not device["online"]: raise ValueError("设备离线或已移除。")
    spec = schema(device["id"])
    definition = adapter_store.definition(device,adapter_choices(),spec)
    if definition is None or definition["id"] != params.get("adapterId"): raise ValueError("动作适配已变更或停用，请重新打开设备。")
    declared = next((a for a in definition.get("actions",[]) if a["id"] == params.get("actionId")), None)
    if declared is None: raise ValueError("适配包未声明此动作。")
    if params.get("confirmed") is not True: raise ValueError("请先确认执行动作。")
    action = declared["action"]
    action_arguments(action,params.get("arguments"))
    readings = dispatch("ReadProperties",{"deviceId":device["id"]})
    condition_guard(definition,readings,action_id=params["actionId"])
    values = {(v.get("siid"),v.get("piid")):v.get("value") for v in readings if v.get("code")==0}
    for prop in spec["properties"]:
        if prop["siid"] != action["siid"]: continue
        semantic = prop["propertyType"].split(":")[3]
        value = values.get((prop["siid"],prop["piid"]))
        if semantic == "mode" and ":switch:" in prop["serviceType"] and (device["model"] not in PURPOSE_SWITCHES or type(value) is not int or value != 0):
            raise ValueError("当前开关模式不允许执行动作。")
        stopping = action["actionType"].split(":")[3] in ("stop","pause","stop-self-clean","cancel-cooking","stop-alarm")
        if semantic == "fault" and not stopping and (type(value) not in (int,float) or value != 0): raise ValueError("请先确认设备故障状态。")
    target,_ = split_device(device["id"])
    scope = (str(api.auth_data.get("userId","")),device["id"],definition["id"])
    return action_executor.execute(api,scope,target,action,params["arguments"],params.get("requestId"))


def dispatch(method, params):
    global generation, api, account_dir
    if method in ("GetTrayPreferences", "SetTrayPreferences", "SetTraySpeaker"):
        require_auth()
        return tray_preferences(params, method)
    if method == "RunSpeakerAction":
        require_auth()
        device = devices.get(params.get("deviceId"))
        if (not device or not device["online"] or not is_speaker(device) or
                params.get("homeId") != device["homeId"] or device["homeId"] not in home_rooms):
            raise ValueError("音箱离线、已移除或不属于当前家庭。")
        spec = schema(device["id"])
        if adapter_store.chosen(device, adapter_choices()):
            raise ValueError("当前设备使用适配包，请从适配面板操作。")
        return run_speaker_action(api, action_executor, device, spec, params)
    if method == "RunAction":
        require_auth()
        return run_declared_action(params)
    if method in ("ListAdapters","InspectAdapter","InstallAdapter","EnableAdapter","RemoveAdapter","RollbackAdapter","SetDeviceAdapter","ExportAdapterContext"):
        require_auth()
        return adapter_dispatch(method,params)
    if method in ("GetHomePreferences","SetHomePreferences"):
        return home_preferences(params,method=="SetHomePreferences")
    if method == "Hello":
        return {"protocol": PROTOCOL, "backend": "0.21.0"}
    if method == "GetSessionStatus":
        if not api.auth_data:
            return {"authenticated": False}
        # Unlike available, this preserves the distinction between network/auth errors.
        api.check_new_msg()
        return {"authenticated": True}
    if method == "BeginLogin":
        if api.auth_data:
            raise ValueError("请先退出当前账号。")
        with state_lock:
            generation += 1
            current = generation
        # Queue response before login events so the UI knows the generation.
        threading.Timer(0.1, lambda: threading.Thread(target=login, args=(current,), daemon=True).start()).start()
        return {"sessionId": current}
    if method == "CancelLogin":
        with state_lock:
            generation += 1
        return {}
    if method == "Logout":
        with state_lock:
            generation += 1
            if api.auth_data and account_dir is None:
                cache_dir()
            (ROOT / "auth.dat").unlink(missing_ok=True)
            if account_dir is not None:
                import shutil
                shutil.rmtree(account_dir, ignore_errors=True)
            api = Api()
            devices.clear()
            schemas.clear()
            home_rooms.clear()
            account_dir = None
        return {}
    require_auth()
    if method == "GetAutomationAccount":
        return {"account": account_key()}
    if method == "ListScenes":
        return list_scenes(params["homeId"])
    if method == "RunScene":
        home_id, scene_id = params["homeId"], params["sceneId"]
        if not any(scene["id"] == scene_id for scene in list_scenes(home_id)):
            raise ValueError("场景已移除或不属于此家庭。")
        check_automation(params)
        try:
            result = api.run_scene(scene_id, home_id)
        except (requests.RequestException, KeyError, ValueError) as exc:
            raise ActionUnknown("场景结果待确认，请检查设备状态。") from exc
        return {"accepted": result is not False}
    if method == "ListHomes":
        homes = api.get_homes_list()
        home_rooms.update({str(h["id"]): h.get("roomlist", []) for h in homes})
        return [{"id": str(h["id"]), "name": h.get("name", "家庭"),
                 "rooms": [{"id": str(r["id"]), "name": r["name"]}
                           for r in h.get("roomlist", [])]} for h in homes]
    if method == "ListDevices":
        home_id = params["homeId"]
        file = cache_dir() / ("devices-v2-" + hashlib.sha256(home_id.encode()).hexdigest() + ".json")
        if params.get("cached"):
            try:
                result = json.loads(file.read_text(encoding="utf-8"))
                preferences = card_preferences()
                result = [apply_appearance(d, preferences) for d in result]
                devices.update({d["id"]: d for d in result})
                return present_devices(result)
            except (ValueError, OSError):
                return []
        if home_id not in home_rooms:
            homes = api.get_homes_list()
            home_rooms.update({str(h["id"]): h.get("roomlist", []) for h in homes})
        rooms = home_rooms.get(home_id, [])
        result = [normalize_device(d, home_id, rooms) for d in api.get_devices_list(home_id)]
        preferences = card_preferences()
        result = [apply_appearance(d, preferences) for d in result]
        devices.update({d["id"]: d for d in result})
        atomic_json(file, result)
        return present_devices(result)
    if method in ("GetDeviceConsumables", "GetDeviceEnergyHistory"):
        require_auth()
        device = devices.get(params.get("deviceId"))
        home_id = params.get("homeId")
        if not device or home_id != device["homeId"] or home_id not in home_rooms:
            raise ValueError("此设备不在当前家庭中，请刷新设备列表。")
        if method == "GetDeviceConsumables":
            return consumable_readings(api.get_consumable_items(home_id), device["id"])
        return energy_readings(api, device)
    if method == "ReadCardStates":
        return read_card_states(params.get("deviceIds", []))
    if method in ("GetCardOrder", "SetCardOrder"):
        home_id = params.get("homeId")
        if not isinstance(home_id, str) or not home_id:
            raise ValueError("请选择家庭。")
        file = cache_dir() / "card-order.json"
        try:
            orders = json.loads(file.read_text(encoding="utf-8"))
            if not isinstance(orders, dict): orders = {}
        except (OSError, ValueError):
            orders = {}
        if method == "SetCardOrder":
            orders[home_id] = checked_order(home_id, params.get("deviceIds"), devices)
            atomic_json(file, orders)
        result = orders.get(home_id, [])
        return [ident for ident in result if isinstance(ident, str)] if isinstance(result, list) else []
    if method == "SetCardSize":
        ident, size = params.get("deviceId"), params.get("size")
        if ident not in devices or size not in CARD_SIZES:
            raise ValueError("卡片大小设置不正确。")
        preferences = card_preferences()
        choice = preferences.get(ident, {})
        if not isinstance(choice, dict):
            choice = {}
        choice["size"] = size
        preferences[ident] = choice
        atomic_json(cache_dir() / "card-appearance.json", preferences)
        return apply_appearance(devices[ident], preferences)
    if method == "SetCardAppearance":
        ident, category = params.get("deviceId"), params.get("category")
        if ident not in devices or category not in CATEGORIES:
            raise ValueError("卡片外观设置不正确。")
        device = devices[ident]
        if device["model"] not in PURPOSE_SWITCHES | {"cuco.plug.v3"}:
            raise ValueError("此设备不支持更改卡片外观。")
        preferences = card_preferences()
        choice = preferences.get(ident, {})
        if not isinstance(choice, dict):
            choice = {}
        if category:
            choice.update({"category": category, "subclassId": device.get("subclassId", "")})
        else:
            choice.pop("category", None)
            choice.pop("subclassId", None)
        preferences[ident] = choice
        atomic_json(cache_dir() / "card-appearance.json", preferences)
        return apply_appearance(device, preferences)
    if method == "GetDeviceSchema":
        return schema(params["deviceId"])
    if method == "ReadProperties":
        props = schema(params["deviceId"])["properties"]
        target, _ = split_device(params["deviceId"])
        keys = [{"did": target, "siid": p["siid"], "piid": p["piid"]}
                for p in props if p["readable"]]
        values = []
        for start in range(0, len(keys), 20):
            values.extend(api.get_devices_prop(keys[start:start + 20]))
        return values
    if method == "WriteProperty":
        check_automation(params, devices.get(params["deviceId"]))
        prop = checked_property(params, True)
        validate(prop, params["value"])
        device=devices[params["deviceId"]]
        if device.get("adapter"):
            definition=adapter_store.definition(device,adapter_choices(),schema(device["id"]))
            if definition is None: raise ValueError("适配已停用，请刷新设备列表。")
            readings=dispatch("ReadProperties",{"deviceId":device["id"]})
            write_guard(device,definition,schema(device["id"])["properties"],prop,params["value"],readings,PURPOSE_SWITCHES,PROFILES)
            current=next(v["value"] for v in readings if v.get("code")==0 and v.get("siid")==prop["siid"] and v.get("piid")==prop["piid"])
            validate(prop,current)
        target, _ = split_device(params["deviceId"])
        check_automation(params, device)
        result = api.set_devices_prop({"did": target, "siid": prop["siid"],
                                       "piid": prop["piid"], "value": params["value"]})
        if result.get("code") not in (0, 1):
            raise ValueError("设备未接受此值，请刷新状态后重试。")
        return {"accepted": True, "confirmationRequired": True}
    raise ValueError("不支持的操作。")


def consume():
    while True:
        _, _, request = pending.get()
        try:
            if request.get("version") != PROTOCOL:
                raise ValueError("通信版本不兼容。")
            method = request["method"]
            if method == "Shutdown":
                emit({"id": request["id"], "result": {}})
                os._exit(0)
            result = dispatch(method, request.get("params") or {})
            emit({"id": request["id"], "result": result})
        except Exception as exc:
            logging.warning("request_failed method=%s type=%s", request.get("method"), type(exc).__name__)
            if isinstance(exc, LoginError):
                code, message = "auth", "登录已失效，请重新登录。"
            elif isinstance(exc, ActionUnknown):
                code, message = "actionUnknown", str(exc)
            elif isinstance(exc, requests.RequestException):
                code, message = "network", "连接失败，已有状态可能已过期。请检查网络后刷新。"
            elif isinstance(exc, ValueError):
                code, message = "invalid", str(exc)
            else:
                code, message = "backend", "操作失败，请稍后重试。"
            emit({"id": request.get("id"), "error": {"code": code, "message": message}})


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdin.reconfigure(encoding="utf-8")
    threading.Thread(target=consume, daemon=True).start()
    for line in sys.stdin:
        try:
            message = json.loads(line)
            sequence += 1
            priority = 0 if message.get("method") in ("Shutdown", "Logout", "CancelLogin", "WriteProperty") else 1
            # Cancellation must not wait behind a blocking cloud request.
            if message.get("method") == "CancelAutomationRun" and message.get("version") == PROTOCOL:
                run_id = message["params"]["runId"]
                if not isinstance(run_id, str) or len(run_id) != 32:
                    emit({"id": message["id"], "error": {"code": "invalid", "message": "无效的自动化运行标识。"}})
                    continue
                with state_lock:
                    now = time.time()
                    for key, expiry in list(cancelled_runs.items()):
                        if expiry <= now:
                            del cancelled_runs[key]
                    cancelled_runs[run_id] = now + 120
                emit({"id": message["id"], "result": {}})
            elif message.get("method") == "CancelLogin":
                with state_lock:
                    generation += 1
                emit({"id": message["id"], "result": {}})
            else:
                pending.put((priority, sequence, message))
        except (ValueError, KeyError):
            emit({"id": None, "error": {"code": "protocol", "message": "通信格式错误。"}})
