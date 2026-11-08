"""Bundle unmodified license texts from the exact local package versions."""
from email.parser import Parser
import hashlib
import json
from pathlib import Path
import sys
from xml.etree import ElementTree as ET
import requests

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "src/MiHome.WinUI/Assets/Licenses"
DEST.mkdir(parents=True, exist_ok=True)
records = []
previous = json.loads((DEST/"index.json").read_text(encoding="utf-8")) if (DEST/"index.json").exists() else []

def add(name, version, license_name, source, texts, group):
    if not texts or not all(text.strip() for _, text in texts):
        raise ValueError("Missing license text: " + name)
    # Avoid dots/hyphens that the WinUI resource compiler treats as qualifiers.
    filename = "license_" + hashlib.sha256(name.encode("utf-8")).hexdigest()[:16] + ".txt"
    content = "\n\n".join(title + "\n" + "="*len(title) + "\n\n" + text for title, text in texts)
    (DEST / filename).write_text(content, encoding="utf-8")
    records.append(dict(name=name, version=version, license=license_name, source=source, file=filename, group=group))

version = ET.parse(ROOT/"src/MiHome.WinUI/MiHome.WinUI.csproj").findtext("PropertyGroup/Version")
add("米家 WinUI", version, "GPL-3.0-or-later", "", [("LICENSE", (ROOT/"LICENSE").read_text(encoding="utf-8"))], "应用")
add("CPython", ".".join(map(str, sys.version_info[:3])), "PSF-2.0 及附带声明", "https://www.python.org/",
    [("CPython LICENSE", (ROOT/"artifacts/python/LICENSE.txt").read_text(encoding="utf-8"))], "Python")
for dist in sorted((ROOT/"artifacts/python/Lib/site-packages").glob("*.dist-info")):
    metadata = Parser().parsestr((dist/"METADATA").read_text(encoding="utf-8"))
    name = metadata["Name"]
    license_name = metadata["License-Expression"] or metadata["License"] or "见许可全文"
    if len(license_name) > 100: license_name = "见许可全文"
    homepage = metadata["Home-page"] or ""
    for project in metadata.get_all("Project-URL", []):
        label, _, url = project.partition(",")
        if label.strip().lower() in ("source", "source code", "repository", "homepage"):
            homepage = url.strip(); break
    texts = [(str(p.relative_to(dist)), p.read_text(encoding="utf-8-sig")) for p in sorted(dist.rglob("*"))
             if p.is_file() and any(k in p.name.lower() for k in ("license", "copying", "notice"))]
    add(name, metadata["Version"], license_name, homepage, texts, "Python")

cache = Path.home()/".nuget/packages"
assets = json.loads((ROOT/"src/MiHome.WinUI/obj/project.assets.json").read_text(encoding="utf-8"))
packages = [key.split("/") for key, val in assets["libraries"].items() if val["type"] == "package"]
runtime = sorted((cache/"microsoft.netcore.app.runtime.win-x64").iterdir(), key=lambda p: tuple(map(int,p.name.split("."))))[-1]
packages.append(["Microsoft.NETCore.App.Runtime.win-x64", runtime.name])
for name, version in packages:
    # Build tools are not redistributed as application runtime components.
    if name in ("Microsoft.Windows.SDK.BuildTools", "Microsoft.Windows.SDK.BuildTools.MSIX"):
        continue
    directory = cache/name.lower()/version
    metadata = ET.parse(next(directory.glob("*.nuspec"))).getroot().find("{*}metadata")
    license_node = metadata.find("{*}license")
    license_name = license_node.text if license_node is not None and license_node.get("type") == "expression" else "见许可全文"
    source_node = metadata.find("{*}repository")
    source = source_node.get("url", "") if source_node is not None else metadata.findtext("{*}projectUrl", "")
    paths = [p for p in directory.iterdir() if p.is_file() and any(k in p.name.lower() for k in ("license", "notice"))]
    if license_node is not None and license_node.get("type") == "file":
        license_file = directory/license_node.text
        if license_file not in paths: paths.insert(0, license_file)
    if name == "QRCoder" and not paths:
        # This exact NuGet package declares MIT but omits its full text.
        saved = ROOT / "tools/fixtures/qrcoder-1.7.0-LICENSE.txt"
        if not saved.exists():
            response = requests.get("https://raw.githubusercontent.com/Shane32/QRCoder/2dc55ee03cb9abf66ffed7d683cbcce0a141b124/LICENSE.txt", timeout=20)
            response.raise_for_status(); saved.write_text(response.text, encoding="utf-8")
        paths = [saved]
    texts = [(p.name, p.read_text(encoding="utf-8-sig")) for p in sorted(paths)]
    add(name, version, license_name, source, texts, ".NET / Windows")

add("设备图片来源", "", "官方 CDN · 按需下载", "",
    [("素材来源", "设备产品图片、用途图片来自小米公开 CDN 和官方用途目录。\n"
      "图片版权归各自权利人。\n")], "素材来源")
(DEST/"index.json").write_text(json.dumps(records, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
current_files = {r["file"] for r in records}
for old in previous:
    filename = old["file"]
    if Path(filename).name == filename and filename not in current_files:
        (DEST/filename).unlink(missing_ok=True)
print(f"Bundled {len(records)} component/license entries")
