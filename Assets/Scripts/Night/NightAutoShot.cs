using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// 動作確認用の自動撮影。exe を「-nightAutoShot <保存先フォルダ>」付きで起動した時だけ動き、
// タイトル画面(各マップ)・プレイ中・敵への照射・擬態・蛍の石・ランタン・閃光・結果画面を順に撮影して終了する。
// 通常の起動では何もしない。
public class NightAutoShot : MonoBehaviour
{
    string dir;
    NightGame g;

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-nightAutoShot");
        if (i >= 0) Application.runInBackground = true; // 撮影はウィンドウが裏にあっても進める
        if (i < 0 || i + 1 >= args.Length) { enabled = false; return; }
        dir = args[i + 1];
        Directory.CreateDirectory(dir);
        g = NightGame.Instance;
        int m = System.Array.IndexOf(args, "-nightMap");
        if (System.Array.IndexOf(args, "-nightDemoVideo") >= 0) { enabled = false; return; } // 紹介動画の撮影は NightDemoVideo が行う
        if (System.Array.IndexOf(args, "-nightHealTest") >= 0) { StartCoroutine(HealTest()); return; }
        if (System.Array.IndexOf(args, "-nightCamoTest") >= 0) { StartCoroutine(CamoTest()); return; }
        if (System.Array.IndexOf(args, "-nightGaugeTest") >= 0) { StartCoroutine(GaugeTest()); return; }
        if (System.Array.IndexOf(args, "-nightEnemyGallery") >= 0) { StartCoroutine(EnemyGallery()); return; }
        if (System.Array.IndexOf(args, "-nightPropPhotos") >= 0) { StartCoroutine(PropPhotos()); return; }
        if (System.Array.IndexOf(args, "-nightSpriteTest") >= 0) { StartCoroutine(SpriteTest()); return; }
        if (System.Array.IndexOf(args, "-nightBench") >= 0) { StartCoroutine(Bench()); return; }
        if (System.Array.IndexOf(args, "-nightEndNameTest") >= 0) { StartCoroutine(EndNameTest()); return; }
        StartCoroutine(m >= 0 && m + 1 < args.Length ? Tour(args[m + 1]) : Run());
    }

    // 結果画面での名前入力の確認: 名前なしで始めて終わらせ、名前入力の画面 → 登録後の画面を撮る(テスト用のランキングに入る)
    IEnumerator EndNameTest()
    {
        yield return new WaitForSeconds(1f);
        g.StartGame();
        yield return new WaitForSeconds(1.2f);
        yield return Shot("countdown");
        yield return new WaitForSeconds(3f);
        g.SendMessage("End", true);
        yield return new WaitForSeconds(6f);
        yield return Shot("end_name_prompt");
        g.SubmitRanking("テスト名");
        yield return new WaitForSeconds(1.5f);
        yield return Shot("end_name_done");
        Application.Quit();
    }

    // 重さの計測: マップCで敵を大量に出し、光を当て続けた時と雷の時のフレーム時間を bench.txt に書く(軽量化の前後比較用)。
    // 垂直同期を切って、1フレームにかかる時間そのものを測る。「-benchNoAim」を付けると自動で照らさず、スマホの光だけになる。
    IEnumerator Bench()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Random.InitState(1234);
        bool aim = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchNoAim") < 0;
        yield return new WaitForSeconds(1f);
        g.SelectMap("C");
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        yield return new WaitForSeconds(1f);
        var report = new StringBuilder();
        foreach (int n in new[] { 30, 70 })
        {
            g.DebugSpawnNear(n - g.Enemies.Count, 50f); // 倒れないよう硬くして、数をそろえる
            yield return Measure("敵" + n + "体・光を当てる", 5f, aim, report);
            g.DebugFlashIn(0.5f);
            yield return Measure("敵" + n + "体・雷", 2.5f, aim, report);
        }
        File.WriteAllText(Path.Combine(dir, "bench.txt"), report.ToString());
        Debug.Log("NightAutoShot: bench\n" + report);
        Application.Quit();
    }

    IEnumerator Measure(string label, float seconds, bool aim, StringBuilder report)
    {
        var times = new System.Collections.Generic.List<float>();
        float t = 0f;
        while (t < seconds)
        {
            g.DebugSetHp(999f);
            // 擬態しないよう、主人公を小さく回らせ続ける
            NightPlayer.DebugInput = new Vector3(Mathf.Cos(Time.time * 2f), 0, Mathf.Sin(Time.time * 2f)) * 0.6f;
            if (aim && g.Enemies.Count > 0)
            {
                Vector3 v = g.Cam.WorldToViewportPoint(g.Enemies[(int)(Time.time * 2f) % g.Enemies.Count].Position);
                g.Flashlight.DebugAim = new Vector2(v.x, 1f - v.y);
            }
            yield return null;
            times.Add(Time.unscaledDeltaTime * 1000f);
            t += Time.unscaledDeltaTime;
        }
        times.Sort();
        float avg = 0f; foreach (var x in times) avg += x; avg /= times.Count;
        report.AppendLine(label + ": 平均 " + avg.ToString("0.0") + "ms (" + (1000f / avg).ToString("0") + "fps)  遅い5% " +
            times[(int)(times.Count * 0.95f)].ToString("0.0") + "ms  最大 " + times[times.Count - 1].ToString("0.0") + "ms  敵 " + g.Enemies.Count);
    }

    // 指定マップを開始し、広場を順に巡って撮影する(階段・橋・湖などの地形確認用)
    IEnumerator Tour(string id)
    {
        yield return new WaitForSeconds(1f);
        g.SelectMap(id);
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        yield return new WaitForSeconds(1.5f);
        yield return Shot(id + "_00_start");
        var regions = g.Map.Regions;
        for (int r = 1; r < regions.Length; r++)
        {
            var reg = regions[r];
            Teleport(new Vector3(reg.x, 0, reg.z));
            g.Flashlight.DebugAim = new Vector2(0.5f, 0.3f);
            yield return new WaitForSeconds(1.6f);
            yield return Shot(id + "_" + r.ToString("00") + "_region");
        }
        Application.Quit();
    }

    // 光で動くもののゲージの確認: ランタンを少し照らして外す → 減らずに残るか、もう一度照らすと続きから貯まるか
    IEnumerator GaugeTest()
    {
        yield return new WaitForSeconds(1f);
        g.SelectMap("A");
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        yield return new WaitForSeconds(0.5f);
        // いちばん近いランタンと花の近くへ移動
        NightLamp lamp = null; float best = float.MaxValue;
        foreach (var l in g.World.Lamps) { float d = Mathf.Abs(l.x) + Mathf.Abs(l.z - g.PlayerPos.z); if (d < best) { best = d; lamp = l; } }
        Teleport(new Vector3(lamp.x + 2.5f, 0, lamp.z + 2.5f));
        yield return new WaitForSeconds(0.8f);
        Vector3 lampPos = new Vector3(lamp.x, lamp.height, lamp.z);
        yield return AimAt(() => lampPos, 0.2f);
        g.Flashlight.DebugAim = new Vector2(0.95f, 0.95f); // 光を外す
        yield return new WaitForSeconds(1.0f);
        Debug.Log("NightAutoShot: lamp charge after release=" + lamp.charge.ToString("0.00") + " (外しても残っていればOK)");
        yield return Shot("gauge_01_lamp_partial");
        yield return AimAt(() => lampPos, 0.15f);
        g.Flashlight.DebugAim = new Vector2(0.95f, 0.95f);
        yield return new WaitForSeconds(0.1f);
        Debug.Log("NightAutoShot: lamp charge after 2nd=" + lamp.charge.ToString("0.00"));
        yield return Shot("gauge_02_lamp_more");
        Application.Quit();
    }

    // 擬態の確認: 止まると敵が見失って「？」→ 3秒で擬態が解けて「！」
    IEnumerator CamoTest()
    {
        yield return new WaitForSeconds(1f);
        g.SelectMap("A");
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        g.Flashlight.DebugAim = new Vector2(0.9f, 0.1f); // 敵に光を当てない
        yield return new WaitForSeconds(0.2f);
        g.DebugSpawnNear(5);
        yield return new WaitForSeconds(0.7f);
        yield return Shot("camo_01_confused");
        yield return new WaitForSeconds(1.6f);
        yield return Shot("camo_02_low");
        Debug.Log("NightAutoShot: camo=" + g.Camouflaged + " camp=" + g.Camp);
        while (g.Camouflaged) yield return null;
        yield return new WaitForSeconds(0.15f);
        yield return Shot("camo_03_found");
        Debug.Log("NightAutoShot: after camo=" + g.Camouflaged + " hp=" + g.Hp);
        Application.Quit();
    }

    // 回復オーブと「敵が増えた！」の警告の確認
    IEnumerator HealTest()
    {
        yield return new WaitForSeconds(1f);
        g.SelectMap("B");
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        yield return new WaitForSeconds(1.5f);
        g.DebugHealTest();
        yield return new WaitForSeconds(0.6f);
        yield return Shot("heal_01_orbs");
        // 一番近いオーブへ歩かせる代わりに、オーブのある場所へ移動して拾わせる
        Teleport(g.PlayerPos + new Vector3(3.2f, 0, 0));
        yield return new WaitForSeconds(0.35f);
        yield return Shot("heal_02_pickup");
        Debug.Log("NightAutoShot: heal hp=" + g.Hp + " healed=" + g.HealedTotal + " threat=" + g.Threat);
        g.DebugRaiseThreat();
        yield return new WaitForSeconds(0.25f);
        yield return Shot("heal_03_alert_in");
        yield return new WaitForSeconds(0.8f);
        yield return Shot("heal_04_alert");
        Debug.Log("NightAutoShot: threat=" + g.Threat + " enemies=" + g.Enemies.Count);
        yield return new WaitForSeconds(3f);
        yield return Shot("heal_05_after");
        Application.Quit();
    }

    // 敵の見た目の確認: 全種類の見た目を1体ずつ横に並べ(ゲームの敵としては動かさない)、
    // 明るいライトを足して、正面・斜め・横から撮る。アニメの途中のコマも撮る
    IEnumerator EnemyGallery()
    {
        yield return new WaitForSeconds(1f);
        g.SelectMap("A");
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        g.Flashlight.DebugAim = new Vector2(0.9f, 0.1f);
        yield return new WaitForSeconds(0.3f);
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
        var sun = new GameObject("GallerySun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(40f, 160f, 0f);

        var models = NightEnemyModels.Instance;
        var kinds = new System.Collections.Generic.List<NightEnemy.Kind>();
        for (int k = 0; k < NightEnemy.KindCount; k++)
        {
            var list = models.ListOf((NightEnemy.Kind)k);
            for (int n = 0; list != null && n < list.Length; n++) kinds.Add((NightEnemy.Kind)k);
        }
        const float gap = 4.5f;
        // マップの木や壁にさえぎられないよう、マップの外の何もない所に地面を1枚置いて並べる
        Vector3 c = new Vector3(400f, 0f, 400f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.position = c;
        floor.transform.localScale = new Vector3(6f, 1f, 4f);
        floor.GetComponent<Renderer>().material.color = new Color(0.25f, 0.32f, 0.28f);
        for (int i = 0; i < kinds.Count; i++)
        {
            float x = c.x + (i - (kinds.Count - 1) * 0.5f) * gap;
            new NightEnemy(kinds[i], new Vector3(x, 0f, c.z));
        }

        yield return LockCamera(c + new Vector3(0f, 5f, 24f), c + Vector3.up * 2f, 0.4f);
        yield return Shot("gallery_01_front");
        yield return LockCamera(c + new Vector3(0f, 5f, 24f), c + Vector3.up * 2f, 0.35f);
        yield return Shot("gallery_02_front_later");
        yield return LockCamera(c + new Vector3(0f, 16f, 18f), c + Vector3.up * 1f, 0.3f);
        yield return Shot("gallery_03_top");
        for (int i = 0; i < kinds.Count; i++)
        {
            float x = c.x + (i - (kinds.Count - 1) * 0.5f) * gap;
            Vector3 p = new Vector3(x, c.y, c.z);
            yield return LockCamera(p + new Vector3(7f, 2.5f, 3f), p + Vector3.up * 1.6f, 0.25f);
            yield return Shot("gallery_side_" + i);
        }
        Application.Quit();
    }

    // すり抜け型の確認: 通路の外(森の中)に出して、通路を無視してまっすぐ近づいてくるか・
    // 倒しても回復オーブを落とさないか・体力が一番低いかをログに出す
    IEnumerator SpriteTest()
    {
        yield return new WaitForSeconds(1f);
        var args = System.Environment.GetCommandLineArgs();
        int m = System.Array.IndexOf(args, "-nightMap");
        g.SelectMap(m >= 0 && m + 1 < args.Length ? args[m + 1] : "A");
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        g.Flashlight.DebugAim = new Vector2(0.9f, 0.1f); // まだ照らさない
        yield return new WaitForSeconds(0.3f);
        for (int k = 0; k < NightEnemy.KindCount; k++)
            Debug.Log("NightAutoShot: hp " + (NightEnemy.Kind)k + "=" + NightEnemy.ConfigOf((NightEnemy.Kind)k).lightHp);
        // 止まっていると擬態して敵が見失うので、擬態ゲージが切れる(しばらく擬態できなくなる)まで待つ
        float w = 0f;
        while (!g.Camouflaged && w < 2f) { w += Time.deltaTime; yield return null; }
        while (g.Camouflaged) yield return null;

        // プレイヤーから横に離れた、通路の外の場所を探して出す
        Vector3 pp = g.PlayerPos, start = pp;
        for (float off = 6f; off < 20f; off += 0.5f)
        {
            if (!g.Map.Walkable(pp.x + off, pp.z, 0f)) { start = new Vector3(pp.x + off + 2f, 0f, pp.z); break; }
        }
        start.y = g.Map.SurfaceAt(start.x, start.z);
        var en = NightEnemy.Spawn(NightEnemy.Kind.Sprite, start);
        g.Enemies.Add(en);
        Debug.Log("NightAutoShot: sprite start=" + start + " walkable=" + g.Map.Walkable(start.x, start.z, 0f) + " dist=" + NightMap.Flat(start, pp).ToString("0.0"));
        int offPath = 0;
        for (int s = 0; s < 8; s++)
        {
            Vector3 mid = Vector3.Lerp(pp, en.Position, 0.5f);
            yield return LockCamera(mid + new Vector3(0f, 9f, 10f), mid + Vector3.up * 0.6f, 0.25f); // ライトは当てないまま見る
            if (!g.Map.Walkable(en.Position.x, en.Position.z, 0f)) offPath++;
            Debug.Log("NightAutoShot: sprite t=" + (s * 0.25f).ToString("0.00") + " pos=" + en.Position + " dist=" + NightMap.Flat(en.Position, g.PlayerPos).ToString("0.0") +
                      " onPath=" + g.Map.Walkable(en.Position.x, en.Position.z, 0f));
            if (s == 2) yield return Shot("sprite_01_through_forest");
        }
        Debug.Log("NightAutoShot: sprite offPathSamples=" + offPath);

        int orbsBefore = g.DebugHealOrbCount;
        yield return LockCameraOn(en.Position, 1.5f); // 照らして倒す
        Debug.Log("NightAutoShot: sprite dying=" + en.dying + " healOrbs before=" + orbsBefore + " after=" + g.DebugHealOrbCount);
        yield return Shot("sprite_02_killed");

        // 4方向(通路の外も含む)から出し、プレイヤーに触れるまでの時間を見る(段差・橋のあるマップは -nightMap B / C で)
        {
            string id = g.MapId;
            g.Flashlight.DebugAim = new Vector2(0.9f, 0.1f);
            g.DebugNoCamo = true;
            g.DebugFlashIn(60f); // 自動の閃光で倒されないように遅らせる
            while (g.Camouflaged) yield return null;
            Vector3 p0 = g.PlayerPos;
            var list = new System.Collections.Generic.List<NightEnemy>();
            foreach (var o in new[] { new Vector3(9f, 0, 0), new Vector3(-9f, 0, 0), new Vector3(0, 0, 9f), new Vector3(0, 0, -9f) })
            {
                Vector3 s = p0 + o;
                s.y = g.Map.SurfaceAt(s.x, s.z);
                var e = NightEnemy.Spawn(NightEnemy.Kind.Sprite, s);
                g.Enemies.Add(e);
                list.Add(e);
            }
            float[] reach = { -1f, -1f, -1f, -1f };
            float hp0 = g.Hp, t = 0f, realT = 0f;
            int frames = 0;
            while (t < 7f && g.State == NightGame.GameState.Playing)
            {
                for (int i = 0; i < list.Count; i++)
                    if (reach[i] < 0f && NightMap.Flat(list[i].Position, g.PlayerPos) < 0.95f && Mathf.Abs(list[i].Position.y - g.PlayerPos.y) < 0.9f) reach[i] = t;
                if (System.Array.TrueForAll(reach, r => r >= 0f)) break;
                hp0 = Mathf.Max(hp0, g.Hp);
                g.DebugSetHp(100f); // ほかの敵に倒されて途中で終わらないように
                t += Mathf.Min(Time.deltaTime, 0.05f); // ゲームと同じく1フレーム最大0.05秒で数える(ゲーム内の時間)
                frames++; realT += Time.unscaledDeltaTime;
                yield return null;
            }
            Debug.Log("NightAutoShot: sprite map " + id + " fps=" + (frames / Mathf.Max(0.01f, realT)).ToString("0") + " enemies=" + g.Enemies.Count);
            Debug.Log("NightAutoShot: sprite map " + id + " reach=" + string.Join(",", System.Array.ConvertAll(reach, r => r.ToString("0.0"))) +
                      " hp " + hp0.ToString("0") + "->" + g.Hp.ToString("0") + " playerY=" + g.PlayerPos.y.ToString("0.00"));
            for (int i = 0; i < list.Count; i++)
                if (reach[i] < 0f) Debug.Log("NightAutoShot: sprite map " + id + " stuck#" + i + " pos=" + list[i].Position);
        }
        Application.Quit();
    }

    // 発表資料用の写真: 花・蛍の石・門・ランタンを1つずつ、背景なし(透明PNG)で撮る。
    // 写したい物の部品だけを空きレイヤーに移し、そのレイヤーだけを写す撮影用カメラで描く
    IEnumerator PropPhotos()
    {
        yield return new WaitForSeconds(1f);
        g.SelectMap("A");
        yield return new WaitForSeconds(0.5f);
        g.BeginPlay("テスト");
        yield return new WaitForSeconds(0.5f);

        // 1つ目の石を照らして、蛍が集まった状態にする(触れないので捕まえはしない)
        var orb = g.World.Orbs[0];
        Teleport(orb.Position + new Vector3(0, 0, 5f));
        yield return new WaitForSeconds(1f);
        yield return AimAt(() => orb.Position, 3f);
        g.Flashlight.DebugAim = new Vector2(0.9f, 0.1f);
        yield return new WaitForSeconds(1f);
        Teleport(new Vector3(NightMap.Exit.x, 0f, NightMap.Exit.y - 6f));
        yield return new WaitForSeconds(0.3f);

        var sun = new GameObject("PhotoSun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.0f;
        sun.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
        Vector3 front = new Vector3(0.25f, 0.5f, 1f);

        // 透明な背景と、夜らしい暗い背景(蛍や花の光が分かる)の2通りを撮る
        var plant = g.World.Plants[0];
        var orb2 = g.World.Orbs[1];
        var lamp = g.World.Lamps[0];
        Vector3 gateView = new Vector3(0.35f, 0.4f, 1f);
        for (int pass = 0; pass < 2; pass++)
        {
            string sfx = pass == 0 ? "" : "_night";
            Color bg = pass == 0 ? new Color(0f, 0f, 0f, 0f) : new Color(0.06f, 0.09f, 0.15f, 1f);
            yield return Photo("prop_flower" + sfx, RenderersNear(new Vector3(plant.x, plant.height, plant.z), 1.0f, 1.6f), front, bg);
            yield return Photo("prop_stone_lit" + sfx, RenderersOf(orb.root), front, bg);
            yield return Photo("prop_stone_dark" + sfx, RenderersOf(orb2.root), front, bg);
            yield return Photo("prop_lantern" + sfx, RenderersNear(new Vector3(lamp.x, lamp.height, lamp.z), 0.55f, 2.2f), front, bg);
            yield return Photo("prop_gate_closed" + sfx, GateParts(), gateView, bg);
        }
        g.World.Gate.Open();
        yield return new WaitForSeconds(2.5f);
        yield return Photo("prop_gate_open", GateParts(), gateView, new Color(0f, 0f, 0f, 0f));
        yield return Photo("prop_gate_open_night", GateParts(), gateView, new Color(0.06f, 0.09f, 0.15f, 1f));
        Application.Quit();
    }

    // 門が開いたときに出る背の高い光の柱は入れない(写真が縦に伸びてしまうため)
    System.Collections.Generic.List<Renderer> GateParts()
    {
        return RenderersOf(g.World.Gate.root).FindAll(r => r.bounds.size.y < 6f);
    }

    static System.Collections.Generic.List<Renderer> RenderersOf(Transform root)
    {
        return new System.Collections.Generic.List<Renderer>(root.GetComponentsInChildren<Renderer>());
    }

    // 親を持たない部品(ランタンなど)用: 水平距離 radius 以内・地面からの高さ maxH 以内にある小さな部品を集める
    static System.Collections.Generic.List<Renderer> RenderersNear(Vector3 p, float radius, float maxH)
    {
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            Vector3 c = r.bounds.center;
            float dx = c.x - p.x, dz = c.z - p.z;
            if (dx * dx + dz * dz > radius * radius) continue;
            if (c.y < p.y - 0.2f || c.y > p.y + maxH) continue;
            if (r.bounds.size.magnitude > maxH * 1.6f) continue;
            if (r.GetComponentInParent<NightPlayer>() != null) continue;
            list.Add(r);
        }
        return list;
    }

    IEnumerator Photo(string name, System.Collections.Generic.List<Renderer> rs, Vector3 viewDir, Color background)
    {
        yield return null;
        foreach (var en in g.Enemies) if (en.go != null) en.go.SetActive(false);
        const int Layer = 31, Size = 1400;
        var saved = new int[rs.Count];
        Bounds b = rs.Count > 0 ? rs[0].bounds : new Bounds(g.PlayerPos, Vector3.one);
        for (int i = 0; i < rs.Count; i++)
        {
            saved[i] = rs[i].gameObject.layer;
            rs[i].gameObject.layer = Layer;
            if (!(rs[i] is ParticleSystemRenderer)) b.Encapsulate(rs[i].bounds);
        }
        var cam = new GameObject("PhotoCam").AddComponent<Camera>();
        cam.enabled = false;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        cam.fieldOfView = 26f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        float dist = b.extents.magnitude * 1.08f / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        cam.transform.position = b.center + viewDir.normalized * dist;
        cam.transform.LookAt(b.center);
        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;

        bool fog = RenderSettings.fog;
        Color amb = RenderSettings.ambientLight;
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
        cam.Render();
        RenderSettings.fog = fog;
        RenderSettings.ambientLight = amb;

        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        Debug.Log("NightAutoShot: photo " + name + " parts=" + rs.Count + " bounds=" + b);

        for (int i = 0; i < rs.Count; i++) if (rs[i] != null) rs[i].gameObject.layer = saved[i];
        cam.targetTexture = null;
        Object.Destroy(rt);
        Object.Destroy(cam.gameObject);
    }

    // LockCameraOn と同じく、毎フレーム Update の後にカメラを上書きし直す(位置と注視点を直接指定する版)
    IEnumerator LockCamera(Vector3 pos, Vector3 look, float seconds)
    {
        float t = 0f;
        while (true)
        {
            g.Cam.transform.position = pos;
            g.Cam.transform.LookAt(look);
            if (t >= seconds) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null;
        yield return null;
        Debug.Log("NightAutoShot: " + name + " state=" + g.State + " hp=" + g.Hp.ToString("0") + " enemies=" + g.Enemies.Count +
                  " lightTarget=" + g.LightTarget + " player=" + g.PlayerPos + " camo=" + g.Camouflaged);
    }

    // NightGame.Tick() が毎フレーム PlaceCamera() でカメラをプレイヤー追従位置へ戻してしまうため、
    // このコルーチンを回している間だけ、Update後(このコルーチンの再開タイミング)に毎フレーム
    // カメラを強制的に目標へ向け直す。プレイヤーが木にぶつかって押し戻される影響を受けずに、
    // 確実に目標が画面に収まった状態を作れる。
    // NightGame.Update() が先に動いてカメラを上書きするので、毎フレーム「Updateの後」に
    // もう一度こちらで上書きし直す。ループの最後も必ず上書きで終わるようにし(その直後にyieldを挟まない)、
    // Shot() が同じフレーム内で WaitForEndOfFrame するときにカメラがズレていないようにする。
    IEnumerator LockCameraOn(Vector3 target, float seconds)
    {
        float t = 0f;
        while (true)
        {
            g.Cam.transform.position = target + new Vector3(0, 9f, 10f);
            g.Cam.transform.LookAt(target + Vector3.up * 0.6f);
            Vector3 v = g.Cam.WorldToViewportPoint(target);
            g.Flashlight.DebugAim = new Vector2(v.x, 1f - v.y);
            if (t >= seconds) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    // 指定したワールド座標を照らし続ける
    IEnumerator AimAt(System.Func<Vector3> world, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            Vector3 v = g.Cam.WorldToViewportPoint(world());
            g.Flashlight.DebugAim = new Vector2(v.x, 1f - v.y);
            t += Time.deltaTime;
            yield return null;
        }
    }

    // PressureMatServer(ESP32)を実機なしで動作確認するため、同じフォーマットのUDPパケットを
    // ループバックへ自分で送りつけ、キー入力なしでプレイヤーが動くかを確認する。
    IEnumerator TestPressureMat()
    {
        Vector3 before = g.PlayerPos;
        var udp = new UdpClient();
        byte[] forward = Encoding.UTF8.GetBytes("1,0,0,0"); // 前のみ踏んでいる状態
        float t = 0f;
        while (t < 1f)
        {
            udp.Send(forward, forward.Length, "127.0.0.1", 5005);
            t += Time.deltaTime;
            yield return null;
        }
        udp.Close();
        Vector3 after = g.PlayerPos;
        float moved = Vector3.Distance(before, after);
        Debug.Log("NightAutoShot: PressureMat test moved=" + moved.ToString("0.00") +
                  " hasMatInput=" + (PressureMatServer.Instance != null && PressureMatServer.Instance.HasMatInput) +
                  " before=" + before + " after=" + after);

        // 送信をやめたら0.7秒以内に「マット入力なし」に戻るはず(StaleMs=500ms)
        yield return new WaitForSeconds(0.7f);
        Debug.Log("NightAutoShot: PressureMat idle hasMatInput=" + (PressureMatServer.Instance != null && PressureMatServer.Instance.HasMatInput));
    }

    // スマホの接続を模擬する(本物のスマホと同じ /pointer への POST)。接続中の人数表示の確認用。
    void PostFake(string id)
    {
        try
        {
            using (var wc = new System.Net.WebClient())
            {
                wc.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                wc.UploadString("http://127.0.0.1:" + (PointerServer.Instance != null ? PointerServer.Instance.port : 8787) + "/pointer", "{\"id\":\"" + id + "\",\"x\":0.5,\"y\":0.5,\"active\":false}");
            }
        }
        catch (System.Exception e) { Debug.Log("NightAutoShot: PostFake failed " + e.Message); }
    }

    void Teleport(Vector3 p)
    {
        g.Player.transform.position = new Vector3(p.x, g.Map.SurfaceAt(p.x, p.z), p.z);
        g.Player.Velocity = Vector3.zero;
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(1.5f);
        NightHud.DebugQrWaiting = true;
        yield return Shot("00_qr_waiting");
        NightHud.DebugQrWaiting = false;
        yield return Shot("01_title_A");
        g.SelectMap("B");
        yield return new WaitForSeconds(0.6f);
        for (int k = 0; k < 4; k++) PostFake("fake" + k);
        yield return new WaitForSeconds(0.3f);
        yield return Shot("02_title_B");
        NightHud.DebugOpenQrZoom();
        yield return new WaitForSeconds(0.2f);
        yield return Shot("02b_qr_zoom");
        NightHud.DebugCloseQrZoom();
        g.SelectMap("C");
        yield return new WaitForSeconds(0.6f);
        yield return Shot("03_title_C");
        g.SelectMap("A");
        yield return new WaitForSeconds(0.6f);

        g.BeginPlay("テスト");
        g.Flashlight.DebugAim = new Vector2(0.5f, 0.35f);
        yield return new WaitForSeconds(2.5f);
        for (int k = 0; k < 4; k++) PostFake("fake" + k);
        yield return new WaitForSeconds(0.3f);
        yield return Shot("04_play_start");

        yield return TestPressureMat();

        // 敵が出るまで待ち、最寄りの敵(死角の外)を照らす → 光っているか
        float wait = 0f;
        while (wait < 10f && g.Enemies.Find(e => !e.dying && Vector3.Distance(e.Position, g.PlayerPos) > 2.5f) == null) { wait += Time.deltaTime; yield return null; }
        var target = g.Enemies.Find(e => !e.dying && Vector3.Distance(e.Position, g.PlayerPos) > 2.5f);
        if (target != null)
        {
            yield return AimAt(() => target.go != null ? target.Position : g.PlayerPos, 0.5f);
            if (target.go != null)
            {
                var mr = target.go.GetComponentInChildren<Renderer>();
                Debug.Log("NightAutoShot: lit enemy " + target.kind + " color=" + mr.sharedMaterial.color + " lightHp=" + target.lightHp.ToString("0.0") + "/" + target.lightHpMax);
            }
            yield return Shot("05_enemy_lit");
        }

        // 止まっていれば擬態に入る
        g.Flashlight.DebugAim = new Vector2(0.5f, 0.2f);
        yield return new WaitForSeconds(0.2f);
        yield return Shot("06_camo");

        // 蛍の石を照らして蛍を集める
        var orb = g.World.Orbs[0];
        Teleport(orb.Position + new Vector3(0, 0, 4f));
        yield return new WaitForSeconds(1.2f);
        yield return AimAt(() => orb.Position, 1.1f);
        yield return Shot("07a_stone_half");
        yield return AimAt(() => orb.Position, 1.4f);
        yield return new WaitForSeconds(0.8f);
        yield return Shot("07_orb_materialized");
        // 触れて蛍を捕まえる
        Teleport(orb.Position + new Vector3(0, 0, 0.6f));
        yield return new WaitForSeconds(0.25f);
        yield return Shot("07b_firefly_caught");
        Debug.Log("[AutoShot] 蛍の回収: collected=" + orb.collected + " OrbCount=" + g.OrbCount);

        // 近くのランタンを点灯
        NightLamp lamp = g.World.Lamps[0];
        foreach (var l in g.World.Lamps) if (Vector3.Distance(new Vector3(l.x, 0, l.z), g.PlayerPos) < Vector3.Distance(new Vector3(lamp.x, 0, lamp.z), g.PlayerPos)) lamp = l;
        yield return AimAt(() => new Vector3(lamp.x, lamp.height, lamp.z), 0.9f);
        yield return new WaitForSeconds(0.4f);
        Debug.Log("NightAutoShot: lamp lit=" + lamp.lit + " charge=" + lamp.charge.ToString("0.00"));
        yield return Shot("08_lantern_lit");

        // 導きの花
        NightGuidePlant plant = g.World.Plants[0];
        foreach (var p in g.World.Plants) if (Vector3.Distance(new Vector3(p.x, 0, p.z), g.PlayerPos) < Vector3.Distance(new Vector3(plant.x, 0, plant.z), g.PlayerPos)) plant = p;
        Teleport(new Vector3(plant.x, 0, plant.z + 2.5f));
        yield return new WaitForSeconds(1f);
        yield return AimAt(() => new Vector3(plant.x, plant.height, plant.z), 2.4f);
        yield return Shot("09_guide_path");

        // 地形オブジェクト(木・岩)が光を当てると黄色く光るかの確認。
        // 森は密集しているため、単純に「一番近い」物を選ぶと他の木に隠れて見えないことがある。
        // 周囲に他のオブジェクトが無い(=カメラから隠れにくい)ものを選ぶ。
        NightTerrainProp litProp = null;
        float bestD = float.PositiveInfinity;
        foreach (var p in g.World.Props)
        {
            if (p.materials == null || p.materials.Length == 0 || !p.solid) continue;
            float minNeighbor = float.PositiveInfinity;
            foreach (var other in g.World.Props)
            {
                if (other == p) continue;
                float nd = Vector3.Distance(p.position, other.position);
                if (nd < minNeighbor) minNeighbor = nd;
            }
            if (minNeighbor < 3.5f) continue; // 近くに他の木・岩があると隠れる可能性が高いので除外
            float d = Vector3.Distance(p.position, g.PlayerPos);
            if (d < bestD) { bestD = d; litProp = p; }
        }
        if (litProp != null)
        {
            // プレイヤーは動かさない(木や岩にぶつかって押し戻され、カメラが目標から外れてしまうため)。
            // カメラと懐中電灯の照準だけを毎フレーム強制的に目標へ向ける。
            yield return LockCameraOn(litProp.position, 1.2f);
            string propKind = litProp.color == NightMeshes.Hex(0x2f6b2f) ? "tree" : litProp.color == NightMeshes.Hex(0x555a5f) ? "rock" : "other";
            Debug.Log("NightAutoShot: terrain prop kind=" + propKind + " pos=" + litProp.position + " glow=" + litProp.glow.ToString("0.00") +
                      " color=" + (litProp.materials.Length > 0 ? litProp.materials[0].color.ToString() : "n/a"));
            yield return Shot("09b_prop_glow");
        }

        // 敵の3Dモデル(群れ型・硬い型・遠距離型)を並べて確認する。1枚は光を当て、1枚は当てない
        Vector3 pc = g.PlayerPos;
        var kinds = new[] { NightEnemy.Kind.Moth, NightEnemy.Kind.Brute, NightEnemy.Kind.Shooter };
        for (int i = 0; i < kinds.Length; i++)
        {
            float ex = pc.x + (i - 1) * 2.6f, ez = pc.z - 6f;
            g.Enemies.Add(NightEnemy.Spawn(kinds[i], new Vector3(ex, g.Map.SurfaceAt(ex, ez), ez)));
        }
        yield return LockCameraOn(new Vector3(pc.x, pc.y, pc.z - 6f), 0.3f);
        foreach (var e in g.Enemies)
        {
            if (e.kind != NightEnemy.Kind.Moth || e.go == null) continue;
            Transform fl = null, bl = null;
            // 四足(狼・猪)の前足が進む向き(go.forward)の側にあるか。プラスなら正しい向き
            foreach (var t in e.go.GetComponentsInChildren<Transform>()) { if (t.name == "LegFL") fl = t; if (t.name == "LegBL") bl = t; }
            if (fl != null && bl != null)
                Debug.Log("NightAutoShot: quad front-back along go.forward=" + Vector3.Dot(fl.position - bl.position, e.go.transform.forward).ToString("0.00"));
        }
        yield return Shot("12a_enemy_models_brute_lit");
        yield return LockCameraOn(new Vector3(pc.x - 2.6f, pc.y, pc.z - 6f), 0.4f);
        yield return Shot("12b_enemy_moth_lit");

        // 閃光
        wait = 0f;
        while (wait < 14f && g.FlashT < 0.25f) { wait += Time.deltaTime; yield return null; }
        yield return Shot("10_flash");

        g.SendMessage("End", false);
        yield return new WaitForSeconds(0.6f);
        yield return Shot("11_end_a");
        yield return new WaitForSeconds(2.0f);
        yield return Shot("11_end_b");
        yield return new WaitForSeconds(3.5f);
        yield return Shot("11_end_c");
        yield return new WaitForSeconds(0.5f);
        Application.Quit();
    }
}
