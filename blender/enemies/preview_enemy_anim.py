# blender -b anim.blend --python preview_anim.py -- out_prefix
# 横(+X)と正面(-Y)から、アニメの 0, 1/4, 1/2, 3/4 のコマを撮る
import bpy, sys, math
from mathutils import Vector
out = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = scene.render.resolution_y = 300
mesh = next(o for o in scene.objects if o.type == 'MESH')
pts = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
center, size = (mn + mx) / 2, max(mx - mn)
cd = bpy.data.cameras.new("C"); cd.type = 'ORTHO'; cd.ortho_scale = size * 1.3
cam = bpy.data.objects.new("C", cd); scene.collection.objects.link(cam); scene.camera = cam
N = scene.frame_end
for vname, d, rot in (("side", Vector((1, 0, 0)), (math.radians(90), 0, math.radians(90))),
                      ("front", Vector((0, -1, 0)), (math.radians(90), 0, 0))):
    cam.location = center + d * size * 3
    cam.rotation_euler = rot
    for k in range(4):
        scene.frame_set(int(round(N * k / 4)))
        scene.render.filepath = "%s_%s_%d.png" % (out, vname, k)
        bpy.ops.render.render(write_still=True)
