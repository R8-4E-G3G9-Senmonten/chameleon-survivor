# rig_enemy_ghosts.py で作った「骨+Walk」入りの .blend に、攻撃などの動きを足して FBX を書き出し直す。
# blender -b <名前>_anim.blend --python add_enemy_motions.py -- <名前> <出力フォルダ> <保存する.blend>
#   例: blender -b MoonlitFrostwolf_anim.blend --python add_enemy_motions.py -- MoonlitFrostwolf ../../Assets/Models/Enemies/MoonlitFrostwolf MoonlitFrostwolf_anim.blend
# 足す動き(Unity 側はクリップ名に "Attack" / "Shoot" を含むものを探す):
#   Attack: プレイヤーのすぐそばで繰り返す攻撃(ループ)。狼=噛みつき、熊=体をひねって右パンチ、妖精=体当たり
#   Shoot : 遠距離型が弾を撃つときの1回きりの動き(0.8秒。始まって約0.4秒で投げる)。狐=狐火を前へ投げる
# 骨の向き(rig_enemy_ghosts.py と同じ): 回転X のプラス=前に傾く、位置 y=上、位置 z のプラス=前へ。
import bpy, sys, math, os

args = sys.argv[sys.argv.index("--") + 1:]
NAME, OUTDIR, BLEND_OUT = args[0], args[1], args[2]

MOTIONS = {
    "MoonlitFrostwolf": ["Attack"],
    "CrimsonSpiritBear": ["Attack"],
    "MintySprite": ["Attack"],
    "Emberfox": ["Shoot"],
}[NAME]

scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in scene.objects if o.type == 'MESH')
pb = arm.pose.bones
walk = bpy.data.actions["Walk"]
walk.use_fake_user = True
H = mesh.dimensions.z

D = math.radians
sn = math.sin

def ss(v, a, b):
    t = min(1.0, max(0.0, (v - a) / (b - a)))
    return t * t * (3 - 2 * t)

def phases(p, wind_end, hit_end, rec_start):
    # u: ため(0→1→0)、s: 打ち込み(0→1→0)
    u = ss(p, 0, wind_end) * (1 - ss(p, wind_end, hit_end))
    s = ss(p, wind_end, hit_end) * (1 - ss(p, rec_start, 1.0))
    return u, s

def bear_attack(p):
    # ゆっくり体を横にひねって右腕を後ろへ引き、ひねりを戻しながら右パンチ。左腕は胸の前で構える
    # (熊は -Y 向きなので、熊から見て右腕は -X 側の ArmR。回転Y のマイナスで右肩が後ろへ回る)
    u, s = phases(p, 0.50, 0.62, 0.72)
    t = 2 * math.pi * p
    return {
        "Root": ((0, 0, 0), (0, 0.025 * H * sn(t), 0.06 * H * s)),
        "Body": ((D(-4 * u + 8 * s), D(-14 * u + 10 * s), 0), None),
        "Chest": ((D(-3 * u + 5 * s), D(-12 * u + 10 * s), 0), None),
        "Head": ((D(-3 * u + 4 * s), D(10 * u - 8 * s), 0), None),
        "ArmR": ((D(-15 * u - 5 * s), 0, 0), (0, 0.02 * H * u, -0.08 * H * u + 0.22 * H * s)),
        "ArmL": ((D(-10 * u + 6 * s), 0, 0), (0, 0, 0.03 * H * u - 0.04 * H * s)),
        "Tail1": ((D(8 + 6 * u - 4 * s), 0, D(8) * sn(t)), None),
        "Tail2": ((D(10 + 10 * u - 6 * s), 0, D(14) * sn(t - 1)), None),
    }

def sprite_attack(p):
    # 後ろに引いてから、羽を速くはばたかせて頭から体当たり
    u, s = phases(p, 0.35, 0.50, 0.60)
    flap = D(30) * sn(6 * math.pi * p)
    return {
        "Root": ((0, 0, 0), (0, 0.04 * H * u - 0.02 * H * s, -0.06 * H * u + 0.16 * H * s)),
        "Body": ((D(-12 * u + 24 * s), 0, 0), None),
        "Chest": ((D(-4 * u + 6 * s), 0, 0), None),
        "Head": ((D(-8 * u + 12 * s), 0, 0), None),
        "ArmL": ((0, D(20 * s), flap), None),
        "ArmR": ((0, D(-20 * s), -flap), None),
        "Tail1": ((D(10 + 16 * u - 10 * s), 0, D(10) * sn(2 * math.pi * p)), None),
        "Tail2": ((D(12 + 24 * u - 14 * s), 0, D(18) * sn(2 * math.pi * p - 1)), None),
    }

def fox_shoot(p):
    # 体をそらして両側の狐火を後ろへ引き、前へ投げ出す(1回きり。最初と最後は Walk の始めとほぼ同じ姿勢)
    u, s = phases(p, 0.40, 0.55, 0.62)
    fling = D(50 * u - 65 * s)
    return {
        "Root": ((0, 0, 0), (0, 0.04 * H * u, 0.07 * H * s)),
        "Body": ((D(-12 * u + 14 * s), 0, 0), None),
        "Chest": ((D(-6 * u + 6 * s), 0, 0), None),
        "Head": ((D(-12 * u + 12 * s), 0, 0), None),
        "ArmL": ((0, -fling, D(-14 * u)), None),
        "ArmR": ((0, fling, D(14 * u)), None),
        "Tail1": ((D(8 + 12 * u - 8 * s), 0, 0), None),
        "Tail2": ((D(10 + 20 * u - 12 * s), 0, 0), None),
    }

