import bpy
import json
import os
import re
import shutil
import sys
import traceback


# These materials use Blender-only color processing (Mix, RGB Curves, ramps,
# and procedural eye nodes).  Baking the evaluated diffuse color preserves the
# authored result instead of asking Unity to guess at the node graph.
BAKED_BASE_MATERIALS = {
    "mat_19",
    "mat_38",
    "mat_50",
    "Auto Eye - CORNEA/SCLERA",
}

# In these card materials the image RGB is not used by Blender; only its alpha
# drives opacity.  Keep the texture for alpha, but use the authored constant
# color for RGB in Unity.
BASE_COLOR_OVERRIDES = {
    "mat_21": [0.008645, 0.008645, 0.008645, 1.0],
    "mat_25": [1.0, 1.0, 1.0, 1.0],
}

NORMAL_SWAP_RG_MATERIALS = {"mat_19", "mat_2"}
NORMAL_INVERT_R_MATERIALS = {"mat_21", "mat_25"}


def script_args():
    if "--" not in sys.argv:
        return []
    return sys.argv[sys.argv.index("--") + 1 :]


def safe_name(value):
    value = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", value).strip(" .")
    return value or "unnamed"


def unique_path(directory, filename, used):
    stem, extension = os.path.splitext(filename)
    candidate = filename
    index = 2
    while candidate.lower() in used:
        candidate = f"{stem}_{index}{extension}"
        index += 1
    used.add(candidate.lower())
    return os.path.join(directory, candidate)


def find_existing_image(image, source_directory):
    raw_path = image.filepath or ""
    if raw_path:
        absolute = bpy.path.abspath(raw_path)
        if os.path.isfile(absolute):
            return absolute

    basename = os.path.basename(raw_path) or image.name
    for root, _, files in os.walk(source_directory):
        for filename in files:
            if filename.lower() == basename.lower():
                return os.path.join(root, filename)
    return None


def save_image(image, destination):
    source = find_existing_image(image, os.path.dirname(bpy.data.filepath))
    if source:
        shutil.copy2(source, destination)
        return "copied"

    # Blender can report has_data=True for packed images while image.save()
    # still tries to resolve the now-missing original path.  Reading the
    # PackedFile bytes preserves the exact embedded texture without decoding.
    packed_file = getattr(image, "packed_file", None)
    if packed_file is None:
        packed_entries = list(getattr(image, "packed_files", []))
        packed_file = packed_entries[0].packed_file if packed_entries else None
    if packed_file is not None:
        packed_data = packed_file.data
        if packed_data:
            with open(destination, "wb") as output_file:
                output_file.write(packed_data)
            return "extracted-packed"

    if not image.has_data:
        try:
            image.reload()
        except Exception:
            pass
    if not image.has_data:
        raise RuntimeError(f"Image has no pixel data: {image.name}")

    old_path = image.filepath_raw
    old_format = image.file_format
    try:
        image.filepath_raw = destination
        image.file_format = "PNG"
        image.save()
    finally:
        image.filepath_raw = old_path
        try:
            image.file_format = old_format
        except Exception:
            pass
    return "extracted"


def principled_targets(image_node):
    targets = set()
    queue = [(image_node, 0)]
    visited = set()
    while queue:
        node, depth = queue.pop(0)
        if depth > 12:
            continue
        pointer = node.as_pointer()
        if pointer in visited:
            continue
        visited.add(pointer)
        for output in node.outputs:
            for link in output.links:
                destination = link.to_node
                targets.add(link.to_socket.name.lower())
                if destination.bl_idname == "ShaderNodeBsdfPrincipled":
                    continue
                else:
                    queue.append((destination, depth + 1))
    return targets


