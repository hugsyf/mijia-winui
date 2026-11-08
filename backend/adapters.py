"""Versioned, data-only device adapters. No plugin code or remote resources execute."""
import hashlib
import io
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import struct
import tempfile
import zipfile
import action_support

KINDS = {"metric", "status", "toggle", "slider", "select", "progress", "group",
         "choice", "brightness", "colorTemperature", "color", "duration", "countdown", "number", "level", "gauge",
         "text", "textInput", "indicator", "readings", "action"}
FEATURES = {"controls.lighting", "controls.choice", "controls.time", "controls.numeric", "controls.text", "controls.indicator", "layout.readings", "labels.options", "actions.v1", "layout.cardExpanded", "conditions.enabled", "labels.localized"}
CATEGORIES = {"light", "switch", "outlet", "humidifier", "temperature-humidity-sensor", "occupancy-sensor", "router", "appliance", "fan", "air-purifier", "curtain", "speaker"}
CAPABILITIES = {
    "temperature": ({"temperature-humidity-sensor", "environment"}, "temperature", "celsius"),
    "humidity": ({"temperature-humidity-sensor", "environment"}, "relative-humidity", "percentage"),
    "battery": ({"battery", "single-use-battery"}, "battery-level", "percentage"),
    "filter": ({"filter"}, "filter-life-level", "percentage"),
    "power": ({"light", "switch", "humidifier"}, "on", ""),
    "electricPower": ({"power-consumption"}, "electric-power", "watt"),
    "connected": ({"router"}, "connected-device-number", ""),
    "download": ({"router"}, "download-speed", ""),
    "upload": ({"router"}, "upload-speed", ""),
}
ID = re.compile(r"[a-z0-9][a-z0-9.-]{1,78}[a-z0-9]\Z")
TYPES = {"bool", "string", "float", "double", "uint8", "uint16", "uint32", "uint64", "int8", "int16", "int32", "int64"}


def semantic(urn):
    return str(urn).split(":")[3] if len(str(urn).split(":")) > 3 else ""


def pairs(items):
    result = {}
    for key, value in items:
        if key in result:
            raise ValueError("JSON 包含重复字段：" + key)
        result[key] = value
    return result


def decode(data):
    if len(data) > 512 * 1024:
        raise ValueError("适配定义过大。")
    return json.loads(data, object_pairs_hook=pairs, parse_constant=lambda _: (_ for _ in ()).throw(ValueError("不支持非有限数值。")))


