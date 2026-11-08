"""Focused action/expanded-card checks. Fake APIs only; never operates a device."""
import copy
import json
from pathlib import Path
import sys
import unittest
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"backend"))
from action_support import ActionExecutor, ActionUnknown, arguments, normalize, resolve as resolve_actions, scalar
from adapters import validate, resolve

class FakeApi:
    def __init__(self,result=None,error=None): self.calls=[];self.result=result or {"code":0,"out":[]};self.error=error
    def run_action(self,payload):
        self.calls.append(payload)
        if self.error: raise self.error
        return self.result

class Actions(unittest.TestCase):
    def setUp(self):
        p=ROOT/"examples/adapters/action-demo"
        self.doc=json.loads((p/"adapter.json").read_text(encoding="utf-8"))
        self.preview=json.loads((p/"preview.json").read_text(encoding="utf-8"))
        self.action=self.preview["schema"]["actions"][0]
        self.noinput=self.preview["schema"]["actions"][1]

    def test_normalization_keeps_input_and_output_order(self):
        props=self.preview["schema"]["properties"]
        raw={"services":[{"iid":2,"type":self.action["serviceType"],"actions":[{"iid":1,"type":self.action["actionType"],"in":[2,1],"out":[3]}]}]}
        result=normalize(raw,props)[0]
        self.assertEqual([p["piid"] for p in result["inputs"]],[2,1])
        self.assertFalse(result["inputs"][0]["readable"])
        self.assertFalse(result["inputs"][0]["writable"])

    def test_arguments_are_typed_and_spec_constrained(self):
        arguments(self.action,[1,True]);arguments(self.action,[4,False])
        for values in ([True,1],[2,False],[-1,False],[256,True],["1",True],[1,1],[1],[]):
            with self.assertRaises(ValueError): arguments(self.action,values)
        with self.assertRaises(ValueError): scalar(self.action["inputs"][0],1.0)
        with self.assertRaises(ValueError): scalar(self.action["outputs"][0],"😀"*2049)

    def test_declarations_resolve_uniquely(self):
        duplicate=copy.deepcopy(self.preview["schema"])
        duplicate["actions"].append(dict(self.noinput,siid=3))
        declaration={"id":"pause","service":"demo","action":"pause"}
        with self.assertRaises(ValueError): resolve_actions([declaration],duplicate)
        self.assertEqual(resolve_actions([dict(declaration,siid=2)],duplicate)[0]["action"]["siid"],2)
        bad=copy.deepcopy(self.preview["schema"]);bad["actions"][0]["inputs"][0]["type"]="array"
        with self.assertRaises(ValueError): resolve(self.doc,self.preview,bad)

    def test_action_only_adapter_and_fixed_arguments(self):
        resolved=resolve(validate(self.doc),self.preview,self.preview["schema"])
        self.assertEqual(len(resolved["bindings"]),0)
        fixed=copy.deepcopy(self.doc);fixed["views"]["drawer"][0]["arguments"]=[4,False]
        resolve(validate(fixed),self.preview,self.preview["schema"])
        fixed["views"]["drawer"][0]["arguments"]=[2,False]
        with self.assertRaises(ValueError): resolve(validate(fixed),self.preview,self.preview["schema"])

    def test_expanded_card_restrictions(self):
        for name in ("reset-filter-life","start-cook","start-self-clean"):
            data=copy.deepcopy(self.preview)
            data["schema"]["actions"][1]["actionType"]=f"urn:miot-spec-v2:action:{name}:00002801:example-actions:1"
            doc=copy.deepcopy(self.doc);doc["actions"][1]["action"]=name
            with self.assertRaises(ValueError): resolve(validate(doc),data,data["schema"])
        bad=copy.deepcopy(self.doc);bad["views"]["cardExpanded"]=[dict(bad["views"]["drawer"][0])]
        with self.assertRaises(ValueError): resolve(validate(bad),self.preview,self.preview["schema"])
        bad=copy.deepcopy(self.doc);bad["views"]["cardExpanded"]*=3
        with self.assertRaises(ValueError): validate(bad)

    def test_executor_dispatches_once_and_uses_in(self):
        api=FakeApi();executor=ActionExecutor();nonce="a"*32
        one=executor.execute(api,"account","target",self.noinput,[],nonce)
        two=executor.execute(api,"account","target",self.noinput,[],nonce)
        self.assertEqual(one,two);self.assertEqual(len(api.calls),1)
        self.assertEqual(api.calls[0],{"did":"target","siid":2,"aiid":2,"in":[]})
        self.assertEqual(one["status"],"accepted");self.assertNotIn("已完成",one["message"])
        with self.assertRaises(ValueError):executor.execute(api,"account","another",self.noinput,[],nonce)
        executor.execute(api,"other-account","target",self.noinput,[],nonce)
        self.assertEqual(len(api.calls),2)

    def test_timeout_and_missing_response_never_retry(self):
        for api in (FakeApi(error=TimeoutError()),FakeApi(result={"message":"missing code"})):
            executor=ActionExecutor()
            for _ in range(2):
                with self.assertRaises(ActionUnknown):executor.execute(api,"account","target",self.noinput,[],"b"*32)
            self.assertEqual(len(api.calls),1)

    def test_rejection_and_gateway_acceptance_are_distinct(self):
        for code,status in ((-1,"rejected"),(1,"accepted")):
            api=FakeApi({"code":code});result=ActionExecutor().execute(api,"account","target",self.noinput,[],"c"*32)
            self.assertEqual(result["status"],status)

    def test_outputs_are_ordered_and_validated(self):
        for output in (["ok"],[{"piid":3,"value":"ok"}]):
            api=FakeApi({"code":0,"out":output});result=ActionExecutor().execute(api,"account","target",self.action,[1,False],"d"*32)
            self.assertEqual(result["outputs"],["ok"])
        api=FakeApi({"code":0,"out":[True]});result=ActionExecutor().execute(api,"account","target",self.action,[1,False],"e"*32)
        self.assertEqual(result["status"],"accepted");self.assertEqual(result["outputs"],[])

    def test_public_examples_have_real_declared_actions(self):
        for folder in ("humidifier","numeric","appliance-actions"):
            directory=ROOT/"examples/adapters"/folder
            doc=json.loads((directory/"adapter.json").read_text(encoding="utf-8"));preview=json.loads((directory/"preview.json").read_text(encoding="utf-8"))
            self.assertGreater(len(resolve(validate(doc),preview,preview["schema"])["actions"]),0)

if __name__=="__main__":unittest.main()