def classify_texture(image_name, colorspace, image_node=None):
    name = image_name.lower()
    stem = os.path.splitext(name)[0]
    if "ink" in name and "mrav" in name:
        return "detail_mrav"
    if "mrav" in name:
        return "mrav"
    if "rough" in name or "糙度" in name:
        return "roughness"
    if re.search(r"(^|[_\-.])(n|nx|normal)([_\-.]|$)", stem) or "法线" in name:
        return "normal"
    if re.search(r"(^|[_\-.])(em|emissive|emission|e)([_\-.]|$)", stem):
        return "emission"
    if "mask" in name or re.search(r"(^|[_\-.])(tr|tx|alpha)([_\-.]|$)", stem):
        return "mask"
    if "ink" in name and re.search(r"(^|[_\-.])d([_\-.]|$)", stem):
        return "detail_base"
    if re.search(r"(^|[_\-.])(d|diffuse|albedo|basecolor)([_\-.]|$)", stem) or "基础色" in name:
        return "base"
    if "skin" in name:
        return "skin_detail"
    targets = principled_targets(image_node) if image_node is not None else set()
    if "base color" in targets or "色彩" in targets:
        return "base"
    if "normal" in targets or "tangent" in targets or "coat normal" in targets or "法向" in targets:
        return "normal"
    if "emission color" in targets or "emission" in targets:
        return "emission"
    if "alpha" in targets or "透明" in targets or "遮罩" in targets:
        return "mask"
    if "metallic" in targets or "roughness" in targets or "信息" in targets:
        return "mrav"
    if colorspace and colorspace.lower() == "srgb":
        return "color_detail"
    return "data"


def material_image_nodes(material):
    if not material.use_nodes or not material.node_tree:
        return []
    images = []
    visited_trees = set()

    def collect(node_tree):
        if node_tree is None:
            return
        pointer = node_tree.as_pointer()
        if pointer in visited_trees:
            return
        visited_trees.add(pointer)
        for node in node_tree.nodes:
            if node.bl_idname == "ShaderNodeTexImage" and node.image is not None:
                images.append(node)
            elif node.bl_idname == "ShaderNodeGroup" and node.node_tree is not None:
                collect(node.node_tree)

    collect(material.node_tree)
    return images


def material_uses_alpha(material):
    if not material.use_nodes or not material.node_tree:
        return False
    visited_trees = set()

    def scan(node_tree):
        if node_tree is None:
            return False
        pointer = node_tree.as_pointer()
        if pointer in visited_trees:
            return False
        visited_trees.add(pointer)
        for node in node_tree.nodes:
            if node.bl_idname == "ShaderNodeBsdfPrincipled":
                alpha = node.inputs.get("Alpha")
                if alpha is not None and alpha.is_linked:
                    return True
            if node.bl_idname == "ShaderNodeGroup" and node.node_tree is not None and scan(node.node_tree):
                return True
        return False

    return scan(material.node_tree)


def material_emission_strength(material):
    if not material.use_nodes or not material.node_tree:
        return 0.0
    outputs = [node for node in material.node_tree.nodes if node.bl_idname == "ShaderNodeOutputMaterial"]
    output = next((node for node in outputs if getattr(node, "is_active_output", False)), outputs[0] if outputs else None)
    surface = output.inputs.get("Surface") if output is not None else None
    if surface is None or not surface.is_linked:
        return 0.0
    source = surface.links[0].from_node

    if source.bl_idname == "ShaderNodeBsdfPrincipled":
        emission = source.inputs.get("Emission Color") or source.inputs.get("Emission")
        if emission is None or not emission.is_linked:
            return 0.0
        strength = source.inputs.get("Emission Strength")
        return float(strength.default_value) if strength is not None and not strength.is_linked else 1.0

    if source.bl_idname == "ShaderNodeGroup":
        for socket in source.inputs:
            socket_name = socket.name.lower()
            if "emission strength" not in socket_name and "发光" not in socket.name:
                continue
            if socket.is_linked:
                return 1.0
            try:
                return float(socket.default_value)
            except (TypeError, ValueError):
                return 0.0
    return 0.0


