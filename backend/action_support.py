"""MIOT action contracts and single-dispatch execution. No credentials stored here."""
import hashlib
import json
import math
import re


class ActionUnknown(Exception):
    pass


def normalize(raw, properties, labels=None):
    labels = labels or {}
    lookup = {(p["siid"], p["piid"]): p for p in properties}
    result = []
    for service in raw.get("services", []):
        siid = service["iid"]
        for action in service.get("actions", []) or []:
            if not isinstance(action,dict): continue
            inputs, outputs = action.get("in", []), action.get("out", [])
            if not isinstance(inputs,list) or not isinstance(outputs,list) or len(inputs)>16 or len(outputs)>16 or any(type(p) is not int or (siid,p) not in lookup for p in inputs+outputs):
                continue
            result.append({"siid": siid, "aiid": action["iid"], "serviceType": service["type"], "actionType": action["type"],
                           "name": action.get("description", action["type"].split(":")[3]),
                           "displayName": labels.get(f"service:{siid:03d}:action:{action['iid']:03d}", ""),
                           "inputs": [lookup[(siid, p)] for p in inputs], "outputs": [lookup[(siid, p)] for p in outputs]})
    return result


def scalar(prop, value):
    kind = prop["type"]
    if kind == "bool":
        valid = type(value) is bool
    elif kind.startswith(("int", "uint")):
        bits = int(kind.lstrip("uint"))
        valid = type(value) is int and (0 <= value < 2**bits if kind.startswith("uint") else -2**(bits-1) <= value < 2**(bits-1))
    elif kind in ("float", "double"):
        valid = type(value) in (int, float) and math.isfinite(value)
    elif kind == "string":
        valid = isinstance(value, str) and len(value.encode("utf-16-le")) // 2 <= 4096
    else:
        valid = False
    if not valid: raise ValueError("动作参数类型不正确。")
    limits = prop.get("range", [])
    if limits:
        if len(limits) < 2 or not limits[0] <= value <= limits[1]: raise ValueError("动作参数超出实际范围。")
        if len(limits) > 2 and limits[2] > 0 and not math.isclose((value-limits[0])/limits[2], round((value-limits[0])/limits[2]), abs_tol=1e-6):
            raise ValueError("动作参数不符合实际步长。")
    choices = prop.get("values", [])
    if choices and not any(type(value) is type(v["value"]) and value == v["value"] for v in choices):
        raise ValueError("动作参数不在实际枚举中。")


def arguments(action, values):
    if not isinstance(values, list) or len(values) != len(action["inputs"]): raise ValueError("动作参数数量不正确。")
    for prop, value in zip(action["inputs"], values): scalar(prop, value)


def resolve(declarations, schema):
    result = []
    for item in declarations:
        def match(action, key, name):
            actual=action.get(key, "")
            return actual == item[key] if key in item else isinstance(actual,str) and actual.startswith(f"urn:miot-spec-v2:{name}:{item[name]}:")
        found = [a for a in schema.get("actions", []) if match(a, "serviceType", "service") and match(a, "actionType", "action") and ("siid" not in item or a["siid"] == item["siid"])]
        if len(found) != 1: raise ValueError("动作无法唯一匹配：" + item["id"])
        for direction in ("inputs","outputs"):
            parameters = found[0].get(direction)
            if not isinstance(parameters,list) or len(parameters)>16: raise ValueError("动作参数规格不正确。")
            for p in parameters:
                if not isinstance(p,dict) or p.get("type") not in {"bool","string","float","double","uint8","uint16","uint32","uint64","int8","int16","int32","int64"} or p.get("siid")!=found[0]["siid"] or type(p.get("piid")) is not int or not isinstance(p.get("values"),list) or not isinstance(p.get("range"),list):
                    raise ValueError("动作参数类型暂不支持。")
                limits=p["range"]
                if limits and (len(limits) not in (2,3) or any(type(v) not in (int,float) or not math.isfinite(v) for v in limits) or limits[0]>limits[1] or len(limits)==3 and limits[2]<=0): raise ValueError("动作参数范围不正确。")
        result.append({"id": item["id"], "action": found[0]})
    return result


CARD_ACTIONS = {"play", "pause", "stop", "previous", "next", "resume-cook", "stop-alarm"}


class ActionExecutor:
    def __init__(self):
        self.receipts = {}

    def execute(self, api, scope, target, action, values, request_id):
        if not isinstance(request_id, str) or not re.fullmatch(r"[a-fA-F0-9-]{32,36}", request_id): raise ValueError("动作请求标识不正确。")
        arguments(action, values)
        fingerprint = hashlib.sha256(json.dumps([target,action["siid"],action["aiid"],values],ensure_ascii=False,sort_keys=True).encode()).hexdigest()
        key = (scope, request_id)
        if key in self.receipts:
            previous, result = self.receipts[key]
            if previous != fingerprint: raise ValueError("动作请求标识已用于其他参数。")
            if result is None: raise ActionUnknown("动作结果待确认，请检查设备状态，不要重复提交。")
            return result
        if len(self.receipts) >= 4096: raise ValueError("本次连接的动作记录已满，请重新打开应用。")
        self.receipts[key] = (fingerprint, None)
        try:
            # run_action forwards the MIOT 'in' field without alteration. One call, no retries.
            result = api.run_action({"did":target,"siid":action["siid"],"aiid":action["aiid"],"in":values})
        except Exception as exc:
            raise ActionUnknown("动作结果待确认，请检查设备状态，不要重复提交。") from exc
        if not isinstance(result, dict) or type(result.get("code")) is not int:
            raise ActionUnknown("动作响应不完整，结果待确认。")
        code = result["code"]
        if code not in (0, 1):
            response = {"status":"rejected","message":f"设备未接受操作（{code}）。","outputs":[]}
        else:
            output = result.get("out", [])
            if isinstance(output,list) and output and all(isinstance(v,dict) and "piid" in v and "value" in v for v in output):
                mapped={v["piid"]:v["value"] for v in output}
                output=[mapped[p["piid"]] for p in action["outputs"]] if len(mapped)==len(output)==len(action["outputs"]) and all(p["piid"] in mapped for p in action["outputs"]) else None
            valid_output = isinstance(output, list) and len(output) == len(action["outputs"])
            if valid_output:
                try:
                    for prop, value in zip(action["outputs"], output): scalar(prop, value)
                except (ValueError, TypeError): valid_output = False
            response = {"status":"accepted", "message":"请求已接受，请以设备状态为准。" if valid_output else "请求已接受，返回参数不完整，请以设备状态为准。", "outputs":output if valid_output else []}
        self.receipts[key] = (fingerprint, response)
        return response
