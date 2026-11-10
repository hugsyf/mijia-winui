"""Check the device insights bridge with offline cloud responses."""

import logging
import os
from pathlib import Path
import sys
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root / "backend"))
    with tempfile.TemporaryDirectory(prefix="mihome-device-insights-") as storage:
        os.environ["LOCALAPPDATA"] = storage
        import worker

        class OfflineApi(worker.Api):
            def _get_home_owner(self, home_id):
                assert home_id == "1"
                return 7

            def _request(self, uri, data):
                assert uri == "/v2/home/standard_consumable_items"
                assert data == {"home_id": 1, "owner_id": 7, "filter_ignore": True}
                return {"items": [
                    {"consumes_data": [{"did": "parent", "details": {
                        "id": 1, "description": "Filter", "value": "0", "inadeq": '{"unit":"percentage"}',
                        "extra_url": "not copied"}}]},
                    {"consumes_data": [
                        {"did": "parent", "details": [{"id": 2, "description": "Cartridge", "value": 12},
                                                       {"id": 3, "description": "Brush"}]},
                        {"did": "other", "details": {"description": "Unrelated"}}]},
                ]}

            def get_statistics(self, data):
                assert data["did"] == "parent" and data["key"] == "7.1"
                assert data["data_type"] == "stat_month_v3" and data["limit"] == 6
                start, end = data["time_start"], data["time_end"]
                return [{"time": start, "value": "[2.75]"}, {"time": start + 60, "value": "[0]"},
                        {"time": start + 120, "value": "[]"}, {"time": start + 180, "value": "[NaN]"},
                        {"time": end + 1, "value": "[42]"}, {"time": start + 240, "value": "__import__('os')"}]

        try:
            worker.api = OfflineApi({})
            worker.api.auth_data = {"userId": "fixture"}
            worker.home_rooms = {"1": [], "2": []}
            worker.devices = {"parent.s7": {"id": "parent.s7", "homeId": "1", "model": "lumi.acpartner.mcn04"}}
            params = {"deviceId": "parent.s7", "homeId": "1"}
            assert worker.dispatch("GetDeviceConsumables", params) == [
                {"label": "Filter", "value": "0%"}, {"label": "Cartridge", "value": "12"},
                {"label": "Brush", "value": None},
            ]
            points = worker.dispatch("GetDeviceEnergyHistory", params)
            assert [point["kilowattHours"] for point in points] == [0, 2.75]
            assert points[0]["time"] > points[1]["time"]
            for method, invalid in (("GetDeviceConsumables", params | {"homeId": "2"}),
                                    ("GetDeviceEnergyHistory", params | {"deviceId": "missing"})):
                try:
                    worker.dispatch(method, invalid)
                except ValueError:
                    pass
                else:
                    raise AssertionError("An out-of-scope device was accepted")
            assert worker.energy_profile({"id": "parent.s2", "model": "lumi.acpartner.mcn04"}) is None
            assert worker.energy_profile({"id": "parent", "model": "unknown"}) is None
        finally:
            logging.shutdown()
    print("PASS: consumable groups, device scope, normalized values and monthly energy records")


if __name__ == "__main__":
    main()