def bake_material_base(material, texture_directory, unity_asset_directory):
    objects = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH" and any(slot.material == material for slot in obj.material_slots)
    ]
    if not objects:
        return None

    source_images = [node.image for node in material_image_nodes(material) if node.image]
    width = min(2048, max([int(image.size[0]) for image in source_images] + [1024]))
    height = min(2048, max([int(image.size[1]) for image in source_images] + [1024]))
    image = bpy.data.images.new(material.name + "_UnityBase", width=width, height=height, alpha=True)
    image.generated_color = (0.0, 0.0, 0.0, 0.0)

    related_materials = {
        slot.material
        for obj in objects
        for slot in obj.material_slots
        if slot.material and slot.material.use_nodes and slot.material.node_tree
    }
    target_nodes = {}
    dummy = bpy.data.images.new(material.name + "_UnityBakeDummy", width=16, height=16, alpha=True)
    try:
        for candidate in related_materials:
            node = candidate.node_tree.nodes.new("ShaderNodeTexImage")
            node.name = "__UNITY_BASE_BAKE_TARGET__"
            node.image = image if candidate == material else dummy
            candidate.node_tree.nodes.active = node
            node.select = True
            target_nodes[candidate] = node

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
        destination = os.path.join(texture_directory, filename)
        image.filepath_raw = destination
        image.file_format = "PNG"
        image.save()
        relative = os.path.relpath(destination, os.path.dirname(texture_directory)).replace("\\", "/")
        return {
            "file": filename,
            "asset_path": unity_asset_directory.rstrip("/") + "/" + relative,
            "mode": "baked-diffuse-color",
            "width": width,
            "height": height,
        }
    finally:
        for candidate, node in target_nodes.items():
            candidate.node_tree.nodes.remove(node)
        bpy.data.images.remove(dummy)


def material_manifest(material, exported_images, baked_materials):
    texture_entries = []
    role_counts = {}
    for node in material_image_nodes(material):
        image = node.image
        exported = exported_images.get(image.name)
        if not exported:
            continue
        base_role = classify_texture(image.name, image.colorspace_settings.name, node)
        role_index = role_counts.get(base_role, 0)
        role_counts[base_role] = role_index + 1
        role = base_role if role_index == 0 else f"{base_role}_{role_index + 1}"
        texture_entries.append(
            {
                "role": role,
                "image": image.name,
                "asset_path": exported["asset_path"],
                "colorspace": image.colorspace_settings.name,
                "uv_map": getattr(node, "uv_map", ""),
                "channel": (
                    "a"
                    if base_role == "mask"
                    and node.outputs.get("Alpha") is not None
                    and node.outputs.get("Alpha").is_linked
                    else "r"
                ),
            }
        )

    baked = baked_materials.get(material.name)
    if baked:
        texture_entries.insert(
            0,
            {
                "role": "baked_base",
                "image": baked["file"],
                "asset_path": baked["asset_path"],
                "colorspace": "sRGB",
                "uv_map": "",
                "channel": "rgb",
            },
        )

    blend_method = getattr(material, "surface_render_method", "DITHERED")
    base_color = BASE_COLOR_OVERRIDES.get(material.name, list(material.diffuse_color))
    return {
        "name": material.name,
        "base_color": base_color,
        "metallic": float(getattr(material, "metallic", 0.0)),
        "roughness": float(getattr(material, "roughness", 0.5)),
        "emission_strength": material_emission_strength(material),
        "blend_method": str(blend_method),
        "double_sided": not bool(getattr(material, "use_backface_culling", False)),
        "uses_alpha": material_uses_alpha(material),
        "ignore_base_texture_color": material.name in BASE_COLOR_OVERRIDES,
        "normal_swap_rg": material.name in NORMAL_SWAP_RG_MATERIALS,
        "normal_invert_r": material.name in NORMAL_INVERT_R_MATERIALS,
        "textures": texture_entries,
    }


def animation_manifest():
    result = []
    for action in bpy.data.actions:
        result.append(
            {
                "name": action.name,
                "source_name": action.get("unity_source_name", action.name),
                "frame_start": float(action.frame_range[0]),
                "frame_end": float(action.frame_range[1]),
                "slots": [slot.name_display for slot in getattr(action, "slots", [])],
            }
        )
    return result


def normalize_action_names():
    # FBX limits/normalizes long action names. Several source actions differ
    # only at the tail, so they otherwise collapse into the same Unity clip.
    for index, action in enumerate(bpy.data.actions, start=1):
        source_name = action.name
        action["unity_source_name"] = source_name
        start = int(round(action.frame_range[0]))
        end = int(round(action.frame_range[1]))
        action.name = f"Anim_{index:02d}_{start}_{end}"


