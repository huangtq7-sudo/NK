import math
import os
import sys

import bpy
from mathutils import Vector


def visible_meshes():
    result = []
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        obj.hide_render = False
        obj.hide_viewport = False
        try:
            obj.hide_set(False)
        except RuntimeError:
            pass
        result.append(obj)
    return result


def world_bounds(objects):
    corners = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    minimum = Vector(tuple(min(corner[index] for corner in corners) for index in range(3)))
    maximum = Vector(tuple(max(corner[index] for corner in corners) for index in range(3)))
    return minimum, maximum


def look_at(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def add_area_light(name, location, target, energy, color, size):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.color = color
    data.shape = "DISK"
    data.size = size
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    look_at(obj, target)


def render_view(scene, camera, target, direction, distance, filepath, width, height):
    camera.location = target - direction.normalized() * distance
    look_at(camera, target)
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.render.filepath = filepath
    bpy.ops.render.render(write_still=True)


def main():
    args = sys.argv[sys.argv.index("--") + 1 :]
    if len(args) != 2:
        raise RuntimeError("Usage: blender -b source.blend --python blender_render_character_preview.py -- <name> <output-dir>")
    output_name, output_dir = args
    output_dir = os.path.abspath(output_dir)
    os.makedirs(output_dir, exist_ok=True)

    # Some source files lock their original scene output to multilayer EXR.
    # A clean scene keeps linked character objects/materials while restoring
    # normal image-output settings for deterministic PNG previews.
    source_objects = list(bpy.data.objects)
    scene = bpy.data.scenes.new("Naraka Preview")
    for obj in source_objects:
        scene.collection.objects.link(obj)
    bpy.context.window.scene = scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.resolution_percentage = 100
    scene.render.image_settings.color_mode = "RGBA"
    scene.world = bpy.data.worlds.new("Naraka Preview World")
    scene.world.color = (0.12, 0.13, 0.15)

    for obj in list(bpy.data.objects):
        if obj.type in {"CAMERA", "LIGHT"}:
            bpy.data.objects.remove(obj, do_unlink=True)

    meshes = visible_meshes()
    minimum, maximum = world_bounds(meshes)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    height = max(size.y if size.y > size.z else size.z, 0.1)
    # Naraka source files use Z-up; keep a fallback for converted files.
    up_axis = 2 if size.z >= size.y else 1
    horizontal_axes = [index for index in range(3) if index != up_axis]
    width = max(size[horizontal_axes[0]], size[horizontal_axes[1]])

    camera_data = bpy.data.cameras.new("Preview Camera")
    camera_data.lens = 70.0
    camera = bpy.data.objects.new("Preview Camera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera

    up = Vector((0, 0, 1)) if up_axis == 2 else Vector((0, 1, 0))
    forward_a = Vector((0, -1, 0)) if up_axis == 2 else Vector((0, 0, 1))
    right = up.cross(forward_a).normalized()
    add_area_light("Key Light", center - forward_a * height + right * height * 0.45 + up * height * 0.65, center, 1150, (1.0, 0.93, 0.84), height)
    add_area_light("Fill Light", center - forward_a * height - right * height * 0.7 + up * height * 0.25, center, 650, (0.72, 0.82, 1.0), height)
    add_area_light("Rim Light", center + forward_a * height + up * height * 0.8, center, 900, (0.75, 0.85, 1.0), height * 0.8)

    full_distance = max(height * 1.32, width * 1.7)
    directions = [forward_a, right, -forward_a, -right]
    for yaw, direction in zip((0, 90, 180, 270), directions):
        render_view(
            scene,
            camera,
            center,
            direction,
            full_distance,
            os.path.join(output_dir, f"{output_name}_full_{yaw}.png"),
            720,
            960,
        )

    face_target = center.copy()
    face_target[up_axis] = minimum[up_axis] + height * 0.82
    face_distance = max(height * 0.48, width * 0.8)
    for yaw, direction in ((0, forward_a), (180, -forward_a)):
        render_view(
            scene,
            camera,
            face_target,
            direction,
            face_distance,
            os.path.join(output_dir, f"{output_name}_face_{yaw}.png"),
            720,
            720,
        )

    print(f"BLENDER_PREVIEWS={output_dir}")


if __name__ == "__main__":
    main()
