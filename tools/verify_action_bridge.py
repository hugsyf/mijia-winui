"""Isolated action bridge checks. All device methods are fake."""
import json
import logging
import os
from pathlib import Path
import sys
import tempfile
from unittest.mock import patch
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"backend"))
from adapters import resolve,validate

with tempfile.TemporaryDirectory() as directory:
    os.environ["LOCALAPPDATA"]=directory
    import worker
    source=ROOT/"examples/adapters/appliance-actions"
    doc=json.loads((source/"adapter.json").read_text(encoding="utf-8"))
    preview=json.loads((source/"preview.json").read_text(encoding="utf-8"))
    definition=resolve(validate(doc),preview,preview["schema"])
    class Fake:
        auth_data={"userId":"test-actions"}
        fault=0
        calls=[]
        def get_devices_prop(self,keys):
            return [dict(key,code=0,value=self.fault if next(p for p in preview["schema"]["properties"] if p["siid"]==key["siid"] and p["piid"]==key["piid"])["propertyType"].split(":")[3]=="fault" else 0) for key in keys]
        def run_action(self,payload):self.calls.append(payload);return {"code":1,"out":[]}
    api=Fake();worker.api=api
    worker.devices={"device":dict(id="device",model=preview["model"],specType=preview["specType"],online=True)}
    original_schema=worker.schema
    def request(action="start",**overrides):
        return dict(deviceId="device",adapterId=doc["id"],actionId=action,arguments=[],requestId="1"*32,confirmed=True,**overrides)
    checks=0
    def rejected(args):
        global checks
        before=len(api.calls)
        try:worker.dispatch("RunAction",args)
        except ValueError:pass
        else:raise AssertionError("Invalid action request was accepted")
        assert len(api.calls)==before
        checks+=1
    with patch.object(worker,"schema",return_value=preview["schema"]),patch.object(worker.adapter_store,"definition",return_value=definition):
        api.fault=1;rejected(request())
        api.fault=0
        for change in ({"confirmed":False},{"adapterId":"stale.package"},{"actionId":"undeclared"},{"arguments":[1]},{"deviceId":"unknown"}):
            params=request();params.update(change);rejected(params)
        worker.devices["device"]["online"]=False;rejected(request());worker.devices["device"]["online"]=True
        result=worker.dispatch("RunAction",request());assert result["status"]=="accepted" and len(api.calls)==1;checks+=1
        worker.dispatch("RunAction",request());assert len(api.calls)==1;checks+=1
        api.fault=1;paused=request("pause");paused["requestId"]="2"*32
        worker.dispatch("RunAction",paused);assert len(api.calls)==2;checks+=1
    with patch.object(worker,"schema",return_value=preview["schema"]),patch.object(worker.adapter_store,"definition",return_value=None):rejected(request())
    # A virtual device cannot invoke another service's action.
    worker.devices["device.s6"]=dict(worker.devices["device"],id="device.s6")
    cache=worker.cache_dir()/("spec-v3-"+worker.hashlib.sha256(preview["specType"].encode()).hexdigest()+".json")
    cache.write_text(json.dumps(preview["schema"]),encoding="utf-8")
    with patch.object(worker.spec_language,"labels",return_value={}),patch.object(worker.requests,"get",side_effect=AssertionError("No network in isolated checks")),patch.object(worker.adapter_store,"definition",side_effect=lambda device,choices,schema:resolve(doc,device,schema)):
        virtual=original_schema("device.s6");assert all(a["siid"]==6 for a in virtual["actions"]);checks+=1
        args=request();args["deviceId"]="device.s6";rejected(args)
    print(f"Action bridge: {checks} isolated checks passed; fake calls only.")
    logging.shutdown()
