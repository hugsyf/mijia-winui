"""Focused offline checks for SDK conditions, localized labels, and legacy packages."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"backend"))
from adapters import validate, resolve, condition_guard

class Sdk20Checks(unittest.TestCase):
    def setUp(self):
        self.document=json.loads((ROOT/"examples/adapters/lighting/adapter.json").read_text(encoding="utf-8"))
        self.preview=json.loads((ROOT/"examples/adapters/lighting/preview.json").read_text(encoding="utf-8"))
        self.document["requires"]=list(dict.fromkeys(self.document["requires"]+['conditions.enabled','labels.localized']))
        self.document.setdefault('localization',{'defaultLocale':'zh-CN','resources':{}})
        self.document['localization']['resources'].setdefault('zh-CN',{})['brightness']='亮度'
        self.document['localization']['resources'].setdefault('en-US',{})['brightness']='Brightness'
        widget=self.document['views']['drawer'][2]
        widget['enabledWhen']={'binding':'power','equals':True};widget['labelKey']='brightness'
        self.document['views']['cardExpanded'][0]['enabledWhen']={'binding':'power','equals':True}
    def definition(self):
        return resolve(validate(self.document),self.preview,self.preview['schema'])
    def test_resolved_conditions_and_resources(self):
        result=self.definition()
        self.assertEqual(result['drawer'][2]['enabledWhen']['binding'],'power')
        self.assertEqual(result['localization']['resources']['en-US']['brightness'],'Brightness')
    def test_missing_feature_rejected(self):
        self.document['requires'].remove('conditions.enabled')
        with self.assertRaises(ValueError):validate(self.document)
    def test_invalid_condition_binding(self):
        self.document['views']['drawer'][2]['enabledWhen']['binding']='missing'
        with self.assertRaises(ValueError):validate(self.document)
    def test_actual_condition_type(self):
        self.document['views']['drawer'][2]['enabledWhen']['equals']=1
        with self.assertRaises(ValueError):self.definition()
    def test_missing_default_translation(self):
        self.document['localization']['defaultLocale']='fr-FR'
        with self.assertRaises(ValueError):validate(self.document)
    def test_missing_resource_key(self):
        self.document['views']['drawer'][2]['labelKey']='not.present'
        with self.assertRaises(ValueError):validate(self.document)
    def test_locale_path_rejected(self):
        self.document['localization']['resources']['../en']={}
        with self.assertRaises(ValueError):validate(self.document)
    def test_backend_guard_on_off_missing(self):
        definition=self.definition()
        power=next(b['property'] for b in definition['bindings'] if b['id']=='power')
        def readings(value):return [{'siid':power['siid'],'piid':power['piid'],'code':0,'value':value}]
        condition_guard(definition,readings(True),['brightness'])
        for state in (readings(False),[],readings(1)):
            with self.assertRaises(ValueError):condition_guard(definition,state,['brightness'])
    def test_nested_group_and_actions_guard(self):
        definition=self.definition()
        definition['drawer']=[{'kind':'group','enabledWhen':{'binding':'power','equals':True},'children':[{'kind':'action','action':'start'}]}]
        with self.assertRaises(ValueError):condition_guard(definition,[],action_id='start')
    def test_read_only_card_cannot_bypass_write_condition(self):
        definition=self.definition()
        definition['card']=[{'kind':'metric','binding':'brightness'}]
        with self.assertRaises(ValueError):condition_guard(definition,[],['brightness'])
    def test_status_option_translations(self):
        self.document['views']['card']=[{'kind':'status','binding':'mode','label':'模式','options':[{'value':1,'label':'颜色','labelKey':'brightness'}]}]
        self.assertEqual(self.definition()['card'][0]['options'][0]['labelKey'],'brightness')
    def test_legacy_adapter_compatible(self):
        document=json.loads((ROOT/'examples/adapters/outlet/adapter.json').read_text(encoding='utf-8'))
        self.assertEqual(validate(document)['formatVersion'],1)

if __name__=='__main__':unittest.main()