def atomic(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")
    os.replace(temp, path)


def fields(value, allowed, required=()):
    if not isinstance(value, dict) or set(value) - set(allowed) or set(required) - set(value):
        raise ValueError("适配定义字段不正确。")


def label(value):
    if not isinstance(value, str) or not value.strip() or len(value) > 100 or any(ord(c) < 32 for c in value):
        raise ValueError("名称或标签不正确。")


def validate(document):
    fields(document, {"formatVersion", "id", "name", "version", "author", "models", "specType", "category", "bindings", "views", "icon", "sdkVersion", "requires", "actions", "localization"},
           {"formatVersion", "id", "name", "version", "models", "category", "bindings", "views"})
    if type(document["formatVersion"]) is not int or document["formatVersion"] != 1:
        raise ValueError("不支持此适配包版本。")
    if not ID.fullmatch(str(document["id"])) or not re.fullmatch(r"\d+\.\d+\.\d+", str(document["version"])):
        raise ValueError("适配包标识或版本不正确。")
    if "sdkVersion" in document and (not isinstance(document["sdkVersion"],str) or not re.fullmatch(r"\d+\.\d+\.\d+",document["sdkVersion"])): raise ValueError("SDK 版本不正确。")
    label(document["name"])
    required = document.get("requires", [])
    if not isinstance(required, list) or any(not isinstance(x, str) or x not in FEATURES for x in required):
        raise ValueError("宿主不支持适配包所需的控件能力。")
    localization=document.get("localization")
    keys=set()
    if localization is not None:
        if "labels.localized" not in required: raise ValueError("请声明 labels.localized 能力。")
        fields(localization,{"defaultLocale","resources"},{"defaultLocale","resources"})
        resources=localization["resources"]
        if not isinstance(resources,dict) or not 1<=len(resources)<=12: raise ValueError("语言资源数量不正确。")
        for locale, entries in resources.items():
            if not re.fullmatch(r"[a-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,2}",locale) or not isinstance(entries,dict) or len(entries)>300: raise ValueError("语言资源格式不正确。")
            for key,text in entries.items():
                if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_.-]{0,79}",key): raise ValueError("语言资源键不正确。")
                label(text)
        if not isinstance(localization["defaultLocale"],str) or localization["defaultLocale"] not in resources: raise ValueError("缺少默认语言资源。")
        keys=set(resources[localization["defaultLocale"]])
        if any(set(entries)-keys for entries in resources.values()): raise ValueError("翻译引用了未声明的资源键。")
    if "author" in document: label(document["author"])
    models = document["models"]
    if not isinstance(models, list) or not 1 <= len(models) <= 30 or len(set(models)) != len(models) or any(not isinstance(m, str) or not re.fullmatch(r"[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+){2,4}", m) for m in models):
        raise ValueError("请指定精确设备型号。")
    if document["category"] not in CATEGORIES:
        raise ValueError("不支持此设备类别。")
    if "specType" in document and (not isinstance(document["specType"], str) or not document["specType"].startswith("urn:miot-spec-v2:device:")):
        raise ValueError("Spec 类型不正确。")
    bindings = document["bindings"]
    if not isinstance(bindings, list) or len(bindings) > 40 or not bindings and not document.get("actions"):
        raise ValueError("绑定数量不正确。")
    seen = set()
    for b in bindings:
        fields(b, {"id", "service", "property", "type", "unit", "access", "siid", "capability", "serviceType", "propertyType"}, {"id", "service", "property", "type", "access"})
        if not isinstance(b["id"], str) or not re.fullmatch(r"[a-zA-Z][a-zA-Z0-9_-]{0,39}", b["id"]) or b["id"] in seen:
            raise ValueError("绑定标识重复或不正确。")
        seen.add(b["id"])
        for key in ("service", "property"):
            if not isinstance(b[key], str) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", b[key]):
                raise ValueError("绑定必须使用 MIoT 语义名称。")
            urn = b.get(key + "Type")
            if urn is not None and (not isinstance(urn, str) or len(urn) > 240 or not re.fullmatch(r"urn:[a-z][a-z0-9-]*:" + key + r":[a-z][a-z0-9-]*:[A-Za-z0-9:._-]+", urn) or semantic(urn) != b[key]):
                raise ValueError("自定义属性必须使用实际完整 URN。")
        if b["type"] not in TYPES or b["access"] not in ("read", "readWrite") or not isinstance(b.get("unit", ""), str):
            raise ValueError("绑定类型或权限不正确。")
        if "siid" in b and (type(b["siid"]) is not int or not 1 <= b["siid"] <= 65535):
            raise ValueError("服务实例编号不正确。")
        cap = b.get("capability")
        if cap:
            rule = CAPABILITIES.get(cap)
            if not rule or b["service"] not in rule[0] or b["property"] != rule[1] or b.get("unit", "") != rule[2]:
                raise ValueError("首页能力与绑定不匹配。")
            if cap == "power" and b["type"] != "bool" or cap != "power" and b["type"] in ("bool", "string"):
                raise ValueError("首页能力类型不正确。")
    fields(document["views"], {"card", "drawer", "full", "cardExpanded"}, {"card", "drawer", "full"})
    if document.get("actions") and "actions.v1" not in required: raise ValueError("请声明 actions.v1 能力。")
    if "cardExpanded" in document["views"] and "layout.cardExpanded" not in required: raise ValueError("请声明 layout.cardExpanded 能力。")
    declarations = document.get("actions", [])
    if not isinstance(declarations,list) or len(declarations)>20: raise ValueError("动作声明数量不正确。")
    action_ids = set()
    for declaration in declarations:
        fields(declaration, {"id","service","action","siid","serviceType","actionType"},{"id","service","action"})
        ident = declaration["id"]
        if not isinstance(ident,str) or not re.fullmatch(r"[a-zA-Z][a-zA-Z0-9_-]{0,39}",ident) or ident in action_ids: raise ValueError("动作标识重复或不正确。")
        action_ids.add(ident)
        for key in ("service","action"):
            name = declaration[key]
            if not isinstance(name,str) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}",name): raise ValueError("动作必须使用实际语义名称。")
            urn = declaration.get(key+"Type")
            if urn is not None and (not isinstance(urn,str) or len(urn)>240 or not re.fullmatch(r"urn:[a-z][a-z0-9-]*:"+key+r":[a-z][a-z0-9-]*:[A-Za-z0-9:._-]+",urn) or semantic(urn)!=name): raise ValueError("动作完整 URN 不正确。")
        if "siid" in declaration and (type(declaration["siid"]) is not int or not 1<=declaration["siid"]<=65535): raise ValueError("动作服务编号不正确。")
    fields(document["views"], {"card", "drawer", "full", "cardExpanded"}, {"card", "drawer", "full"})
    widget_count=[0]
    def widgets(items, card=False, depth=0, expanded=False):
        if not isinstance(items, list) or len(items) > (2 if card or expanded else 12) or depth > 2:
            raise ValueError("布局过大。")
        for w in items:
            widget_count[0]+=1
            if widget_count[0]>80: raise ValueError("布局控件过多。")
            fields(w, {"kind", "label", "binding", "children", "when", "presentation", "activeBinding", "options", "states", "maxLength", "action", "arguments", "enabledWhen", "labelKey"}, {"kind", "label"})
            label(w["label"])
            if "labelKey" in w and (not isinstance(w["labelKey"],str) or w["labelKey"] not in keys): raise ValueError("控件资源键不存在。")
            if w["kind"] not in KINDS or card and w["kind"] not in ("metric", "status", "toggle"):
                raise ValueError("不支持此布局控件。")
            if expanded and w["kind"] not in ("metric","status","toggle","slider","brightness","colorTemperature","choice","select","level","duration","countdown","action"):
                raise ValueError("展开卡片不支持此控件。")
            if w["kind"] == "action":
                if w.get("action") not in action_ids or "binding" in w or "children" in w: raise ValueError("动作控件引用不正确。")
                if "arguments" in w and (not isinstance(w["arguments"],list) or len(w["arguments"])>16 or any(isinstance(v,(list,dict)) or v is None for v in w["arguments"])): raise ValueError("预设动作参数不正确。")
            elif "action" in w or "arguments" in w: raise ValueError("此控件不能引用动作。")
            if "presentation" in w and (w["kind"] not in ("choice", "select", "level") or w["presentation"] not in ("auto", "segmented", "dropdown")):
                raise ValueError("选择控件显示方式不正确。")
            if "activeBinding" in w and (w["kind"] != "countdown" or w["activeBinding"] not in seen):
                raise ValueError("倒计时启用状态绑定不正确。")
            if "maxLength" in w and (w["kind"] != "textInput" or type(w["maxLength"]) is not int or not 1 <= w["maxLength"] <= 256):
                raise ValueError("文本输入长度不正确。")
            for key, kinds in (("options", ("choice", "select", "status")), ("states", ("indicator",))):
                if key not in w: continue
                if w["kind"] not in kinds or not isinstance(w[key], list) or not 1 <= len(w[key]) <= 50:
                    raise ValueError("标签映射不正确。")
                seen_values = []
                for option in w[key]:
                    required = {"value", "label", "tone"} if key == "states" else {"value", "label"}
                    fields(option, required | {"labelKey"}, required); label(option["label"])
                    if "labelKey" in option and (not isinstance(option["labelKey"],str) or option["labelKey"] not in keys): raise ValueError("选项资源键不存在。")
                    value = option["value"]
                    if not isinstance(value, (str, bool, int, float)) or isinstance(value, float) and not math.isfinite(value) or any(type(value) is type(v) and value == v for v in seen_values):
                        raise ValueError("选项值重复或不正确。")
                    seen_values.append(value)
                    if key == "states" and option["tone"] not in ("neutral", "success", "warning", "critical"):
                        raise ValueError("状态颜色不正确。")
            if w["kind"] in ("group", "readings"):
                if "binding" in w: raise ValueError("分组不能绑定属性。")
                if w["kind"] == "readings" and (not isinstance(w.get("children"), list) or not 1 <= len(w["children"]) <= 6 or any(not isinstance(c, dict) or c.get("kind") not in ("metric", "status", "gauge", "text", "indicator") for c in w["children"])):
                    raise ValueError("读数组合仅支持 1 至 6 个只读控件。")
                widgets(w.get("children"), depth=depth + 1)
            elif w["kind"] != "action":
                if w.get("binding") not in seen or "children" in w:
                    raise ValueError("控件绑定不存在。")
            if "enabledWhen" in w:
                if "conditions.enabled" not in document.get("requires",[]): raise ValueError("请声明 conditions.enabled 能力。")
                condition=w["enabledWhen"]
                fields(condition,{"binding","equals"},{"binding","equals"})
                if not isinstance(condition["binding"],str) or condition["binding"] not in seen or type(condition["equals"]) not in (str,bool,int,float) or isinstance(condition["equals"],float) and not math.isfinite(condition["equals"]): raise ValueError("启用条件不正确。")
            if "when" in w:
                fields(w["when"], {"binding", "equals"}, {"binding", "equals"})
                if w["when"]["binding"] not in seen or isinstance(w["when"]["equals"], (dict, list)):
                    raise ValueError("显示条件不正确。")
    for name, items in document["views"].items(): widgets(items, name == "card", expanded=name=="cardExpanded")
    if not document["views"]["drawer"]:
        raise ValueError("抽屉布局不能为空。")
    return document


