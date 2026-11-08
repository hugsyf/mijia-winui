"""Generate WinUI .resw and code resources from the shared UI catalog."""
import json
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]/"src"/"MiHome.WinUI"
def sync():
    catalog=json.loads((ROOT/"Strings"/"catalog.json").read_text(encoding="utf-8"))
    for locale in ("zh-CN","en-US"):
        values={key:item[locale] for key,item in catalog.items()}
        document=ET.Element("root")
        for name,value in values.items():
            entry=ET.SubElement(document,"data",{"name":name,"xml:space":"preserve"})
            ET.SubElement(entry,"value").text=value
        directory=ROOT/"Strings"/locale;directory.mkdir(parents=True,exist_ok=True)
        ET.indent(document)
        (directory/"Resources.resw").write_text(ET.tostring(document,encoding="unicode"),encoding="utf-8")
        (directory/"Ui.json").write_text(json.dumps(values,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(f"Generated two UI languages, {len(catalog)} resource entries each.")
if __name__=="__main__":sync()