def wolf_attack(p):
    # 前足を浮かせて身をそらし、前へ飛び出しながら頭を振り下ろして噛みつく
    u, s = phases(p, 0.35, 0.50, 0.62)
    t = 2 * math.pi * p
    return {
        "Root": ((0, 0, 0), (0, 0.05 * H * u, -0.05 * H * u + 0.14 * H * s)),
        "Body": ((D(-10 * u + 8 * s), 0, 0), None),
        "Head": ((D(-18 * u + 28 * s), 0, 0), None),
        "LegFL": ((D(-38 * u + 14 * s), 0, 0), None),
        "LegFR": ((D(-30 * u + 18 * s), 0, 0), None),
        "LegBL": ((D(16 * u - 6 * s), 0, 0), None),
        "LegBR": ((D(20 * u - 6 * s), 0, 0), None),
        "Tail": ((D(10 * u), D(20) * sn(t), 0), None),
    }

DEFS = {  # (作り方, コマ数(30fps), ループするか)
    ("CrimsonSpiritBear", "Attack"): (bear_attack, 36, True),
    ("MintySprite", "Attack"): (sprite_attack, 18, True),
    ("MoonlitFrostwolf", "Attack"): (wolf_attack, 18, True),
    ("Emberfox", "Shoot"): (fox_shoot, 24, False),
}

# ---- 重みの手直し ----
# 熊の手先と爪は体のずっと前に突き出ていて、rig_enemy_ghosts.py の「左右の外側=腕」の判定から外れ、
# 胸の骨に付いたままだった(腕を動かすと爪だけ置き去りになる)。前に突き出た部分を腕の骨に付け直す。
#   fwd: 体の中心から前へどれだけ出ているか(高さ H に対する割合)で、この範囲より前を腕にする
#   h  : 腕にする高さの範囲(0=足元, 1=頭のてっぺん)。顔(鼻先)は含めない
FIX_ARMS = {
    "CrimsonSpiritBear": dict(fwd=(0.18, 0.22), h=(0.45, 0.78), xmin=0.10),
}.get(NAME)
if FIX_ARMS:
    import numpy as np
    me = mesh.data
    co = np.array([v.co[:] for v in me.vertices])
    X, F, Z = co[:, 0], -co[:, 1], co[:, 2]
    h = (Z - Z.min()) / H
    cf = float(np.median(F))
    lo, hi = FIX_ARMS["fwd"]
    w = np.clip(((F - cf) / H - lo) / (hi - lo), 0, 1)
    w = w * w * (3 - 2 * w)
    hl, hh = FIX_ARMS["h"]
    w *= (h >= hl) & (h <= hh) & (np.abs(X) >= FIX_ARMS["xmin"] * H)
    groups = {g.index: g for g in mesh.vertex_groups}
    armL, armR = mesh.vertex_groups["ArmL"], mesh.vertex_groups["ArmR"]
    n = 0
    for v in me.vertices:
        wi = float(w[v.index])
        if wi <= 0.001: continue
        side = armL if X[v.index] > 0 else armR
        cur = {groups[g.group].name: g.weight for g in v.groups}
        arm_now = cur.get(side.name, 0.0)
        target = max(arm_now, wi)
        rest = 1.0 - arm_now
        k = (1.0 - target) / rest if rest > 1e-6 else 0.0
        for g in v.groups:
            if g.group != side.index: g.weight *= k
        side.add([v.index], target, 'REPLACE')
        n += 1
    print("FIXED ARM WEIGHTS", NAME, n, "verts")

for b in pb: b.rotation_mode = 'XYZ'
for mname in MOTIONS:
    old = bpy.data.actions.get(mname)
    if old: bpy.data.actions.remove(old)
    fn, N, loop = DEFS[(NAME, mname)]
    act = bpy.data.actions.new(mname)
    act.use_fake_user = True
    arm.animation_data.action = act
    for i in range(N + 1):
        # ループの動きは最後のコマ = 最初のコマ。1回きりの動きは最後で 1.0(元の姿勢)に着く
        p = i / N
        pose = fn(p)
        for b in pb:
            rot, loc = pose.get(b.name, ((0, 0, 0), None))
            b.rotation_euler = rot
            b.keyframe_insert("rotation_euler", frame=i)
            b.location = loc if loc is not None else (0, 0, 0)
            b.keyframe_insert("location", frame=i)
    act.frame_range = (0, N)
    print("MOTION", NAME, mname, "frames", N, "loop", loop)

arm.animation_data.action = walk
scene.frame_start, scene.frame_end = int(walk.frame_range[0]), int(walk.frame_range[1])
scene.frame_set(0)

fbx_path = os.path.join(OUTDIR, NAME + ".fbx")
for o in scene.objects: o.select_set(o in (arm, mesh))
bpy.ops.export_scene.fbx(
    filepath=fbx_path, use_selection=False, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
    mesh_smooth_type='FACE', path_mode='STRIP', armature_nodetype='NULL',
    primary_bone_axis='Y', secondary_bone_axis='X')
bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT, compress=True)
print("DONE", NAME, fbx_path, [a.name for a in bpy.data.actions])
