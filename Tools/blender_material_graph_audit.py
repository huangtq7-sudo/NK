import bpy
import json


materials = []
for material in bpy.data.materials:
    if not material.node_tree:
        continue
    image_nodes = {}
    for node in material.node_tree.nodes:
        if node.bl_idname == "ShaderNodeTexImage" and node.image:
            image_nodes[node.name] = node.image.name
    if not image_nodes:
        continue
    links = []
    for link in material.node_tree.links:
        links.append(
            {
                "from_node": link.from_node.name,
                "from_type": link.from_node.bl_idname,
                "from_image": image_nodes.get(link.from_node.name, ""),
                "from_socket": link.from_socket.name,
                "to_node": link.to_node.name,
                "to_type": link.to_node.bl_idname,
                "to_socket": link.to_socket.name,
            }
        )
    materials.append(
        {
            "material": material.name,
            "image_nodes": image_nodes,
            "links": links,
        }
    )

print(json.dumps(materials, ensure_ascii=False, indent=2))