def normalize_action_object_scales():
    # Extracted FBX actions sometimes retain their original centimeter-to-meter
    # object scale (0.01).  The Unity model is exported at scale 1, so allowing
    # those curves through shrinks the whole character whenever a clip is
    # sampled.  Preserve animated bone scale, but normalize only object-level
    # scale curves that are effectively a constant 0.01 conversion.
    normalized = 0
    for action in bpy.data.actions:
        for curve in action_fcurves(action):
            if curve.data_path != "scale":
                continue
            values = [point.co[1] for point in curve.keyframe_points]
            if not values or max(abs(value - 0.01) for value in values) > 0.0001:
                continue
            for point in curve.keyframe_points:
                point.co[1] *= 100.0
                point.handle_left[1] *= 100.0
                point.handle_right[1] *= 100.0
            normalized += 1
    return normalized


def action_fcurves(action):
    seen = set()
    for curve in getattr(action, "fcurves", []):
        if curve.as_pointer() not in seen:
            seen.add(curve.as_pointer())
            yield curve
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                for curve in bag.fcurves:
                    if curve.as_pointer() not in seen:
                        seen.add(curve.as_pointer())
                        yield curve


def remap_animation_bone_paths():
    # Some extracted Naraka actions use underscore-separated bone names while
    # the accompanying skeleton uses spaces. Map only exact known bone names,
    # so animation curves become compatible without altering unrelated paths.
    bone_names = {
        bone.name
        for obj in bpy.data.objects
        if obj.type == "ARMATURE"
        for bone in obj.data.bones
    }
    pattern = re.compile(r'pose\.bones\["((?:\\.|[^"\\])*)"\]')
    remapped = 0
    for action in bpy.data.actions:
        for curve in action_fcurves(action):
            match = pattern.search(curve.data_path)
            if not match:
                continue
            source_bone = match.group(1).replace('\\"', '"').replace('\\\\', '\\')
            if source_bone in bone_names:
                continue
            candidate = source_bone.replace("_", " ")
            if candidate not in bone_names:
                continue
            escaped = candidate.replace('\\', '\\\\').replace('"', '\\"')
            curve.data_path = curve.data_path[: match.start(1)] + escaped + curve.data_path[match.end(1) :]
            remapped += 1
    return remapped


def export_fbx(destination):
    # Nested armature objects produce an invalid FBX hierarchy in Blender's
    # exporter (and are not a supported Unity rig layout). Preserve their
    # world transform but make each rig an independent root. Mesh parenting
    # and Armature modifiers remain attached to the correct rig.
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE" and obj.parent is not None and obj.parent.type == "ARMATURE":
            world_matrix = obj.matrix_world.copy()
            obj.parent = None
            obj.matrix_world = world_matrix

    for obj in bpy.data.objects:
        try:
            obj.hide_set(False)
        except Exception:
            pass
        obj.hide_viewport = False

    if not hasattr(bpy.ops.export_scene, "fbx"):
        raise RuntimeError("Blender FBX exporter is not available")

    requested = {
        "filepath": destination,
        "check_existing": False,
        "use_selection": False,
        "use_visible": False,
        "use_active_collection": False,
        "global_scale": 1.0,
        "apply_unit_scale": True,
        "apply_scale_options": "FBX_SCALE_ALL",
        "use_space_transform": True,
        "bake_space_transform": False,
        "object_types": {"ARMATURE", "EMPTY", "MESH"},
        "use_mesh_modifiers": True,
        "use_mesh_modifiers_render": True,
        "mesh_smooth_type": "FACE",
        "colors_type": "SRGB",
        "use_subsurf": False,
        "use_mesh_edges": False,
        "use_tspace": True,
        "use_triangles": False,
        "use_custom_props": True,
        "add_leaf_bones": False,
        "primary_bone_axis": "Y",
        "secondary_bone_axis": "X",
        "use_armature_deform_only": False,
        "armature_nodetype": "NULL",
        # Animation clips are exported separately. Baking every action into the
        # model FBX evaluates legacy object-scale curves and changes the rest
        # hierarchy to 0.01, which makes the skinned body disappear in Unity.
        "bake_anim": False,
        "bake_anim_use_all_bones": True,
        "bake_anim_use_nla_strips": True,
        "bake_anim_use_all_actions": True,
        "bake_anim_force_startend_keying": True,
        "bake_anim_step": 1.0,
        "bake_anim_simplify_factor": 0.0,
        "path_mode": "AUTO",
        "embed_textures": False,
        "batch_mode": "OFF",
        "use_batch_own_dir": True,
        "axis_forward": "-Z",
        "axis_up": "Y",
    }
    properties = set(bpy.ops.export_scene.fbx.get_rna_type().properties.keys())
    options = {key: value for key, value in requested.items() if key in properties}
    result = bpy.ops.export_scene.fbx(**options)
    if "FINISHED" not in result:
        raise RuntimeError(f"FBX export failed: {result}")


