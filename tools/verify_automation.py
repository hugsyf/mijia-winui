"""Offline bridge checks for automation ownership and scene dispatch."""

import logging
import os
from pathlib import Path
import sys
import tempfile
import time
from types import SimpleNamespace


def require_error(callback, error=ValueError):
    try:
        callback()
    except error:
        return
    raise AssertionError(f"Expected {error.__name__}")


def main():
    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root / "backend"))
    (root / "tmp").mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="automation-check-", dir=root / "tmp") as storage:
        assert root in Path(storage).resolve().parents
        os.environ["LOCALAPPDATA"] = storage
        import worker

        calls = []
        worker.api = SimpleNamespace(
            auth_data={"userId": "fixture-account"},
            get_scenes_list=lambda home: [{"scene_id": "1", "name": "Game", "private": "unused"}] if home == "a" else [],
            run_scene=lambda scene, home: calls.append((scene, home)) or True,
        )
        worker.home_rooms = {"a": [], "b": []}
        try:
            account = worker.dispatch("GetAutomationAccount", {})["account"]
            assert "fixture-account" not in account
            assert worker.dispatch("ListScenes", {"homeId": "a"}) == [{"id": "1", "name": "Game", "homeId": "a"}]
            scope = dict(account=account, homeId="a", specType="lamp-spec", runId="0" * 32, expiresAt=time.time() + 45)
            scene = dict(homeId="a", sceneId="1", automation=scope)
            assert worker.dispatch("RunScene", scene) == {"accepted": True}
            require_error(lambda: worker.dispatch("RunScene", scene | {"sceneId": "missing"}))
            require_error(lambda: worker.dispatch("RunScene", scene | {"homeId": "b"}))
            require_error(lambda: worker.dispatch("RunScene", scene | {"automation": scope | {"account": "other"}}))
            worker.cancelled_runs[scope["runId"]] = time.time() + 120
            require_error(lambda: worker.dispatch("RunScene", scene))
            worker.cancelled_runs.clear()
            require_error(lambda: worker.dispatch("RunScene", scene | {"automation": scope | {"expiresAt": 0}}))
            assert calls == [("1", "a")]

            device = dict(homeId="a", specType="lamp-spec", online=True)
            worker.check_automation({"automation": scope}, device)
            for changed in (dict(homeId="b"), dict(specType="other"), dict(online=False)):
                require_error(lambda: worker.check_automation({"automation": scope}, device | changed))
            print("PASS: scene filtering, scene dispatch, account isolation, expiration, cancellation and device scope")
        finally:
            logging.shutdown()


if __name__ == "__main__":
    main()
