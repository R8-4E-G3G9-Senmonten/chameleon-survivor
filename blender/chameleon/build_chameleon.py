"""
参考画像(カラーパレット付きのモデルシート)に近づけて作り直したカメレオン。
画像・外部テクスチャは使わず、メッシュ形状とプロシージャルなマテリアル(Color Ramp)だけで
色のグラデーション(頭〜背中=緑、お腹=タン、しっぽ=オレンジ+縞)を再現する。

v2の変更点(参考画像とかけ離れていた点を修正):
  - 体と頭を「ひとつの丸い塊」にせず、体・首・頭を別プロファイルにして輪郭にメリハリを付けた
  - トゲを頭頂からしっぽの先端まで"途切れず"連続させ、サイズも連続的に減衰させた
  - 脚を「腿+すね」の2関節にして、トカゲらしく曲がった脚にした
  - しっぽは体と同じ太さから始まり、先端までなめらかに先細りするようにした
  - 全体をなめらかなPBR風シェーディング(粗さを下げる)にした

実行方法:
  "blender.exe" --background --python build_chameleon.py -- [--render] [--export]
  (GUIで開いたまま実行する場合は --background を外す)

座標系: X=前方(鼻の方向) / Y=左右 / Z=上。書き出し時にUnity向け(Y-up, -Z forward)に変換する。
"""

import bpy
import bmesh
import math
import sys
import os
import mathutils

# ---------------------------------------------------------------------------
# 引数処理
# ---------------------------------------------------------------------------
argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
DO_RENDER = "--render" in argv
DO_EXPORT = "--export" in argv

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FBX = os.path.join(HERE, "..", "..", "Assets", "Models", "Chameleon", "chameleon.fbx")  # blender/chameleon/ から見たプロジェクト
OUT_BLEND = os.path.join(HERE, "chameleon.blend")
OUT_PREVIEW_DIR = os.path.join(HERE, "preview")


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for block_collection in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.lights, bpy.data.cameras, bpy.data.images):
        for block in list(block_collection):
            block_collection.remove(block)


# ---------------------------------------------------------------------------
# 汎用ヘルパー
# ---------------------------------------------------------------------------
def lathe_mesh(name, profile, segments=16):
    """profile: [(x, radius, z_offset), ...] を X 軸まわりに回転させて滑らかな塊(胴体・頭など)を作る。"""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()

    ring_verts = []
    for (x, r, zoff) in profile:
        if r <= 0.0001:
            ring_verts.append([bm.verts.new((x, 0.0, zoff))])
            continue
        ring = []
        for i in range(segments):
            a = (i / segments) * math.tau
            y = math.sin(a) * r
            z = math.cos(a) * r + zoff
            ring.append(bm.verts.new((x, y, z)))
        ring_verts.append(ring)

    for i in range(len(ring_verts) - 1):
        a = ring_verts[i]
        b = ring_verts[i + 1]
        if len(a) == 1 and len(b) > 1:
            for j in range(segments):
                bm.faces.new([a[0], b[j], b[(j + 1) % segments]])
        elif len(b) == 1 and len(a) > 1:
            for j in range(segments):
                bm.faces.new([a[j], a[(j + 1) % segments], b[0]])
        elif len(a) > 1 and len(b) > 1:
            for j in range(segments):
                j2 = (j + 1) % segments
                bm.faces.new([a[j], b[j], b[j2], a[j2]])

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def add_shrinkwrap_smooth(obj, levels=1):
    mod = obj.modifiers.new("Subsurf", 'SUBSURF')
    mod.levels = levels
    mod.render_levels = levels + 1


def add_sphere(name, radius, loc, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=loc, segments=14, ring_count=10)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    return obj


def add_cone(name, radius1, radius2, depth, loc, rot=(0, 0, 0), segs=8):
    bpy.ops.mesh.primitive_cone_add(radius1=radius1, radius2=radius2, depth=depth, location=loc,
                                     rotation=rot, vertices=segs)
    obj = bpy.context.object
    obj.name = name
    return obj


