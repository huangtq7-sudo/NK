import json
import math
import os
import sys

import bpy
from mathutils import Vector


def value_to_json(value):
    if isinstance(value, (bool, int, float, str)) or value is None:
        return value
    try:
        return [float(item) for item in value]
    except (TypeError, ValueError):
        return str(value)


def socket_snapshot(socket):
    return {
        "name": socket.name,
        "identifier": getattr(socket, "identifier", ""),
        "linked": bool(socket.is_linked),
        "default": value_to_json(getattr(socket, "default_value", None)),
    }


def node_tree_snapshot(node_tree, tree_path="", visited=None):
    if node_tree is None:
        return []
    if visited is None:
        visited = set()
    pointer = node_tree.as_pointer()
    if pointer in visited:
        return []
    visited.add(pointer)
    nodes = []
    for node in node_tree.nodes:
        entry = {
            "tree": tree_path or node_tree.name,
            "name": node.name,
            "type": node.type,
            "inputs": [socket_snapshot(socket) for socket in node.inputs],
            "outputs": [
                {
                    "name": socket.name,
                    "default": value_to_json(getattr(socket, "default_value", None)),
                    "links": [
                        {
                            "to_node": link.to_node.name,
                            "to_type": link.to_node.type,
                            "to_socket": link.to_socket.name,
                        }
                        for link in socket.links
                    ],
                }
                for socket in node.outputs
            ],
        }
        if node.type == "TEX_IMAGE" and node.image:
            entry["image"] = node.image.name
        if node.type == "GROUP" and node.node_tree:
            entry["group_tree"] = node.node_tree.name
        nodes.append(entry)
        if node.type == "GROUP" and node.node_tree:
            child_path = (tree_path + "/" if tree_path else "") + node.node_tree.name
            nodes.extend(node_tree_snapshot(node.node_tree, child_path, visited))
    return nodes


def material_snapshot(material):
    result = {
        "name": material.name,
        "diffuse_color": list(material.diffuse_color),
        "surface_render_method": getattr(material, "surface_render_method", ""),
        "use_nodes": material.use_nodes,
        "outputs": [],
        "nodes": [],
    }
    if not material.use_nodes or not material.node_tree:
        return result

    result["nodes"] = node_tree_snapshot(material.node_tree)

    for output in (node for node in material.node_tree.nodes if node.type == "OUTPUT_MATERIAL"):
        surface = output.inputs.get("Surface")
        entry = {"active": bool(getattr(output, "is_active_output", False)), "source": None}
        if surface and surface.is_linked:
            source = surface.links[0].from_node
            entry["source"] = {
                "name": source.name,
                "type": source.type,
                "inputs": [socket_snapshot(socket) for socket in source.inputs],
            }
        result["outputs"].append(entry)
    return result


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return {
        "min": [min(corner[index] for corner in corners) for index in range(3)],
        "max": [max(corner[index] for corner in corners) for index in range(3)],
    }


def mesh_snapshot(obj):
    modifiers = []
    for modifier in obj.modifiers:
        if modifier.type == "ARMATURE":
            modifiers.append({
                "name": modifier.name,
                "type": modifier.type,
                "object": modifier.object.name if modifier.object else "",
            })
    return {
        "name": obj.name,
        "parent": obj.parent.name if obj.parent else "",
        "vertices": len(obj.data.vertices),
        "polygons": len(obj.data.polygons),
        "materials": [slot.material.name if slot.material else "" for slot in obj.material_slots],
        "hide_viewport": obj.hide_viewport,
        "hide_render": obj.hide_render,
        "visible": obj.visible_get(),
        "location": list(obj.location),
        "rotation_euler": list(obj.rotation_euler),
        "scale": list(obj.scale),
        "dimensions": list(obj.dimensions),
        "bounds_world": world_bounds(obj),
        "vertex_groups": len(obj.vertex_groups),
        "armature_modifiers": modifiers,
    }


def object_snapshot(obj):
    return {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else "",
        "location": list(obj.location),
        "rotation_euler": list(obj.rotation_euler),
        "scale": list(obj.scale),
        "matrix_world_scale": list(obj.matrix_world.to_scale()),
        "dimensions": list(obj.dimensions),
    }


def main():
    args = sys.argv[sys.argv.index("--") + 1 :]
    if len(args) != 2:
        raise RuntimeError("Usage: blender --python blender_character_diagnostic.py -- <blend-or-fbx> <report.json>")
    source_path, report_path = map(os.path.abspath, args)

    if source_path.lower().endswith(".blend"):
        bpy.ops.wm.open_mainfile(filepath=source_path)
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.wm.fbx_import(filepath=source_path)

    report = {
        "source": source_path,
        "objects": [object_snapshot(obj) for obj in bpy.data.objects],
        "meshes": [mesh_snapshot(obj) for obj in bpy.data.objects if obj.type == "MESH"],
        "materials": [material_snapshot(material) for material in bpy.data.materials],
    }
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)
    print(json.dumps({
        "source": source_path,
        "meshes": len(report["meshes"]),
        "materials": len(report["materials"]),
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