def export_animation_fbxs(output_directory, unity_asset_directory):
    actions = list(bpy.data.actions)
    armatures = sorted(
        (obj for obj in bpy.data.objects if obj.type == "ARMATURE"),
        key=lambda obj: len(obj.data.bones),
        reverse=True,
    )
    if not actions or not armatures:
        return []

    target_armature = armatures[0]
    animation_directory = os.path.join(output_directory, "Animations")
    os.makedirs(animation_directory, exist_ok=True)
    animation_data = target_armature.animation_data_create()
    previous_action = animation_data.action
    previous_start = bpy.context.scene.frame_start
    previous_end = bpy.context.scene.frame_end
    exports = []

    requested_base = {
        "check_existing": False,
        "use_selection": True,
        "use_visible": False,
        "use_active_collection": False,
        "global_scale": 1.0,
        "apply_unit_scale": True,
        "apply_scale_options": "FBX_SCALE_ALL",
        "use_space_transform": True,
        "bake_space_transform": False,
        "object_types": {"ARMATURE"},
        "use_custom_props": True,
        "add_leaf_bones": False,
        "primary_bone_axis": "Y",
        "secondary_bone_axis": "X",
        "use_armature_deform_only": False,
        "armature_nodetype": "NULL",
        "bake_anim": True,
        "bake_anim_use_all_bones": True,
        "bake_anim_use_nla_strips": False,
        "bake_anim_use_all_actions": False,
        "bake_anim_force_startend_keying": True,
        "bake_anim_step": 1.0,
        "bake_anim_simplify_factor": 0.0,
        "path_mode": "AUTO",
        "embed_textures": False,
        "axis_forward": "-Z",
        "axis_up": "Y",
    }
    properties = set(bpy.ops.export_scene.fbx.get_rna_type().properties.keys())

    try:
        for action in actions:
            for obj in bpy.context.view_layer.objects:
                obj.select_set(False)
            target_armature.select_set(True)
            bpy.context.view_layer.objects.active = target_armature
            animation_data.action = action
            bpy.context.scene.frame_start = int(action.frame_range[0])
            bpy.context.scene.frame_end = int(action.frame_range[1])
            destination = os.path.join(animation_directory, safe_name(action.name) + ".fbx")
            requested = dict(requested_base)
            requested["filepath"] = destination
            options = {key: value for key, value in requested.items() if key in properties}
            result = bpy.ops.export_scene.fbx(**options)
            if "FINISHED" not in result:
                raise RuntimeError(f"Animation FBX export failed for {action.name}: {result}")
            relative = os.path.relpath(destination, output_directory).replace("\\", "/")
            exports.append(
                {
                    "name": action.name,
                    "source_name": action.get("unity_source_name", action.name),
                    "asset_path": unity_asset_directory.rstrip("/") + "/" + relative,
                    "frame_start": float(action.frame_range[0]),
                    "frame_end": float(action.frame_range[1]),
                    "fcurves": sum(1 for _ in action_fcurves(action)),
                }
            )
    finally:
        animation_data.action = previous_action
        bpy.context.scene.frame_start = previous_start
        bpy.context.scene.frame_end = previous_end
    return exports