def add_capsule_between(name, p0, p1, r0, r1, segs=8):
    """p0→p1 を結ぶ、太さが変化する円柱(脚の腿・すねなど)。"""
    v0, v1 = mathutils.Vector(p0), mathutils.Vector(p1)
    length = (v1 - v0).length
    mid = (v0 + v1) / 2
    rot = (v1 - v0).to_track_quat('Z', 'Y').to_euler()
    return add_cone(name, radius1=r0, radius2=r1, depth=length, loc=mid, rot=rot, segs=segs)


def find_bsdf(mat):
    for n in mat.node_tree.nodes:
        if n.bl_idname == "ShaderNodeBsdfPrincipled":
            return n
    raise RuntimeError("Principled BSDF node not found on material " + mat.name)


def add_material(name, base_color, roughness=0.4):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = find_bsdf(mat)
    bsdf.inputs["Base Color"].default_value = (*base_color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def build_body_gradient_material(name, top_color, belly_color, mid_color=None):
    """背中(緑)→腹(タン)へ、Zの高さでなめらかに変化するグラデーション。参考画像は側面にもう一段
    明るい緑の帯があるので、3点のColor Rampで表現する。"""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nodes, links = nt.nodes, nt.links
    bsdf = find_bsdf(mat)

    tex_coord = nodes.new("ShaderNodeTexCoord")
    ramp = nodes.new("ShaderNodeValToRGB")
    els = ramp.color_ramp.elements
    els[0].position = 0.28
    els[0].color = (*belly_color, 1)
    if mid_color is not None:
        mid_el = els.new(0.55)
        mid_el.color = (*mid_color, 1)
    els[1].position = 0.85
    els[1].color = (*top_color, 1)

    sep = nodes.new("ShaderNodeSeparateXYZ")
    links.new(tex_coord.outputs["Object"], sep.inputs["Vector"])
    links.new(sep.outputs["Z"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.38
    return mat


def build_tail_material(name, root_color, tip_color, stripe_color, x_min, x_max):
    """しっぽ用: 根本(緑)→先端(オレンジ)のグラデーションに、輪切りの縞模様を重ねる。
    x_min/x_max はしっぽの実際のX範囲(オブジェクト座標)。テクスチャ座標を実寸にスケールして
    帯の間隔をどの長さのしっぽでも均一に保つ。"""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nodes, links = nt.nodes, nt.links
    bsdf = find_bsdf(mat)

    tex_coord = nodes.new("ShaderNodeTexCoord")
    sep = nodes.new("ShaderNodeSeparateXYZ")
    links.new(tex_coord.outputs["Object"], sep.inputs["Vector"])

    mapv = nodes.new("ShaderNodeMapRange")
    mapv.inputs["From Min"].default_value = x_min
    mapv.inputs["From Max"].default_value = x_max
    mapv.inputs["To Min"].default_value = 0.0
    mapv.inputs["To Max"].default_value = 1.0
    links.new(sep.outputs["X"], mapv.inputs["Value"])

    grad_ramp = nodes.new("ShaderNodeValToRGB")
    grad_ramp.color_ramp.elements[0].position = 0.0
    grad_ramp.color_ramp.elements[0].color = (*tip_color, 1)
    grad_ramp.color_ramp.elements[1].position = 1.0
    grad_ramp.color_ramp.elements[1].color = (*root_color, 1)
    links.new(mapv.outputs["Result"], grad_ramp.inputs["Fac"])

    wave = nodes.new("ShaderNodeTexWave")
    wave.wave_type = 'BANDS'
    wave.inputs["Scale"].default_value = 6.0
    wave.inputs["Distortion"].default_value = 0.0
    links.new(tex_coord.outputs["Object"], wave.inputs["Vector"])

    stripe_ramp = nodes.new("ShaderNodeValToRGB")
    stripe_ramp.color_ramp.elements[0].position = 0.42
    stripe_ramp.color_ramp.elements[0].color = (1, 1, 1, 1)
    stripe_ramp.color_ramp.elements[1].position = 0.58
    stripe_ramp.color_ramp.elements[1].color = (0, 0, 0, 1)
    links.new(wave.outputs["Color"], stripe_ramp.inputs["Fac"])

    mix = nodes.new("ShaderNodeMixRGB")
    mix.blend_type = 'MULTIPLY'
    mix.inputs["Color2"].default_value = (*stripe_color, 1)
    links.new(grad_ramp.outputs["Color"], mix.inputs["Color1"])
    links.new(stripe_ramp.outputs["Color"], mix.inputs["Fac"])

    links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.38
    return mat


def join_all(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    return result


# ---------------------------------------------------------------------------
# パレット(参考画像のカラーパレットに合わせる: 濃緑/黄緑/タン/オレンジ/こげ茶)
# ---------------------------------------------------------------------------
COL_GREEN_DARK = (0.176, 0.412, 0.176)
COL_GREEN_MID = (0.376, 0.612, 0.259)
COL_TAN = (0.804, 0.769, 0.549)
COL_ORANGE = (0.816, 0.522, 0.243)
COL_BROWN = (0.522, 0.353, 0.216)
COL_SPIKE = (0.129, 0.259, 0.129)
COL_WHITE = (0.95, 0.95, 0.93)
COL_BLACK = (0.03, 0.03, 0.03)


def build_chameleon():
    clear_scene()

    body_mat = build_body_gradient_material("Body", COL_GREEN_DARK, COL_TAN, COL_GREEN_MID)
    tail_mat = build_tail_material("Tail", COL_GREEN_DARK, COL_ORANGE, COL_BROWN, x_min=-3.25, x_max=0.55)
    spike_mat = add_material("Spikes", COL_SPIKE, roughness=0.35)
    foot_mat = add_material("Feet", COL_GREEN_MID, roughness=0.45)
    eye_white_mat = add_material("EyeWhite", COL_WHITE, roughness=0.1)
    eye_black_mat = add_material("EyePupil", COL_BLACK, roughness=0.05)

    parts = []

    # --- 体(しっぽの付け根〜前脚の後ろあたりまで。参考画像はやや細長い樽型) ---
    body_profile = [
        (0.05, 0.40, 0.02),   # 後端。しっぽの付け根と同じ太さに合わせる
        (0.35, 0.58, 0.03),
        (0.75, 0.64, 0.04),   # 体で一番太い部分
        (1.15, 0.58, 0.03),
        (1.45, 0.46, 0.02),   # 首の付け根に向けて少し細くなる
    ]
    # --- 首・頭は体と同じプロファイルに連結し、1つの滑らかな塊として作る(継ぎ目をなくす) ---
    neck_profile = [
        (1.45, 0.46, 0.02),
        (1.65, 0.40, 0.04),
    ]
    head_profile = [
        (1.85, 0.46, 0.06),
        (2.00, 0.58, 0.18),   # 後頭部が盛り上がる(カスク)
        (2.22, 0.58, 0.24),   # カスクの頂点付近
        (2.50, 0.50, 0.16),
        (2.78, 0.38, 0.09),   # 鼻先に向けて細くなる
        (3.00, 0.16, 0.04),
        (3.12, 0.02, 0.02),
    ]
    body_head_profile = body_profile + neck_profile + head_profile
    body = lathe_mesh("BodyHead", body_head_profile, segments=16)
    body.data.materials.append(body_mat)
    add_shrinkwrap_smooth(body, levels=2)
    parts.append(body)

    # --- 背中〜しっぽのトゲ(頭頂からしっぽの先まで"途切れず"連続。サイズも連続的に減衰) ---
    def spine_point(t):
        """t=0(頭頂)→t=1(しっぽの先端) の背中のライン(x, z)。"""
        if t <= 0.28:
            u = t / 0.28
            x = 2.22 - u * (2.22 - 1.65)
            z = 0.78 - u * (0.78 - 0.52)
        elif t <= 0.55:
            u = (t - 0.28) / (0.55 - 0.28)
            x = 1.65 - u * (1.65 - 0.75)
            z = 0.52 + u * (0.78 - 0.52)
        elif t <= 0.70:
            u = (t - 0.55) / (0.70 - 0.55)
            x = 0.75 - u * 0.70
            z = 0.78 - u * (0.78 - 0.60)
        else:
            u = (t - 0.70) / (1 - 0.70)
            x = 0.05 - u * 3.3
            z = 0.60 - u * 0.66
        return x, z

    spike_objs = []
    n_spikes = 34
    for i in range(n_spikes):
        t = i / (n_spikes - 1)
        x, z = spine_point(t)
        size = max(0.16 * (1 - t) ** 1.4, 0.02)
        s = add_cone(f"Spike_{i}", radius1=size, radius2=0.0, depth=size * 2.2,
                     loc=(x, 0, z + size * 0.5), rot=(0, -math.radians(18) - t * math.radians(30), 0), segs=4)
        spike_objs.append(s)
    spikes = join_all(spike_objs, "Spikes")
    spikes.data.materials.append(spike_mat)
    parts.append(spikes)

    # --- しっぽ(体と同じ太さから始まり、なめらかに先細りしながらゆるく下にカール) ---
    tail_len = 3.3
    tail_root_r = 0.40  # body_profile の後端(x=0.05)と同じ太さに合わせて段差をなくす
    tail_profile = [(0.05, tail_root_r, 0.02)]
    n_tail_segs = 26
    for i in range(1, n_tail_segs + 1):
        t = i / n_tail_segs
        x = 0.05 - t * tail_len
        curl = t * t * 1.6
        z = -curl + 0.02
        r = max(tail_root_r * (1 - t) ** 2.0, 0.02)
        tail_profile.append((x, r, z))
    tail = lathe_mesh("Tail", tail_profile, segments=14)
    tail.data.materials.append(tail_mat)
    add_shrinkwrap_smooth(tail, levels=2)
    parts.append(tail)

    # --- 目(大きく丸い白目+黒目、頭の側面から飛び出す) ---
    eye_x, eye_z = 2.30, 0.42
    for side in (-1, 1):
        white = add_sphere(f"EyeWhite_{side}", 0.26, (eye_x, side * 0.46, eye_z), scale=(1.0, 0.9, 1.0))
        white.data.materials.append(eye_white_mat)
        parts.append(white)
        pupil = add_sphere(f"EyePupil_{side}", 0.13, (eye_x + 0.16, side * 0.55, eye_z + 0.02), scale=(0.8, 0.8, 0.8))
        pupil.data.materials.append(eye_black_mat)
        parts.append(pupil)

    # --- 脚(腿+すねの2関節。トカゲらしく肘を外側に張り出させる) ---
    leg_defs = [("FL", 1.10), ("BL", 0.20)]  # 肩付近と腰付近にはっきり分ける
    for name, leg_x in leg_defs:
        for side in (-1, 1):
            hip = (leg_x, side * 0.42, -0.10)
            knee = (leg_x + 0.20, side * 0.62, -0.42)
            ankle = (leg_x - 0.05, side * 0.58, -0.80)

            thigh = add_capsule_between(f"Thigh_{name}_{side}", hip, knee, 0.22, 0.16, segs=8)
            thigh.data.materials.append(foot_mat)
            parts.append(thigh)

            shin = add_capsule_between(f"Shin_{name}_{side}", knee, ankle, 0.16, 0.10, segs=8)
            shin.data.materials.append(foot_mat)
            parts.append(shin)

            foot_base = (ankle[0] + 0.08, ankle[1] + side * 0.02, ankle[2] - 0.05)
            foot = add_sphere(f"Foot_{name}_{side}", 0.15, foot_base, scale=(1.2, 1.0, 0.6))
            foot.data.materials.append(foot_mat)
            parts.append(foot)

            for toe_i in range(3):
                spread = (toe_i - 1) * 0.32
                toe = add_cone(f"Toe_{name}_{side}_{toe_i}", radius1=0.06, radius2=0.015, depth=0.32,
                                loc=(foot_base[0] + 0.18, foot_base[1] + spread, foot_base[2] - 0.01),
                                rot=(math.radians(100), 0, spread * 1.5), segs=6)
                toe.data.materials.append(foot_mat)
                parts.append(toe)

    chameleon = join_all(parts, "Chameleon")
    bpy.ops.object.shade_smooth()
    return chameleon


# ---------------------------------------------------------------------------
# プレビュー撮影
# ---------------------------------------------------------------------------
def setup_render_scene(target):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    bg = next(n for n in scene.world.node_tree.nodes if n.bl_idname == "ShaderNodeBackground")
    bg.inputs[0].default_value = (0.08, 0.09, 0.10, 1.0)
    bg.inputs[1].default_value = 1.0

    light_data = bpy.data.lights.new("KeyLight", type='SUN')
    light_data.energy = 3.0
    light_obj = bpy.data.objects.new("KeyLight", light_data)
    bpy.context.collection.objects.link(light_obj)
    light_obj.rotation_euler = (math.radians(55), 0, math.radians(35))

    fill_data = bpy.data.lights.new("FillLight", type='SUN')
    fill_data.energy = 1.2
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    bpy.context.collection.objects.link(fill_obj)
    fill_obj.rotation_euler = (math.radians(60), 0, math.radians(-120))

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 60
    cam_obj = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    return cam_obj


def point_camera(cam_obj, location, target=(0.3, 0, 0.2)):
    cam_obj.location = location
    direction = mathutils.Vector(target) - mathutils.Vector(location)
    cam_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()


def render_previews(cam_obj):
    os.makedirs(OUT_PREVIEW_DIR, exist_ok=True)
    shots = {
        "front": (7.5, 0.0, 0.6),
        "side": (0.3, -7.8, 0.6),
        "back": (-7.5, 0.0, 0.6),
        "iso": (5.6, -5.6, 3.0),
    }
    for name, loc in shots.items():
        point_camera(cam_obj, loc)
        bpy.context.scene.render.filepath = os.path.join(OUT_PREVIEW_DIR, f"{name}.png")
        bpy.ops.render.render(write_still=True)


def bake_gradient_materials(obj):
    """Body/Tail はプロシージャルなグラデーションで色を作っているが、FBXはノードグラフを保持
    できずベースカラーの画像しか引き継げない。UV展開してこの2マテリアルだけ画像に焼き付ける。"""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')

    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32

    for mat_name in ("Body", "Tail"):
        mat = bpy.data.materials.get(mat_name)
        if mat is None:
            continue
        nt = mat.node_tree
        bsdf = find_bsdf(mat)

        img = bpy.data.images.new(f"{mat_name}_Bake", width=1024, height=1024)
        img_node = nt.nodes.new("ShaderNodeTexImage")
        img_node.image = img
        nt.nodes.active = img_node

        bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=8)

        img.filepath_raw = os.path.join(os.path.dirname(OUT_FBX), f"{mat_name}_Bake.png")
        img.file_format = 'PNG'
        img.save()

        nt.links.new(img_node.outputs["Color"], bsdf.inputs["Base Color"])

    scene.render.engine = 'BLENDER_EEVEE'


def export_fbx(target):
    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        object_types={'MESH'},
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
    )


def main():
    chameleon = build_chameleon()
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)

    if DO_RENDER:
        cam = setup_render_scene(chameleon)
        render_previews(cam)

    if DO_EXPORT:
        bake_gradient_materials(chameleon)
        export_fbx(chameleon)

    print("DONE: verts=%d" % len(chameleon.data.vertices))


main()
