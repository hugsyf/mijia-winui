"""Remove local debug paths and unused pip console launchers from published files."""
from pathlib import Path
import sys


def prepare(directory):
    expected = Path(__file__).resolve().parents[1] / "artifacts/app"
    directory = Path(directory).resolve()
    if directory != expected.resolve():
        raise ValueError("Expected the project artifacts/app directory.")
    removed = 0
    for path in directory.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(directory)
        pip_launcher = relative.parts[:4] == ("runtime", "Lib", "site-packages", "bin") and path.suffix.lower() == ".exe"
        if path.suffix.lower() in {".pdb", ".pyc", ".pyo"} or pip_launcher:
            if path.is_symlink() or directory not in path.resolve().parents:
                raise ValueError("Release file escapes the publish directory.")
            path.unlink()
            removed += 1
    print(f"Removed {removed} build-only files from the published directory.")


if __name__ == "__main__":
    prepare(sys.argv[1])
