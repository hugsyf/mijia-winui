"""Import the official read-only subclass catalog saved during source verification.

Keep signed CDN query parameters out of the application and source records.
"""
import concurrent.futures
import hashlib
import json
from pathlib import Path
from urllib.parse import urlsplit
import requests
ROOT = Path(__file__).resolve().parents[1]
source = ROOT / "artifacts/reference-apk/subclass-catalog.json"
dest = ROOT / "src/MiHome.WinUI/Assets/Devices"
# Presentation semantics only. Never used to infer device control properties.
semantics = {
 "电暖器":("heater","heater"), "饮水机":("water-dispenser","water-dispenser"), "灯泡":("light","light"), "鱼缸灯":("light","aquarium-light"),
 "筒灯":("light","downlight"), "射灯":("light","spotlight"), "灯带":("light","light-strip"),
 "吊灯":("light","pendant-light"), "吸顶灯":("light","ceiling-light"), "壁灯":("light","wall-light"),
 "床头灯":("light","bedside-light"), "氛围灯":("light","ambient-light"), "灯":("light","light"),
 "排气扇":("fan","exhaust-fan"), "凉霸":("fan","cooling-panel"), "晾衣架":("drying-rack","drying-rack"),
 "窗帘":("curtain","curtain"), "其他":("switch","switch"), "台灯":("light","desk-light"),
 "落地灯":("light","floor-light"), "电脑":("computer","computer"), "电视":("television","television"),
 "风扇":("fan","fan"), "取暖器":("heater","heater"), "热水器":("water-heater","water-heater"),
 "摄像头":("camera","camera"), "净水器":("water-purifier","water-purifier"), "冰箱":("refrigerator","refrigerator"),
 "空调":("air-conditioner","air-conditioner"), "电热毯":("electric-blanket","electric-blanket"),
 "洗衣机":("washer","washer"), "鱼缸":("aquarium","aquarium"), "微波炉":("microwave","microwave"),
 "烤箱":("oven","oven"), "驱蚊器":("mosquito-repellent","mosquito-repellent"), "电磁炉":("induction-cooktop","induction-cooktop"),
 "电饭煲":("rice-cooker","rice-cooker"), "洗碗机":("dishwasher","dishwasher"), "加湿器":("humidifier","humidifier"),
 "马桶":("toilet","toilet"), "投影仪":("projector","projector"), "吸油烟机":("range-hood","range-hood"),
 "路由器":("router","router"), "装饰灯":("light","ambient-light"), "夹子灯":("light","desk-light")
}
rows = [r for r in json.loads(source.read_text(encoding="utf-8")) if r["plugin_type"] in ("switch","socket")]
def fetch(row):
    image_field = "proxy_category_icon" if row.get("proxy_category_icon") else "light_icon"
    url = row[image_field]
    parts = urlsplit(url)
    if parts.scheme != "https" or parts.hostname != "cdn.cnbj1.fds.api.mi-img.com": raise ValueError("Unexpected image host")
    name = "purpose_" + str(row["id"]) + ".png"
    response = requests.get(url,timeout=25,allow_redirects=False)
    response.raise_for_status()
    data=response.content
    if not data.startswith(b"\x89PNG\r\n\x1a\n") or len(data)>2*1024*1024: raise ValueError("Invalid PNG")
    (dest/name).write_bytes(data)
    category,icon = semantics.get(row["chinese_name"],("appliance","appliance"))
    return str(row["id"]),dict(file=name,name=row["chinese_name"],pluginType=row["plugin_type"],rank=row["rank"],
        category=category,icon=icon,imageField=image_field,url=url.split("?")[0],sha256=hashlib.sha256(data).hexdigest(),bytes=len(data))
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    records=dict(pool.map(fetch,rows))
records=dict(sorted(records.items(),key=lambda r:int(r[0])))
catalog={"source":"/v2/device/switch_subclass", "verifiedAt":"2026-11-07", "records":records}
(dest/"purpose-index.json").write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
shared={k:{field:v[field] for field in ["name","pluginType","rank","category","icon"]} for k,v in records.items()}
(ROOT/"backend/device_purposes.json").write_text(json.dumps(shared,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print("Imported",len(records),"official purpose images")