def resolve(document, device, schema, icon=""):
    if device["model"] not in document["models"] or document.get("specType", device.get("specType")) != device.get("specType"):
        raise ValueError("型号或 Spec 不兼容。")
    bound = []
    for b in document["bindings"]:
        def matches(p, key):
            actual = p.get(key + "Type", "")
            return actual == b[key + "Type"] if key + "Type" in b else actual.startswith("urn:miot-spec-v2:" + key + ":" + b[key] + ":")
        candidates = [p for p in schema["properties"] if
                      matches(p, "service") and matches(p, "property") and
                      ("siid" not in b or b["siid"] == p["siid"])]
        if len(candidates) != 1:
            raise ValueError("绑定无法唯一匹配：" + b["id"])
        p = candidates[0]
        if not p["readable"] or p["type"] != b["type"] or p.get("unit", "") != b.get("unit", "") or b["access"] == "readWrite" and not p["writable"]:
            raise ValueError("属性类型、单位或权限不兼容：" + b["id"])
        bound.append({"id": b["id"], "property": p, "allowWrite": b["access"] == "readWrite", "capability": b.get("capability", "")})
    by_id = {b["id"]: b for b in bound}
    resolved_actions = action_support.resolve(document.get("actions",[]),schema)
    actions_by_id = {a["id"]:a["action"] for a in resolved_actions}
    def check(items, expanded=False):
        for w in items:
            for name in ("when","enabledWhen"):
                if name in w: action_support.scalar(by_id[w[name]["binding"]]["property"],w[name]["equals"])
            if w["kind"] in ("group", "readings"): check(w["children"]); continue
            if w["kind"] == "action":
                action = actions_by_id[w["action"]]
                if "arguments" in w: action_support.arguments(action,w["arguments"])
                if expanded and (action["inputs"] or not action["actionType"].startswith("urn:miot-spec-v2:action:") or semantic(action["actionType"]) not in action_support.CARD_ACTIONS): raise ValueError("此动作应保留在详情页。")
                continue
            p = by_id[w["binding"]]["property"]
            if w["kind"] in ("toggle", "slider", "select", "choice", "brightness", "colorTemperature", "color", "duration", "number", "level", "textInput") and not by_id[w["binding"]]["allowWrite"]:
                raise ValueError("控制项缺少写入声明。")
            if w["kind"] in ("text", "textInput") and p["type"] != "string":
                raise ValueError("文本控件需要字符串属性。")
            if w["kind"] == "textInput" and p["values"]:
                raise ValueError("文本枚举请使用选择控件。")
            if w["kind"] == "indicator" and p["type"] != "bool" and not p["values"]:
                raise ValueError("状态指示需要布尔值或实际枚举。")
            for key in ("options", "states"):
                actual_values = [True, False] if key == "states" and p["type"] == "bool" else [v["value"] for v in p["values"]]
                if any(not any(type(item["value"]) is type(v) and item["value"] == v for v in actual_values) for item in w.get(key, [])):
                    raise ValueError("标签映射包含设备没有的实际值。")
            if w["kind"] == "toggle" and p["type"] != "bool": raise ValueError("开关需要布尔值。")
            if w["kind"] in ("select", "choice") and not p["values"]: raise ValueError("选择项缺少实际枚举。")
            if w["kind"] in ("slider", "progress", "brightness", "colorTemperature", "color", "duration", "countdown", "number", "level", "gauge") and (p["type"] in ("bool", "string") or p["values"] or len(p["range"]) < 2 or not all(type(n) in (int, float) and math.isfinite(n) for n in p["range"]) or p["range"][0] >= p["range"][1] or len(p["range"]) > 2 and p["range"][2] <= 0):
                raise ValueError("数值控件缺少有效范围。")
            kind = w["kind"]
            if kind == "level":
                limits = p["range"]
                if not p["type"].startswith(("int", "uint")) or any(n != round(n) for n in limits) or len(limits) < 3 or not 1 <= (limits[1] - limits[0]) / limits[2] <= 7 or not math.isclose((limits[1] - limits[0]) / limits[2], round((limits[1] - limits[0]) / limits[2]), abs_tol=1e-6):
                    raise ValueError("档位控件需要 2 至 8 个等距实际档位。")
            if kind in ("brightness", "colorTemperature", "color"):
                expected = {"brightness": ("brightness", "percentage"), "colorTemperature": ("color-temperature", "kelvin"), "color": ("color", "rgb")}[kind]
                if semantic(p.get("serviceType")) != "light" or (semantic(p.get("propertyType")), p.get("unit", "")) != expected:
                    raise ValueError("灯光控件与实际语义或单位不匹配。")
                if kind == "color" and (not p["type"].startswith(("int", "uint")) or p["range"][0] < 0 or p["range"][1] > 16777215):
                    raise ValueError("颜色控件仅支持 RGB24 整数。")
            if kind in ("duration", "countdown") and (p.get("unit") not in ("seconds", "minutes", "hours") or p["range"][0] < 0):
                raise ValueError("时长控件需要有效的秒、分钟或小时单位。")
            if kind == "countdown" and "countdown" not in semantic(p.get("propertyType")) and semantic(p.get("propertyType")) not in ("left-time", "remaining-time"):
                raise ValueError("倒计时必须绑定设备剩余时间。")
            if w.get("activeBinding") and by_id[w["activeBinding"]]["property"]["type"] != "bool":
                raise ValueError("倒计时启用状态需要布尔值。")
    for name, items in document["views"].items(): check(items,name=="cardExpanded")
    return {"id": document["id"], "name": document["name"], "version": document["version"], "category": document["category"],
            "bindings": bound, "card": document["views"]["card"], "drawer": document["views"]["drawer"], "full": document["views"]["full"], "iconFile": icon,
            "cardExpanded":document["views"].get("cardExpanded",[]), "cardExpandedDeclared":"cardExpanded" in document["views"], "actions":resolved_actions,"localization":document.get("localization")}


