"""Read-only source probe. Never print credentials or device identifiers."""
import json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"backend"))
import worker
worker.require_auth()
rows=[]
for home in worker.api.get_homes_list():
 for raw in worker.api.get_devices_list(home["id"]):
  row={"model":raw.get("model"),"subclassId":str(raw.get("subclass_id", "")),"split": ".s" in str(raw.get("did","")),"images":{}}
  for key in ("icon","icon_real","icon_on","icon_off","image","image_url","product_id","pid"):
   val=raw.get(key)
   if val is not None: row["images"][key]=val
  rows.append(row)
print(json.dumps(rows,ensure_ascii=False))
