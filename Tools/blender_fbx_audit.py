import bpy
import json
import os
import sys


def args_after_separator():
    if "--" not in sys.argv:
        return []
    return sys.argv[sys.argv.index("--") + 1 :]


def action_curve_count(action):
    curves = {curve.as_pointer() for curve in getattr(action, "fcurves", [])}
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                curves.update(curve.as_pointer() for curve in bag.fcurves)
    return len(curves)


arguments = args_after_separator()
if len(arguments) != 2:
    raise RuntimeError("Usage: blender --python blender_fbx_audit.py -- <fbx> <report.json>")

fbx_path, report_path = arguments
bpy.ops.wm.read_factory_settings(use_empty=True)

if hasattr(bpy.ops.import_scene, "fbx"):
    bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
elif hasattr(bpy.ops.wm, "fbx_import"):
    bpy.ops.wm.fbx_import(filepath=fbx_path)
else:
    raise RuntimeError("No FBX importer is available")

meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
report = {
    "fbx": os.path.abspath(fbx_path),
    "objects": len(bpy.data.objects),
    "meshes": len(meshes),
    "vertices": sum(len(obj.data.vertices) for obj in meshes),
    "polygons": sum(len(obj.data.polygons) for obj in meshes),
    "armatures": [
        {
            "name": obj.name,
            "bones": len(obj.data.bones),
        }
        for obj in armatures
    ],
    "actions": [
        {
            "name": action.name,
            "frame_start": float(action.frame_range[0]),
            "frame_end": float(action.frame_range[1]),
            "fcurves": action_curve_count(action),
        }
        for action in bpy.data.actions
    ],
    "materials": len(bpy.data.materials),
}

os.makedirs(os.path.dirname(os.path.abspath(report_path)), exist_ok=True)
with open(report_path, "w", encoding="utf-8") as output:
    json.dump(report, output, ensure_ascii=False, indent=2)

print(json.dumps(report, ensure_ascii=False))