def safe_path(name):
    parts = PurePosixPath(name).parts
    if not parts or len(parts) > 4 or any(not re.fullmatch(r"[A-Za-z0-9_.-]+", p) or p in (".", "..") or p.endswith((".", " ")) or p.split(".")[0].upper() in {"CON", "PRN", "AUX", "NUL", *("COM" + str(i) for i in range(1, 10)), *("LPT" + str(i) for i in range(1, 10))} for p in parts) or name.startswith("/") or "\\" in name:
        raise ValueError("包内路径不正确。")
    return Path(*parts)


def inspect(path):
    path = Path(path)
    if path.stat().st_size > 20 * 1024 * 1024: raise ValueError("适配包过大。")
    with path.open("rb") as stream: snapshot=stream.read(20*1024*1024+1)
    if len(snapshot)>20*1024*1024: raise ValueError("适配包过大。")
    package_hash=hashlib.sha256(snapshot).hexdigest()
    files = {}
    with zipfile.ZipFile(io.BytesIO(snapshot)) as archive:
        if len(archive.infolist()) > 100 or sum(i.file_size for i in archive.infolist()) > 20 * 1024 * 1024:
            raise ValueError("适配包展开后过大。")
        names = set()
        for i in archive.infolist():
            if i.is_dir(): safe_path(i.filename.rstrip("/")); continue
            relative = safe_path(i.filename)
            if str(relative).lower() in names or stat.S_ISLNK(i.external_attr >> 16): raise ValueError("包内包含重复路径或链接。")
            names.add(str(relative).lower())
            if i.filename not in ("adapter.json", "preview.json", "README.md", "LICENSE") and not (i.filename.startswith("assets/") and relative.suffix.lower() == ".png"):
                raise ValueError("包内包含不支持的文件。")
            data = archive.read(i)
            if relative.suffix.lower() == ".png":
                if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n": raise ValueError("图片格式不正确。")
                width, height = struct.unpack(">II", data[16:24])
                if not 0 < width <= 1024 or not 0 < height <= 1024: raise ValueError("图片尺寸过大。")
            files[i.filename] = data
    if not {"adapter.json", "preview.json", "LICENSE"} <= files.keys(): raise ValueError("缺少定义、预览资料或许可。")
    document = validate(decode(files["adapter.json"]))
    if document.get("icon") and (not document["icon"].startswith("assets/") or document["icon"] not in files): raise ValueError("图标路径不正确。")
    preview = decode(files["preview.json"])
    fields(preview, {"model", "specType", "schema"}, {"model", "specType", "schema"})
    fields(preview["schema"], {"properties","actions"}, {"properties"})
    if not isinstance(preview["schema"]["properties"], list) or len(preview["schema"]["properties"]) > 500: raise ValueError("预览 Spec 过大。")
    if not isinstance(preview["schema"].get("actions",[]),list) or len(preview["schema"].get("actions",[]))>500 or any(not isinstance(a,dict) for a in preview["schema"].get("actions",[])): raise ValueError("预览动作规格不正确。")
    definition = resolve(document, preview, preview["schema"])
    return document, preview, definition, files, package_hash


