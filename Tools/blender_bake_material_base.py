import json
import os
import re
import sys

import bpy


def safe_name(value):
    value = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", value).strip(" .")
    return value or "material"


def recursive_image_nodes(material):
    result = []
    visited = set()

    def collect(node_tree):
        if node_tree is None or node_tree.as_pointer() in visited:
            return
        visited.add(node_tree.as_pointer())
        for node in node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                result.append(node)
            elif node.type == "GROUP" and node.node_tree:
                collect(node.node_tree)

    collect(material.node_tree)
    return result


def bake_material(material, output_dir):
    objects = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH" and any(slot.material == material for slot in obj.material_slots)
    ]
    if not objects:
        return None

    source_images = [node.image for node in recursive_image_nodes(material) if node.image]
    width = min(2048, max([int(image.size[0]) for image in source_images] + [1024]))
    height = min(2048, max([int(image.size[1]) for image in source_images] + [1024]))
    image = bpy.data.images.new(material.name + "_UnityBase", width=width, height=height, alpha=True)
    image.generated_color = (0.0, 0.0, 0.0, 0.0)

    materials = {
        slot.material
        for obj in objects
        for slot in obj.material_slots
        if slot.material and slot.material.use_nodes and slot.material.node_tree
    }
    nodes = {}
    dummy = bpy.data.images.new("UnityBakeDummy", width=16, height=16, alpha=True)
    try:
        for candidate in materials:
            node = candidate.node_tree.nodes.new("ShaderNodeTexImage")
            node.name = "__UNITY_BASE_BAKE_TARGET__"
            node.image = image if candidate == material else dummy
            candidate.node_tree.nodes.active = node
            node.select = True
            nodes[candidate] = node

        for obj in bpy.context.view_layer.objects:
            obj.select_set(False)
        for obj in objects:
            obj.hide_render = False
            obj.hide_viewport = False
            obj.select_set(True)
        bpy.context.view_layer.objects.active = objects[0]
        bpy.ops.object.bake(
            type="DIFFUSE",
            pass_filter={"COLOR"},
            use_clear=True,
            use_selected_to_active=False,
            margin=12,
        )

        filename = safe_name(material.name) + "_baked_base.png"
        destination = os.path.join(output_dir, filename)
        image.filepath_raw = destination
        image.file_format = "PNG"
        image.save()
        return {
            "material": material.name,
            "file": filename,
            "width": width,
            "height": height,
        }
    finally:
        for candidate, node in nodes.items():
            candidate.node_tree.nodes.remove(node)
        bpy.data.images.remove(dummy)


def main():
    args = sys.argv[sys.argv.index("--") + 1 :]
    if len(args) < 2:
        raise RuntimeError("Usage: blender -b source.blend --python blender_bake_material_base.py -- <output-dir> <material>...")
    output_dir = os.path.abspath(args[0])
    material_names = args[1:]
    os.makedirs(output_dir, exist_ok=True)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.render.bake.use_clear = True
    scene.render.bake.margin = 12

    results = []
    for material_name in material_names:
        material = bpy.data.materials.get(material_name)
        if material is None:
            raise RuntimeError("Missing material: " + material_name)
        results.append(bake_material(material, output_dir))
    print("BAKE_RESULTS=" + json.dumps(results, ensure_ascii=False))


if __name__ == "__main__":
    main()
