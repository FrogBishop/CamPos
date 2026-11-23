import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from tools.export_area_hints import make_profiles, mc_geometry, publish


def shape():
    return {"minArea": {"y": -7.5}, "maxArea": {"y": 96},
            "roadPoints": [{"x": 10, "z": -30}, {"x": 20, "z": -30}, {"x": 20, "z": -12}]}


class ExportTests(unittest.TestCase):
    def test_signed_swap_and_bounds(self):
        vertices, box, altitude = mc_geometry(shape())
        self.assertEqual(vertices, [{"x": 30, "z": -10}, {"x": 30, "z": -20}, {"x": 12, "z": -20}])
        self.assertEqual(box, [{"x": 12, "z": -10}, {"x": 30, "z": -10},
                               {"x": 30, "z": -20}, {"x": 12, "z": -20}])
        self.assertEqual(altitude, {"min": -7.5, "max": 96})

    def test_filtered_parent_preserves_priority_and_multipart_names(self):
        areas = [
            {"sceneId": 3, "configId": 10, "level": 1, "areaID1": 1, "nameCHS": "同名", "showTips": None, "shapes": [shape()]},
            {"sceneId": 3, "configId": 11, "level": 2, "areaID1": 1, "nameCHS": "同名", "showTips": True, "shapes": [shape(), shape()]},
            {"sceneId": 3, "configId": 20, "level": 1, "areaID1": 2, "nameCHS": None, "showTips": True, "shapes": [shape()]},
        ]
        files = make_profiles(areas)
        full = files["all/scene-3/overworld.json"]
        tips = files["tips-only/scene-3/overworld.json"]
        self.assertEqual([a["name"] for a in tips], ["同名〔11-1〕", "同名〔11-2〕", "区域20"])
        self.assertTrue(all(a["level"] == 1 and a["base-name"] is None for a in tips))
        self.assertEqual([(a["surfacename"], a["base-name"]) for a in full if a["level"] == 2],
                         [("同名", "同名〔10〕"), ("同名", "同名〔10〕")])

    def test_repeat_export_preserves_a_manually_edited_copy(self):
        with TemporaryDirectory() as temp:
            output = Path(temp)

            def export_once():
                with TemporaryDirectory(dir=output) as stage:
                    package = Path(stage) / "package"
                    package.mkdir()
                    (package / "overworld.json").write_bytes(b"[]\n")
                    return publish(package, output, "fixed-version")

            first = export_once()
            self.assertEqual(export_once(), first)
            (first / "overworld.json").write_bytes(b"custom")
            self.assertNotEqual(export_once(), first)
            self.assertEqual((first / "overworld.json").read_bytes(), b"custom")