class AdapterStore:
    def __init__(self, root):
        self.root = Path(root)

    def package(self, ident, previous=False):
        if not ID.fullmatch(str(ident)): raise ValueError("适配包标识不正确。")
        return self.root / ident / ("previous" if previous else "current")

    def state(self):
        try: return json.loads((self.root / "state.json").read_text(encoding="utf-8"))
        except (OSError, ValueError): return {}

    def catalog(self):
        result = []
        if not self.root.exists(): return result
        for folder in sorted(self.root.iterdir()):
            if not folder.is_dir() or not ID.fullmatch(folder.name): continue
            try:
                doc = validate(decode((folder / "current/adapter.json").read_bytes()))
                result.append({"id": doc["id"], "name": doc["name"], "version": doc["version"], "models": doc["models"],
                               "enabled": self.state().get(doc["id"], True), "canRollback": (folder / "previous/adapter.json").is_file(),
                               "formatVersion":doc["formatVersion"],"sdkVersion":doc.get("sdkVersion", ""),"author":doc.get("author", ""),
                               "readme":self.readme(folder / "current/README.md"),
                               "iconFile":str(folder / "current" / safe_path(doc["icon"])) if doc.get("icon") else ""})
            except (OSError, ValueError, KeyError, TypeError): continue
        return result

    @staticmethod
    def readme(path):
        try:
            with path.open("rb") as stream: data=stream.read(65537)
            text=data[:65536].decode("utf-8-sig",errors="replace")
            return text+("\n…" if len(data)>65536 else "")
        except OSError: return ""

    def install(self, path, expected_hash=None):
        doc, _, _, files, package_hash = inspect(path)
        if expected_hash is not None and package_hash!=expected_hash: raise ValueError("适配包已变化，请重新预览。")
        self.root.mkdir(parents=True, exist_ok=True)
        target, old = self.package(doc["id"]), self.package(doc["id"], True)
        target.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix=".stage-", dir=self.root) as temp:
            stage = Path(temp) / "current"; stage.mkdir()
            for name, data in files.items():
                destination = stage / safe_path(name); destination.parent.mkdir(parents=True, exist_ok=True); destination.write_bytes(data)
            if old.exists(): shutil.rmtree(old)
            had_current = target.exists()
            if had_current: os.replace(target, old)
            try: os.replace(stage, target)
            except Exception:
                if had_current: os.replace(old, target)
                raise
        return doc

    def enable(self, ident, enabled):
        if type(enabled) is not bool or not self.package(ident).is_dir(): raise ValueError("适配包不存在。")
        state = self.state(); state[ident] = enabled; atomic(self.root / "state.json", state)

    def remove(self, ident):
        folder = self.package(ident).parent
        if folder.is_dir(): shutil.rmtree(folder)
        state = self.state(); state.pop(ident, None); atomic(self.root / "state.json", state)

    def rollback(self, ident):
        current, old = self.package(ident), self.package(ident, True)
        if not old.is_dir(): raise ValueError("没有可回退的版本。")
        temp = current.parent / "swap"
        os.replace(current, temp)
        try: os.replace(old, current); os.replace(temp, old)
        except Exception:
            if not current.exists(): os.replace(temp, current)
            raise

    def chosen(self, device, choices):
        ident = choices.get("devices", {}).get(device["id"], choices.get("models", {}).get(device["model"], ""))
        return ident if ident and ident != "$builtin" and self.state().get(ident, True) and self.package(ident).is_dir() else ""

    def definition(self, device, choices, schema):
        ident = self.chosen(device, choices)
        if not ident: return None
        folder = self.package(ident)
        document = validate(decode((folder / "adapter.json").read_bytes()))
        icon = str(folder / safe_path(document["icon"])) if document.get("icon") else ""
        return resolve(document, device, schema, icon)


