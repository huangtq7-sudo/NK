import bpy
import json
import os
import sys


def script_args():
    if "--" not in sys.argv:
        return []
    return sys.argv[sys.argv.index("--") + 1 :]


def image_info(image):
    raw_path = image.filepath or ""
    absolute_path = bpy.path.abspath(raw_path) if raw_path else ""
    return {
        "name": image.name,
        "source": image.source,
        "filepath": raw_path,
        "absolute_path": absolute_path,
        "exists": bool(absolute_path and os.path.isfile(absolute_path)),
        "packed": image.packed_file is not None,
        "size": list(image.size),
        "colorspace": image.colorspace_settings.name,
        "file_format": image.file_format,
    }


def material_info(material):
    nodes = []
    if material.use_nodes and material.node_tree:
        for node in material.node_tree.nodes:
            item = {"name": node.name, "type": node.bl_idname, "label": node.label}
            if node.bl_idname == "ShaderNodeTexImage":
                item["image"] = node.image.name if node.image else None
                item["interpolation"] = node.interpolation
                item["projection"] = node.projection
            nodes.append(item)
    return {
        "name": material.name,
        "use_nodes": material.use_nodes,
        "surface_render_method": getattr(material, "surface_render_method", None),
        "use_transparency_overlap": getattr(material, "use_transparency_overlap", None),
        "node_count": len(nodes),
        "nodes": nodes,
    }


def object_info(obj):
    item = {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "hide_render": obj.hide_render,
        "location": list(obj.location),
        "rotation_euler": list(obj.rotation_euler),
        "scale": list(obj.scale),
    }
    if obj.type == "MESH":
        mesh = obj.data
        item.update(
            {
                "vertices": len(mesh.vertices),
                "polygons": len(mesh.polygons),
                "uv_layers": [uv.name for uv in mesh.uv_layers],
                "color_attributes": [color.name for color in mesh.color_attributes],
                "shape_keys": [key.name for key in mesh.shape_keys.key_blocks] if mesh.shape_keys else [],
                "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
                "vertex_groups": len(obj.vertex_groups),
                "armature_modifiers": [mod.object.name for mod in obj.modifiers if mod.type == "ARMATURE" and mod.object],
            }
        )
    elif obj.type == "ARMATURE":
        item.update(
            {
                "bones": len(obj.data.bones),
                "bone_names": [bone.name for bone in obj.data.bones],
                "pose_bones": len(obj.pose.bones) if obj.pose else 0,
                "action": obj.animation_data.action.name if obj.animation_data and obj.animation_data.action else None,
                "nla_tracks": [track.name for track in obj.animation_data.nla_tracks] if obj.animation_data else [],
            }
        )
    return item


def main():
    args = script_args()
    if len(args) != 1:
        raise SystemExit("Usage: blender -b file.blend --python blender_scene_audit.py -- output.json")
    output_path = os.path.abspath(args[0])
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    report = {
        "blend_file": bpy.data.filepath,
        "blender_version": bpy.app.version_string,
        "scene": bpy.context.scene.name,
        "frame_start": bpy.context.scene.frame_start,
        "frame_end": bpy.context.scene.frame_end,
        "unit_system": bpy.context.scene.unit_settings.system,
        "unit_scale": bpy.context.scene.unit_settings.scale_length,
        "objects": [object_info(obj) for obj in bpy.data.objects],
        "materials": [material_info(material) for material in bpy.data.materials],
        "images": [image_info(image) for image in bpy.data.images],
        "actions": [
            {
                "name": action.name,
                "frame_range": list(action.frame_range),
                "fcurves": len(getattr(action, "fcurves", [])),
                "groups": [group.name for group in getattr(action, "groups", [])],
                "slots": [slot.name_display for slot in getattr(action, "slots", [])],
                "layers": len(getattr(action, "layers", [])),
            }
            for action in bpy.data.actions
        ],
        "libraries": [library.filepath for library in bpy.data.libraries],
    }

    with open(output_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)
    print("AUDIT_WRITTEN=" + output_path)


if __name__ == "__main__":
    main()
