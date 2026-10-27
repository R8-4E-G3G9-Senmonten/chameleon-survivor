# Meshy のメッシュ1つだけの .blend に、自動で骨・重み・ループアニメを付けて FBX に書き出す。
# blender -b in.blend --python rig_enemy_ghosts.py -- <名前> <出力フォルダ> <保存する.blend>
#   例: blender -b Meshy_AI_Emberfox_..._texture.blend --python rig_enemy_ghosts.py -- Emberfox ../../Assets/Models/Enemies/Emberfox Emberfox_anim.blend
#   <名前> は下の P の表にあるもの。新しい敵を足すときは P に1行足す(手順は docs/enemies.md)。
# 前提: 上が +Z、正面が -Y(Meshy の書き出しのまま)。
import bpy, sys, math, os, json
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
NAME, OUTDIR, BLEND_OUT = args[0], args[1], args[2]

# ---- キャラごとの設定(寸法は Blender の単位。h は高さの割合 0..1) ----
# float: 宙に浮くお化け。arm_x より外側・arm_h の高さの範囲を腕(翼・炎)として動かす
# quad : 四足。leg_top より下で、前後の位置 leg_f ± leg_w を足とする
P = {
    "CrimsonSpiritBear": dict(type="float", faces=7000, arm_x=0.42, arm_h=(0.38, 0.80), shoulder_h=0.70, arm_mode="swing", arm_amp=16),
    "Emberfox":          dict(type="float", faces=6000, arm_x=0.30, arm_h=(0.45, 0.85), shoulder_h=0.66, arm_mode="flap", arm_amp=12),
    "MintySprite":       dict(type="float", faces=5000, arm_x=0.34, arm_h=(0.28, 0.85), shoulder_h=0.62, arm_mode="flap", arm_amp=24),
    "MoonfangSpecter":   dict(type="float", faces=8000, arm_x=0.33, arm_h=(0.15, 0.78), shoulder_h=0.72, arm_mode="swing", arm_amp=14),
    "SpectralKnight":    dict(type="float", faces=8000, arm_x=0.46, arm_h=(0.28, 0.80), shoulder_h=0.72, arm_mode="swing", arm_amp=12),
    "MoonlitFrostwolf":  dict(type="quad", faces=5000, leg_top=0.40, leg_f=(0.38, -0.24), leg_w=0.13, leg_x=0.10,
                              head_f=(0.50, 0.66), tail_f=(-0.62, -0.42), leg_amp=28),
    "PurpleTuskboar":    dict(type="quad", faces=5000, leg_top=0.24, leg_f=(0.42, -0.33), leg_w=0.17, leg_x=0.18,
                              head_f=(0.45, 0.62), tail_f=(-0.62, -0.42), leg_amp=22),
}[NAME]

scene = bpy.context.scene
scene.name = "Walk"  # FBX のアニメ名は「リグ名|シーン名」になる。Unity 側は "Walk" を含むクリップを探す
for o in list(scene.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o, do_unlink=True)
mesh_obj = next(o for o in scene.objects if o.type == 'MESH')
mesh_obj.name = NAME
mesh_obj.data.name = NAME
bpy.context.view_layer.objects.active = mesh_obj
for o in scene.objects: o.select_set(o == mesh_obj)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ---- 軽量化(ローポリの見た目なので面を減らしてフラットシェーディングに) ----
src_faces = len(mesh_obj.data.polygons)
dec = mesh_obj.modifiers.new("Dec", 'DECIMATE')
dec.ratio = min(1.0, P["faces"] / src_faces)
bpy.ops.object.modifier_apply(modifier=dec.name)
try:
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
except Exception as e:
    print("split normals:", e)
bpy.ops.object.shade_flat()
print("FACES", src_faces, "->", len(mesh_obj.data.polygons))

# ---- 色のテクスチャだけを書き出す(1024px) ----
mat = mesh_obj.data.materials[0]
bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
img = bsdf.inputs["Base Color"].links[0].from_node.image
img.scale(1024, 1024)
os.makedirs(OUTDIR, exist_ok=True)
tex_path = os.path.join(OUTDIR, NAME + "_texture_0.png")
img.filepath_raw = tex_path
img.file_format = 'PNG'
img.save()
mat.name = NAME

# ---- 寸法 ----
me = mesh_obj.data
co = np.empty(len(me.vertices) * 3, dtype=np.float64)
me.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
X, Y, Z = co[:, 0], co[:, 1], co[:, 2]
z0, H = Z.min(), Z.max() - Z.min()
h = (Z - z0) / H
F = -Y  # 正面がプラス