def condition_guard(definition, readings, binding_ids=(), action_id=None):
    values = {(v.get("siid"),v.get("piid")):v.get("value") for v in readings if v.get("code")==0}
    bindings = {b["id"]:b["property"] for b in definition["bindings"]}
    def matches(condition):
        prop=bindings[condition["binding"]]
        actual=values.get((prop["siid"],prop["piid"]))
        expected=condition["equals"]
        return actual is not None and (type(actual) is type(expected) or type(actual) in (int,float) and type(expected) in (int,float)) and actual==expected
    def paths(widgets, conditions=()):
        for widget in widgets:
            rules=conditions+tuple(widget[key] for key in ("enabledWhen","when") if key in widget)
            if widget["kind"] in ("group","readings"): yield from paths(widget["children"],rules)
            elif (action_id is not None and widget.get("action")==action_id) or widget.get("binding") in binding_ids and widget["kind"] in ("toggle","slider","select","choice","brightness","colorTemperature","color","duration","number","level","textInput"):
                yield all(matches(rule) for rule in rules)
    options=[allowed for view in ("card","drawer","full","cardExpanded") for allowed in paths(definition.get(view,[]))]
    if options and not any(options): raise ValueError("当前设备状态不允许此操作。")


def write_guard(device, definition, properties, prop, value, readings, wireless_models, known_models=()):
    if not any(b["allowWrite"] and b["property"]["siid"] == prop["siid"] and b["property"]["piid"] == prop["piid"] for b in definition["bindings"]):
        raise ValueError("适配包未声明此控制项。")
    if isinstance(value, str):
        binding_ids = {b["id"] for b in definition["bindings"] if (b["property"]["siid"], b["property"]["piid"]) == (prop["siid"], prop["piid"])}
        def limits(widgets):
            for widget in widgets:
                if widget["kind"] in ("group", "readings"): yield from limits(widget["children"])
                elif widget["kind"] == "textInput" and widget["binding"] in binding_ids: yield widget.get("maxLength", 128)
        maximum = min([limit for view in ("drawer", "full") for limit in limits(definition[view])], default=None)
        if maximum is not None and (len(value.encode("utf-16-le")) // 2 > maximum or any(ord(c) < 32 or 127 <= ord(c) <= 159 for c in value)):
            raise ValueError("文本超出允许长度或包含控制字符。")
    values = {(v.get("siid"), v.get("piid")): v.get("value") for v in readings if v.get("code") == 0}
    binding_ids={b["id"] for b in definition["bindings"] if (b["property"]["siid"],b["property"]["piid"])==(prop["siid"],prop["piid"])}
    condition_guard(definition,readings,binding_ids)
    if (prop["siid"], prop["piid"]) not in values: raise ValueError("无法确认当前状态。")
    guards = [p for p in properties if p["siid"] == prop["siid"]]
    modes = [p for p in guards if semantic(p.get("propertyType")) == "mode" and semantic(p.get("serviceType")) == "switch"]
    if modes:
        if device["model"] not in known_models and device["model"] not in wireless_models or device["model"] in wireless_models and any(type(values.get((p["siid"], p["piid"]))) not in (int, float) or values[(p["siid"], p["piid"])] != 0 for p in modes):
            raise ValueError("当前模式不允许此操作。")
    if value is True:
        for p in guards:
            if semantic(p.get("propertyType")) == "fault" and (type(values.get((p["siid"], p["piid"]))) not in (int,float) or values.get((p["siid"], p["piid"])) != 0):
                raise ValueError("设备异常状态尚未解除。")
