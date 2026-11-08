"""Small offline check: syntax, Windows encryption, protocol. No device access."""
import ast
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[1]
for source in (root / "backend").glob("*.py"):
    ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
sys.path.insert(0, str(root / "backend"))
from secure_store import load, save
from device_data import normalize_device, split_device, filter_properties

# Room membership comes from the home room's dids, not from device_info.room_id.
rooms = [{"id": "r1", "name": "房间甲", "dids": ["parent"]},
         {"id": "r2", "name": "房间乙", "dids": ["parent.s3"]}]
assert normalize_device({"did": "parent", "isOnline": True}, "h", rooms)["roomId"] == "r1"
assert normalize_device({"did": "parent.s2"}, "h", rooms)["roomId"] == "r1"
assert normalize_device({"did": "parent.s3"}, "h", rooms)["roomId"] == "r2"
assert normalize_device({"did": "unknown"}, "h", rooms)["roomId"] == ""
assert split_device("parent.s3") == ("parent", 3)
assert split_device("blt.3.example") == ("blt.3.example", None)
properties = [{"siid": 2, "piid": 1}, {"siid": 3, "piid": 1}]
assert filter_properties(properties, "parent.s3") == [{"siid": 3, "piid": 1}]
assert filter_properties(properties, "parent.s8") == []
assert filter_properties(properties, "parent") == properties

with tempfile.TemporaryDirectory(prefix="mihome-verify-") as temporary:
    token = Path(temporary) / "auth.dat"
    sample = {"test": "offline-secret"}
    save(token, sample)
    assert load(token) == sample
    assert b"offline-secret" not in token.read_bytes()
    env = {**os.environ, "LOCALAPPDATA": temporary, "PYTHONIOENCODING": "utf-8"}
    process = subprocess.Popen([sys.executable, "-u", str(root / "backend" / "worker.py")],
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               text=True, encoding="utf-8", env=env)
    def call(ident, method, version=1):
        process.stdin.write(json.dumps({"version": version, "id": ident, "method": method}) + "\n")
        process.stdin.flush()
        line = process.stdout.readline()
        if not line:
            raise RuntimeError(process.stderr.read())
        return json.loads(line)
    try:
        assert call(1, "Hello")["result"]["protocol"] == 1
        assert call(2, "GetSessionStatus")["result"]["authenticated"] is False
        assert "error" in call(3, "Hello", 2)
        assert call(4, "Shutdown")["result"] == {}
        assert process.wait(timeout=5) == 0
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
print("PASS: room membership, exact split-device routing, Python syntax, DPAPI, protocol handshake/rejection, signed-out session, shutdown")