def ss(v, a, b):
    t = np.clip((v - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

def hat(v, nodes, i):
    # 区分線形の「山」: nodes[i] で1、隣の節で0
    w = np.zeros_like(v)
    c = nodes[i]
    if i > 0:
        a = nodes[i - 1]; m = (v >= a) & (v <= c); w[m] = (v[m] - a) / (c - a)
    else:
        w[v <= c] = 1
    if i < len(nodes) - 1:
        b = nodes[i + 1]; m = (v >= c) & (v <= b); w[m] = (b - v[m]) / (b - c)
    else:
        w[v >= c] = 1
    return w

# ---- 骨(全部 +Z 向き・ロール0 にそろえる: ローカルX=左右軸, Y=上下軸, Z=前後軸) ----
bones = {}   # name -> (位置, 親)
weights = {}
if P["type"] == "float":
    hn = [0.10, 0.32, 0.55, 0.75, 0.92]
    names = ["Tail2", "Tail1", "Body", "Chest", "Head"]
    pivots = {"Tail2": 0.22, "Tail1": 0.44, "Body": 0.55, "Chest": 0.66, "Head": 0.84}
    parent = {"Body": "Root", "Tail1": "Body", "Tail2": "Tail1", "Chest": "Body", "Head": "Chest"}
    cf = float(np.median(F))
    bones["Root"] = (Vector((0, -cf, z0)), None)
    for n in names:
        bones[n] = (Vector((0, -cf, z0 + pivots[n] * H)), parent[n])
    lo, hi = P["arm_h"]
    ax = P["arm_x"]
    band = ss(h, lo - 0.03, lo + 0.03) * (1 - ss(h, hi - 0.03, hi + 0.03))
    warm = ss(np.abs(X), ax - 0.02, ax + 0.05) * band
    wl = warm * (X > 0)
    wr = warm * (X < 0)
    bones["ArmL"] = (Vector((ax, -cf, z0 + P["shoulder_h"] * H)), "Chest")
    bones["ArmR"] = (Vector((-ax, -cf, z0 + P["shoulder_h"] * H)), "Chest")
    weights["ArmL"], weights["ArmR"] = wl, wr
    rest = 1 - warm
    for i, n in enumerate(names):
        weights[n] = hat(h, hn, i) * rest
else:
    ff, fb = P["leg_f"]
    lt = P["leg_top"]
    low = 1 - ss(h, lt - 0.07, lt + 0.02)
    lx = P["leg_x"]
    legs = {}
    for side, sgn in (("L", 1), ("R", -1)):
        sidew = ss(X * sgn, -0.015, 0.015)
        for fr, fc in (("F", ff), ("B", fb)):
            bandw = 1 - ss(np.abs(F - fc), P["leg_w"], P["leg_w"] + 0.06)
            legs["Leg" + fr + side] = low * bandw * sidew
            bones["Leg" + fr + side] = (Vector((lx * sgn, -fc, z0 + lt * H)), "Body")
    total = sum(legs.values())
    s = np.maximum(total, 1)
    for k in legs: legs[k] = legs[k] / s
    total = np.minimum(total, 1)
    weights.update(legs)
    rest = 1 - total
    wh = ss(F, *P["head_f"])
    wt = 1 - ss(F, *P["tail_f"])
    weights["Head"] = wh * rest
    weights["Tail"] = wt * rest
    weights["Body"] = np.clip(1 - wh - wt, 0, 1) * rest
    cf = 0.5 * (ff + fb)
    bones["Root"] = (Vector((0, -cf, z0)), None)
    bones["Body"] = (Vector((0, -cf, z0 + 0.6 * H)), "Root")
    bones["Head"] = (Vector((0, -P["head_f"][0], z0 + 0.62 * H)), "Body")
    bones["Tail"] = (Vector((0, -P["tail_f"][1], z0 + 0.62 * H)), "Body")

arm_data = bpy.data.armatures.new(NAME + "_Rig")
arm = bpy.data.objects.new(NAME + "_Rig", arm_data)
scene.collection.objects.link(arm)
for o in scene.objects: o.select_set(o == arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for n, (pos, par) in bones.items():
    b = arm_data.edit_bones.new(n)
    b.head = pos
    b.tail = pos + Vector((0, 0, 0.08 * H))
    b.roll = 0
    eb[n] = b
for n, (pos, par) in bones.items():
    if par: eb[n].parent = eb[par]
bpy.ops.object.mode_set(mode='OBJECT')

# 重みを正規化して頂点グループに入れる
names_w = list(weights.keys())
W = np.stack([weights[n] for n in names_w], axis=1)
sumw = W.sum(axis=1, keepdims=True)
W = np.where(sumw > 1e-6, W / np.maximum(sumw, 1e-6), 0)
fallback = "Body"
for j, n in enumerate(names_w):
    vg = mesh_obj.vertex_groups.new(name=n)
    for i in np.nonzero(W[:, j] > 0.002)[0]:
        vg.add([int(i)], float(W[i, j]), 'REPLACE')
vg_body = mesh_obj.vertex_groups[fallback]
for i in np.nonzero(sumw[:, 0] <= 1e-6)[0]:
    vg_body.add([int(i)], 1.0, 'REPLACE')
mesh_obj.parent = arm
mod = mesh_obj.modifiers.new("Armature", 'ARMATURE')
mod.object = arm

# ---- ループアニメーション ----
D = math.radians
pb = arm.pose.bones
for b in pb: b.rotation_mode = 'XYZ'
N = 36 if P["type"] == "float" else 24
scene.render.fps = 30
scene.frame_start, scene.frame_end = 0, N

def key(b, frame, rot=None, loc=None):
    if rot is not None:
        pb[b].rotation_euler = rot
        pb[b].keyframe_insert("rotation_euler", frame=frame)
    if loc is not None:
        pb[b].location = loc
        pb[b].keyframe_insert("location", frame=frame)

for i in range(N + 1):
    t = 2 * math.pi * i / N
    sn = math.sin
    if P["type"] == "float":
        # ローカル: X=左右軸(前後に傾く), Y=上下軸(ひねり), Z=前後軸(左右に揺れる)。位置の y が上下
        key("Root", i, rot=(0, 0, 0), loc=(0, 0.035 * H * sn(t), 0))
        key("Body", i, rot=(D(3) * sn(2 * t), D(3) * sn(t), D(4) * sn(t)))
        key("Tail1", i, rot=(D(8) + D(4) * sn(2 * t - 0.8), 0, D(10) * sn(t - 0.9)))
        key("Tail2", i, rot=(D(10) + D(6) * sn(2 * t - 1.6), 0, D(16) * sn(t - 1.8)))
        key("Chest", i, rot=(D(2) * sn(2 * t + 0.4), 0, -D(3) * sn(t)))
        key("Head", i, rot=(D(4) * sn(2 * t + 0.8), D(5) * sn(t + 0.5), D(2) * sn(t)))
        a = D(P["arm_amp"])
        if P["arm_mode"] == "swing":
            key("ArmL", i, rot=(a * sn(t), 0, 0))
            key("ArmR", i, rot=(-a * sn(t), 0, 0))
        else:
            key("ArmL", i, rot=(0, 0, a * sn(2 * t)))
            key("ArmR", i, rot=(0, 0, -a * sn(2 * t)))
    else:
        a = D(P["leg_amp"])
        key("LegFL", i, rot=(a * sn(t), 0, 0))
        key("LegBR", i, rot=(a * sn(t), 0, 0))
        key("LegFR", i, rot=(-a * sn(t), 0, 0))
        key("LegBL", i, rot=(-a * sn(t), 0, 0))
        key("Root", i, rot=(0, 0, 0), loc=(0, 0.02 * H * math.cos(2 * t), 0))
        key("Body", i, rot=(D(2) * sn(2 * t), 0, D(3) * sn(t)))
        key("Head", i, rot=(D(5) * sn(2 * t + 0.6), D(4) * sn(t), 0))
        key("Tail", i, rot=(D(6) * sn(2 * t), D(14) * sn(t), 0))

act = arm.animation_data.action
act.name = "Walk"
for fc in getattr(act, "fcurves", []):
    for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
scene.frame_set(0)

# ---- 書き出し ----
fbx_path = os.path.join(OUTDIR, NAME + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx_path, use_selection=False, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
    mesh_smooth_type='FACE', path_mode='STRIP', armature_nodetype='NULL',
    primary_bone_axis='Y', secondary_bone_axis='X')
img.filepath = tex_path
bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT, compress=True)
print("DONE", NAME, fbx_path, "bones", len(bones), "H=%.3f" % H)
