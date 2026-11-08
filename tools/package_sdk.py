"""Build the offline SDK archive from an explicit source file list."""
import zipfile
from pathlib import Path

from adapter_tool import SDK_VERSION


def main():
    root = Path(__file__).resolve().parents[1]
    output = root / "artifacts" / f"MiHome-Adapter-SDK-{SDK_VERSION}.zip"
    files = {
        "README.md": root / "sdk/README.md",
        **{name: root / name for name in (
            "LICENSE", "tools/adapter_tool.py", "sdk/adapter.schema.json",
            "backend/adapter-agent-guide.md", "backend/adapters.py",
            "backend/action_support.py",
        )},
    }
    for path in sorted((root / "examples/adapters").rglob("*")):
        if path.is_file() and "__pycache__" not in path.parts and path.suffix != ".pyc":
            if path.is_symlink():
                raise ValueError(f"SDK source must not be a symlink: {path}")
            files[path.relative_to(root).as_posix()] = path
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(".zip.tmp")
    try:
        with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as archive:
            for name, path in sorted(files.items()):
                archive.write(path, name)
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"SDK {SDK_VERSION}: {output.name} ({len(files)} files)")


if __name__ == "__main__":
    main()
