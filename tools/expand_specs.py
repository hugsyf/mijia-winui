"""Fetch public specs for cached model URNs. No account or device IDs are sent."""
import concurrent.futures,json
from pathlib import Path
import requests
ROOT=Path(__file__).resolve().parents[1]
file=ROOT/"tools/fixtures/device-card-specs.json"
fixtures=json.loads(file.read_text(encoding="utf-8"))
models={model: spec["specType"] for model,spec in fixtures.items()}

def fetch(item):
 model,urn=item
 if model in fixtures and fixtures[model].get("specType")==urn:return model,fixtures[model]
 r=requests.get("https://miot-spec.org/miot-spec-v2/instance",params={"type":urn},timeout=25);r.raise_for_status();raw=r.json();props=[]
 if raw.get("type")!=urn:raise ValueError("Spec mismatch")
 for service in raw.get("services",[]):
  if ":device-information:" in service.get("type", ""):continue
  for p in service.get("properties",[]):
   props.append({"siid":service["iid"],"piid":p["iid"],"service":service.get("description", ""),"serviceType":service.get("type", ""),"propertyType":p.get("type", ""),"name":p.get("description", ""),"type":p.get("format", "unknown"),"unit":p.get("unit", ""),"readable":"read" in p.get("access",[]),"writable":"write" in p.get("access",[]),"range":p.get("value-range") or [],"values":p.get("value-list") or []})
 return model,{"specType":urn,"properties":props}
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
 for model,spec in pool.map(fetch,models.items()):fixtures[model]=spec
file.write_text(json.dumps(fixtures,ensure_ascii=False,indent=2),encoding="utf-8")
for model in sorted(models):
 spec=fixtures[model]; rows=[]
 for p in spec["properties"]:
  if p["readable"]:rows.append(f"{p['siid']}.{p['piid']} {p['serviceType'].split(':')[3]}.{p['propertyType'].split(':')[3]} {p['type']} {'W' if p['writable'] else 'R'} {p['unit']} "+str(p['values']))
 print(model+"\n  "+"\n  ".join(rows))
