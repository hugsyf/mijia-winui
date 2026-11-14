"""Offline authoring tool. Uses the same validator as the application."""
import argparse
import json
from pathlib import Path
import sys
import tempfile
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from adapters import inspect, safe_path, decode

SDK_VERSION = "0.22.0"


def pack(source, output):
    source, output = Path(source).resolve(), Path(output).resolve()
    if not source.is_dir() or source == output or source in output.parents:
        raise ValueError("输出文件必须位于源码文件夹之外。")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as temp:
        candidate = Path(temp) / "adapter.mihome-adapter"
        with zipfile.ZipFile(candidate, "w", zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(source.rglob("*")):
                if path.is_symlink(): raise ValueError("源码中不能包含链接。")
                if path.is_file():
                    name = path.relative_to(source).as_posix()
                    safe_path(name)
                    if name=="adapter.json":
                        document=decode(path.read_bytes()); document.setdefault("sdkVersion",SDK_VERSION)
                        archive.writestr(name,json.dumps(document,ensure_ascii=False,indent=2))
                    else: archive.write(path, name)
        doc, _, _, _, digest = inspect(candidate)
        output.write_bytes(candidate.read_bytes())
    print(f"{doc['id']} {doc['version']} -> {output}\nSHA-256 {digest}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    p = commands.add_parser("pack"); p.add_argument("source"); p.add_argument("output")
    p = commands.add_parser("validate"); p.add_argument("package")
    p = commands.add_parser("preview-from-export"); p.add_argument("export"); p.add_argument("output")
    args = parser.parse_args()
    try:
        if args.command == "pack": pack(args.source, args.output)
        elif args.command == "validate":
            doc, _, definition, _, digest = inspect(args.package)
            print(f"有效：{doc['id']} {doc['version']}，{len(definition['bindings'])} 个绑定\nSHA-256 {digest}")
        else:
            data = json.loads(Path(args.export).read_text(encoding="utf-8-sig"))
            preview = {k: data[k] for k in ("model", "specType", "schema")}
            Path(args.output).write_text(json.dumps(preview, ensure_ascii=False, indent=2), encoding="utf-8")
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        parser.exit(1, str(error) + "\n")


if __name__ == "__main__": main()