def main():
    args = script_args()
    if len(args) != 4:
        raise SystemExit(
            "Usage: blender -b source.blend --python blender_to_unity_export.py -- "
            "model_name output_directory unity_asset_directory manifest_path"
        )

    model_name, output_directory, unity_asset_directory, manifest_path = args
    output_directory = os.path.abspath(output_directory)
    texture_directory = os.path.join(output_directory, "Textures")
    os.makedirs(texture_directory, exist_ok=True)
    os.makedirs(os.path.dirname(os.path.abspath(manifest_path)), exist_ok=True)

    used_images = {}
    for material in bpy.data.materials:
        for node in material_image_nodes(material):
            used_images[node.image.name] = node.image

    exported_images = {}
    used_filenames = set()
    extraction_errors = []
    for image_name, image in used_images.items():
        extension = os.path.splitext(image.name)[1].lower()
        if extension not in {".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".bmp"}:
            extension = ".png"
        filename = safe_name(os.path.splitext(image.name)[0]) + extension
        destination = unique_path(texture_directory, filename, used_filenames)
        try:
            mode = save_image(image, destination)
            relative = os.path.relpath(destination, output_directory).replace("\\", "/")
            asset_path = unity_asset_directory.rstrip("/") + "/" + relative
            exported_images[image_name] = {
                "file": os.path.basename(destination),
                "asset_path": asset_path,
                "mode": mode,
                "width": int(image.size[0]),
                "height": int(image.size[1]),
            }
        except Exception as error:
            extraction_errors.append({"image": image_name, "error": str(error)})

    previous_engine = bpy.context.scene.render.engine
    bpy.context.scene.render.engine = "CYCLES"
    bpy.context.scene.cycles.samples = 1
    bpy.context.scene.render.bake.use_clear = True
    bpy.context.scene.render.bake.margin = 12
    baked_materials = {}
    try:
        for material in bpy.data.materials:
            if material.name not in BAKED_BASE_MATERIALS:
                continue
            baked = bake_material_base(material, texture_directory, unity_asset_directory)
            if baked:
                baked_materials[material.name] = baked
    finally:
        bpy.context.scene.render.engine = previous_engine

    remapped_animation_curves = remap_animation_bone_paths()
    normalized_object_scale_curves = normalize_action_object_scales()
    normalize_action_names()
    fbx_name = safe_name(model_name) + ".fbx"
    fbx_path = os.path.join(output_directory, fbx_name)
    export_fbx(fbx_path)
    animation_assets = export_animation_fbxs(output_directory, unity_asset_directory)

    manifest = {
        "version": 2,
        "model_name": model_name,
        "source_blend": bpy.data.filepath,
        "fbx_asset_path": unity_asset_directory.rstrip("/") + "/" + fbx_name,
        "unit_system": bpy.context.scene.unit_settings.system,
        "unit_scale": float(bpy.context.scene.unit_settings.scale_length),
        "objects": [
            {
                "name": obj.name,
                "type": obj.type,
                "parent": obj.parent.name if obj.parent else "",
            }
            for obj in bpy.data.objects
        ],
        "armatures": [
            {
                "name": obj.name,
                "bones": len(obj.data.bones),
                "bone_names": [bone.name for bone in obj.data.bones],
            }
            for obj in bpy.data.objects
            if obj.type == "ARMATURE"
        ],
        "animations": animation_manifest(),
        "animation_assets": animation_assets,
        "remapped_animation_curves": remapped_animation_curves,
        "normalized_object_scale_curves": normalized_object_scale_curves,
        "materials": [material_manifest(material, exported_images, baked_materials) for material in bpy.data.materials],
        "images": list(exported_images.values()) + list(baked_materials.values()),
        "image_errors": extraction_errors,
    }

    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=2)
    print("FBX_WRITTEN=" + fbx_path)
    print("MANIFEST_WRITTEN=" + os.path.abspath(manifest_path))
    print("EXPORTED_IMAGES=" + str(len(exported_images)))
    print("BAKED_MATERIALS=" + str(len(baked_materials)))
    print("IMAGE_ERRORS=" + str(len(extraction_errors)))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        raise
