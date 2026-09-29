import bpy
import json


result = []
for action in bpy.data.actions:
    entry = {
        "name": action.name,
        "frame_range": list(action.frame_range),
        "slots": [],
        "channelbags": [],
    }
    for slot in getattr(action, "slots", []):
        entry["slots"].append(
            {
                "identifier": slot.identifier,
                "display_name": slot.name_display,
                "target_id_type": slot.target_id_type,
            }
        )
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                paths = [curve.data_path for curve in bag.fcurves]
                non_pose_curves = []
                for curve in bag.fcurves:
                    if curve.data_path.startswith('pose.bones'):
                        continue
                    values = [point.co[1] for point in curve.keyframe_points]
                    non_pose_curves.append(
                        {
                            "path": curve.data_path,
                            "array_index": curve.array_index,
                            "keys": len(curve.keyframe_points),
                            "minimum": min(values) if values else None,
                            "maximum": max(values) if values else None,
                        }
                    )
                entry["channelbags"].append(
                    {
                        "slot_identifier": bag.slot.identifier if bag.slot else "",
                        "fcurves": len(bag.fcurves),
                        "pose_fcurves": sum(path.startswith('pose.bones') for path in paths),
                        "sample_paths": paths[:16],
                        "non_pose_curves": non_pose_curves,
                    }
                )
    result.append(entry)

print(json.dumps(result, ensure_ascii=False, indent=2))
