"""Resolve built-in speaker commands against the device's MIOT spec."""

from device_data import split_device

TRANSPORT = {"previous", "play", "pause", "next"}
TEXT_INPUT = ("text-content", "string")
QUIET_INPUT = ("silent-execution", "bool")


def is_speaker(device):
    return (":device:speaker:" in device.get("specType", "") or
            ".wifispeaker." in device.get("model", ""))


def resolve_speaker_action(schema, command, text=None, quiet=False):
    if command in TRANSPORT:
        service, signature, values = "play-control", [], []
        if text is not None or quiet is not False:
            raise ValueError("播放操作不接受文字参数。")
    elif command in ("play-text", "execute-text-directive"):
        service = "intelligent-speaker"
        if not isinstance(text, str) or not text.strip() or len(text.encode("utf-16-le")) > 512:
            raise ValueError("请输入 1 到 256 个字符。")
        if type(quiet) is not bool or (quiet and command == "play-text"):
            raise ValueError("静默设置不适用于此操作。")
        directive = command == "execute-text-directive"
        signature = [TEXT_INPUT, QUIET_INPUT] if directive else [TEXT_INPUT]
        values = [text.strip(), quiet] if directive else [text.strip()]
    else:
        raise ValueError("不支持的音箱操作。")

    def matches(action):
        return (action["serviceType"].startswith(f"urn:miot-spec-v2:service:{service}:") and
                action["actionType"].startswith(f"urn:miot-spec-v2:action:{command}:") and
                len(action["inputs"]) == len(signature) and
                all(prop["type"] == kind and prop["propertyType"].startswith(f"urn:miot-spec-v2:property:{name}:")
                    for prop, (name, kind) in zip(action["inputs"], signature)))

    found = [action for action in schema.get("actions", []) if matches(action)]
    if len(found) != 1:
        raise ValueError("此音箱不支持该操作。")
    return found[0], values


def run_speaker_action(api, executor, device, schema, params):
    target, _ = split_device(device["id"])
    action, values = resolve_speaker_action(schema, params.get("command"), params.get("text"), params.get("quiet", False))
    scope = (str(api.auth_data.get("userId", "")), device["id"], "$speaker")
    return executor.execute(api, scope, target, action, values, params.get("requestId"))
