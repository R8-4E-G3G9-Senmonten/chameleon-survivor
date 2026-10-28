# 敵のアニメ付き .blend を、斜め前と真横の2方向から1周分レンダリングする(紹介用の動画づくり)。
# blender -b <名前>_anim.blend --python render_enemy_turn.py -- <出力フォルダ>
#   <出力フォルダ>/front_0000.png ... と side_0000.png ... ができる(1周 = アニメの frame_start〜frame_end-1)
import bpy, sys, math, os
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out, exist_ok=True)
scene = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        scene.render.engine = eng
        break
    except TypeError:
        pass
scene.render.resolution_x = scene.render.resolution_y = 640
scene.render.film_transparent = False
scene.render.image_settings.file_format = 'PNG'

# 背景: 夜の森っぽい暗い青
world = scene.world or bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs[0].default_value = (0.035, 0.05, 0.09, 1)
bg.inputs[1].default_value = 1.0

mesh = next(o for o in scene.objects if o.type == 'MESH')
scene.frame_set(scene.frame_start)
pts = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
center, size = (mn + mx) / 2, max(mx - mn)

# 地面(影を落として浮いているのが分かるように)
bpy.ops.mesh.primitive_circle_add(vertices=48, radius=size * 0.9, fill_type='NGON', location=(center.x, center.y, mn.z - size * 0.06))
ground = bpy.context.active_object
gm = bpy.data.materials.new("Ground")
gm.use_nodes = True
next(n for n in gm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs["Base Color"].default_value = (0.09, 0.14, 0.12, 1)
ground.data.materials.append(gm)

# 光: 上からの月明かりと、前からの弱い補助光
def add_light(kind, energy, rot, color):
    ld = bpy.data.lights.new(kind, kind)
    ld.energy = energy
    ld.color = color
    lo = bpy.data.objects.new(kind, ld)
    scene.collection.objects.link(lo)
    lo.rotation_euler = rot
    return lo
add_light('SUN', 3.2, (math.radians(40), 0, math.radians(30)), (0.85, 0.9, 1.0))
add_light('SUN', 1.2, (math.radians(75), 0, math.radians(200)), (0.6, 0.7, 1.0))

cd = bpy.data.cameras.new("Cam")
cd.lens = 50
cam = bpy.data.objects.new("Cam", cd)
scene.collection.objects.link(cam)
scene.camera = cam

def aim(direction, elev_deg, dist):
    d = direction.normalized()
    pos = center + Vector((d.x, d.y, 0)).normalized() * dist * math.cos(math.radians(elev_deg))
    pos.z = center.z + dist * math.sin(math.radians(elev_deg))
    cam.location = pos
    cam.rotation_euler = (center - pos).to_track_quat('-Z', 'Y').to_euler()

dist = size * 2.6
views = {
    "front": (Vector((math.sin(math.radians(35)), -math.cos(math.radians(35)), 0)), 12),  # 正面(-Y)から右へ35度
    "side": (Vector((1, 0, 0)), 8),
}
frames = range(scene.frame_start, scene.frame_end)  # 最後のコマは最初と同じなので除く
for name, (d, elev) in views.items():
    aim(d, elev, dist)
    for i, f in enumerate(frames):
        scene.frame_set(f)
        scene.render.filepath = os.path.join(out, "%s_%04d.png" % (name, i))
        bpy.ops.render.render(write_still=True)
print("RENDERED", out, len(frames))
