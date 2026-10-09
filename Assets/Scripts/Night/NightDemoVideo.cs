using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ゲーム全体の仕組みを紹介する動画の撮影モード。exe を「-nightAutoShot <フォルダ> -nightDemoVideo」付きで起動したときだけ動く。
// 主人公を自動で動かして、マップ選択 → 移動 → 灯り役の光 → 回復オーブ → 擬態 → ランタン → 導きの花 → 蛍の石 → 雷 → 脱出 → ランキング
// の順に実演し、画面の下に説明の字幕を出す。1フレームずつ JPG に保存するので(30fps固定)、PCの速さに関係なく滑らかな動画になる。
// 保存した連番画像は tools/make_demo_video.py で動画(WebM)にまとめる。
public class NightDemoVideo : MonoBehaviour
{
    const int Fps = 30;
    NightGame g;
    string dir;
    int frame;
    bool recording;

    string chapter = "", caption = "";
    int chapterNo;
    float captionTime;
    float fade; // 場面転換の暗転(0〜1)
    Font font;
    readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-nightAutoShot");
        if (System.Array.IndexOf(args, "-nightDemoVideo") < 0 || i < 0 || i + 1 >= args.Length) { enabled = false; return; }
        dir = Path.Combine(args[i + 1], "frames");
        Directory.CreateDirectory(dir);
        // -nightDemoClean: 字幕と暗転を出さずに撮る(Remotion などで後から演出を重ねるため)。
        // 場面の区切りは <フォルダ>/chapters.json に、何フレーム目から始まるかを書き出す。
        clean = System.Array.IndexOf(args, "-nightDemoClean") >= 0;
        chaptersPath = Path.Combine(args[i + 1], "chapters.json");
        g = NightGame.Instance;
        font = Resources.Load<Font>("Fonts/RocknRollOne-Regular");
        Application.runInBackground = true;
        Time.captureFramerate = Fps; // 1フレーム = 1/30秒として進める(撮影が重くても動画の時間はずれない)
        StartCoroutine(Capture());
        StartCoroutine(Run());
    }

    // ---- 撮影 ----
    IEnumerator Capture()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            if (!recording) continue;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, "f_" + frame.ToString("00000") + ".jpg"), tex.EncodeToJPG(90));
            Destroy(tex);
            frame++;
        }
    }

    IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.deltaTime; KeepAlive(); yield return null; }
    }

    // 撮影中に力尽きないように(デモなのでHPは減りすぎないようにする)
    void KeepAlive()
    {
        if (g.State == NightGame.GameState.Playing && g.Hp < 60f) g.DebugSetHp(60f);
    }

    bool clean;
    string chaptersPath;
    readonly List<string> chapterJson = new List<string>();

    void Caption(string title, string text)
    {
        chapterNo++;
        chapter = title;
        caption = text;
        captionTime = Time.time;
        chapterJson.Add("{\"no\":" + chapterNo + ",\"frame\":" + frame + ",\"title\":\"" + title + "\",\"text\":\"" + text + "\"}");
    }

    void SaveChapters()
    {
        File.WriteAllText(chaptersPath, "{\"fps\":" + Fps + ",\"frames\":" + frame + ",\"chapters\":[\n" + string.Join(",\n", chapterJson) + "\n]}\n", new System.Text.UTF8Encoding(false));
    }

    IEnumerator Cut(System.Action during)
    {
        for (float t = 0; t < 0.35f; t += Time.deltaTime) { fade = t / 0.35f; KeepAlive(); yield return null; }
        fade = 1f;
        during();
        yield return null;
        yield return null;
        for (float t = 0; t < 0.35f; t += Time.deltaTime) { fade = 1f - t / 0.35f; KeepAlive(); yield return null; }
        fade = 0f;
    }

    // ---- 自動操作 ----
    IEnumerator WalkTo(Vector3 target, float maxTime, float arrive = 0.6f)
    {
        var path = g.Map.NavigationPath(g.PlayerPos, target);
        int k = 0;
        float t = 0f;
        while (t < maxTime)
        {
            Vector3 p = g.PlayerPos;
            if (NightMap.Flat(p, target) < arrive) break;
            Vector3 wp = k < path.Count ? path[k] : target;
            if (k < path.Count && NightMap.Flat(p, wp) < 0.5f) { k++; continue; }
            Vector3 d = wp - p; d.y = 0f;
            NightPlayer.DebugInput = d.normalized;
            t += Time.deltaTime;
            KeepAlive();
            yield return null;
        }
        NightPlayer.DebugInput = Vector3.zero;
    }

    void Teleport(Vector3 p)
    {
        g.Player.transform.position = new Vector3(p.x, g.Map.SurfaceAt(p.x, p.z), p.z);
        g.Player.Velocity = Vector3.zero;
    }

    void Aim(Vector3 world)
    {
        Vector3 v = g.Cam.WorldToViewportPoint(world);
        g.Flashlight.DebugAim = new Vector2(v.x, 1f - v.y);
    }

    void AimAway() => g.Flashlight.DebugAim = new Vector2(0.97f, 0.97f);

    IEnumerator AimAt(System.Func<Vector3> world, float seconds, System.Func<bool> until = null)
    {
        float t = 0f;
        while (t < seconds && (until == null || !until()))
        {
            Aim(world());
            t += Time.deltaTime;
            KeepAlive();
            yield return null;
        }
    }

    // 主人公から見て、指定した場所に近い通路の上の点(その物の手前に立つため)
    Vector3 StandNear(Vector3 obj, float dist)
    {
        Vector3 best = obj;
        float bestScore = float.MaxValue;
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI / 12f;
            Vector3 c = obj + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * dist;
            if (!g.Map.Walkable(c.x, c.z, 0.6f)) continue;
            float score = Mathf.Abs(c.z - obj.z - dist) + Mathf.Abs(c.x - obj.x) * 0.5f; // なるべく物の手前(カメラ側=+Z)
            if (score < bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    NightEnemy NearestEnemy()
    {
        NightEnemy best = null; float bd = float.MaxValue;
        foreach (var e in g.Enemies) { if (e.dying) continue; float d = NightMap.Flat(e.Position, g.PlayerPos); if (d < bd) { bd = d; best = e; } }
        return best;
    }

    // ---- 台本 ----
    IEnumerator Run()
    {
        yield return new WaitForSeconds(0.5f);
        recording = true;

        // 1. マップ選択
        Caption("カメレオンナイト", "主人公(カメレオン)と、スマホで光を当てる「灯り役」が協力して、夜の森から脱出するゲーム");
        yield return Wait(3.2f);
        Caption("マップを選ぶ", "3つのマップは難易度が違う。A やさしい / B ふつう / C むずかしい。マップごとにランキングがある");
        g.SelectMap("B"); yield return Wait(1.3f);
        g.SelectMap("C"); yield return Wait(1.3f);
        g.SelectMap("A"); yield return Wait(1.4f);
        Caption("名前を入れてスタート", "ランキングに載る名前を入力。灯り役はQRコードを読むだけで、名前なしで参加できる");
        g.StartGame();
        yield return Wait(2.6f);
        g.BeginPlay("デモ");
        AimAway();
        yield return Wait(1.0f);

        // 2. 移動
        Caption("移動する", "主人公は圧力マット(足踏み)で移動。トーチの灯りで足元だけが見える。目的は2つの石で蛍を集めて門から脱出");
        Vector3 start = g.PlayerPos;
        yield return WalkTo(start + new Vector3(0, 0, -7f), 3.5f);
        yield return WalkTo(start + new Vector3(0, 0, -2f), 2.5f);

        // 3. 灯り役の光で敵を倒す
        Caption("灯り役が光で敵を倒す", "スマホを動かすとライトが動く。光を当て続けると敵は赤く光ってダメージを受け、倒れる(最大7人で参加できる)");
        g.DebugSpawnNear(2, 1f);
        yield return Wait(0.6f);
        for (int n = 0; n < 2; n++)
        {
            var en = NearestEnemy();
            if (en == null) break;
            yield return AimAt(() => en.Position, 5f, () => en.dying);
            yield return Wait(0.3f);
        }

        // 4. 回復オーブ
        Caption("回復オーブ", "倒した敵は緑の回復オーブを落とす。近づくと吸い寄せられて、HPが回復する(頭の上の緑のバー)");
        AimAway();
        g.DebugSetHp(55f);
        yield return Wait(0.6f);
        float wait = 0f;
        while (g.DebugHealOrbCount > 0 && wait < 4f)
        {
            Vector3 orb = g.DebugNearestHealOrb();
            yield return WalkTo(orb, 1.5f, 0.3f);
            wait += 1.5f;
        }
        yield return Wait(1.2f);

        // 5. 擬態
        Caption("止まると擬態", "立ち止まると地面の色に変わって無敵になる(最大3秒・頭の上の水色のバー)。敵は見失って「？」を出してうろうろする");
        g.DebugSpawnNear(4, 50f);
        NightPlayer.DebugInput = Vector3.zero;
        yield return Wait(3.4f);
        Caption("擬態が解けると見つかる", "ゲージが切れると敵が「！」で気づいて、また追ってくる。ゲージは動いている間に少しずつ戻る");
        yield return Wait(1.6f);
        foreach (var e in g.Enemies) e.lightHp = Mathf.Min(e.lightHp, 1f); // 次の場面のため、残った敵を片付けやすくする
        yield return AimAt(() => { var e = NearestEnemy(); return e != null ? e.Position : g.PlayerPos; }, 2.5f, () => NearestEnemy() == null);
        AimAway();

        // 6. ランタン
        NightLamp lamp = null; float bl = float.MaxValue;
        foreach (var l in g.World.Lamps) { if (l.lit) continue; float d = NightMap.Flat(new Vector3(l.x, 0, l.z), g.PlayerPos); if (d < bl) { bl = d; lamp = l; } }
        if (lamp != null)
        {
            Vector3 lp = new Vector3(lamp.x, lamp.height, lamp.z);
            yield return Cut(() => Teleport(StandNear(lp, 3f)));
            Caption("ランタンを灯す", "ランタンに光を当てると、照らした時間がたまっていく(上のゲージ)。途中で光を外しても、続きからたまる");
            yield return Wait(0.8f);
            yield return AimAt(() => lp, 0.22f);
            AimAway(); yield return Wait(1.2f);
            yield return AimAt(() => lp, 0.6f, () => lamp.lit);
            Caption("ランタンが点灯", "ゲージが満タンになると点灯して、周りの敵を一気に倒す。その後も輪の中の敵にダメージを与え続ける");
            AimAway();
            yield return Wait(3.0f);
        }

        // 7. 導きの花
        NightGuidePlant plant = null; float bp = float.MaxValue;
        foreach (var pl in g.World.Plants) { float d = NightMap.Flat(new Vector3(pl.x, 0, pl.z), g.PlayerPos); if (d < bp) { bp = d; plant = pl; } }
        if (plant != null)
        {
            Vector3 pp = new Vector3(plant.x, plant.height, plant.z);
            yield return Cut(() => Teleport(StandNear(pp, 3f)));
            Caption("導きの花", "花に合計2秒光を当てると、まだ蛍のいる石までの道が光る。迷ったら花を探そう");
            yield return Wait(0.6f);
            yield return AimAt(() => pp, 2.3f);
            AimAway();
            yield return Wait(3.2f);
        }

        // 8. 蛍の石(1つ目)
        var orbs = g.World.Orbs;
        if (orbs.Count > 0)
        {
            var o = orbs[0];
            yield return Cut(() => Teleport(StandNear(o.Position, 3.2f)));
            Caption("石を照らして蛍を集める", "石に光を合計2秒当てると、石の色が変わって蛍が集まってくる。触れると蛍を捕まえられる");
            yield return Wait(0.6f);
            yield return AimAt(() => o.Position, 2.4f, () => o.materialized);
            AimAway();
            yield return Wait(0.4f);
            Caption("蛍を捕まえると敵が増える", "蛍を捕まえたり回復したりすると「脅威レベル」が上がり、敵が増えて速く出てくる。後半ほど難しくなる");
            yield return WalkTo(o.Position, 3f, 0.4f);
            yield return Wait(3.0f);
        }

        // 9. 雷
        Caption("雷(閃光)", "約12秒ごとにゴロゴロと予兆が鳴り、雷で森全体が光る。そのとき近くの敵はまとめてダメージを受ける");
        g.DebugSpawnNear(3, 1f);
        g.DebugFlashIn(1.6f);
        yield return Wait(4.2f);

        // 10. 蛍の石(2つ目) → 門が開く
        if (orbs.Count > 1)
        {
            var o = orbs[1];
            yield return Cut(() => Teleport(StandNear(o.Position, 3.2f)));
            Caption("2つ目の蛍で門が開く", "2つの石の蛍を捕まえると、スタート地点の門が開く");
            yield return AimAt(() => o.Position, 2.4f, () => o.materialized);
            AimAway();
            yield return WalkTo(o.Position, 3f, 0.4f);
            yield return Wait(2.2f);
        }

        // 11. 脱出 → 結果
        yield return Cut(() => Teleport(new Vector3(NightMap.Exit.x, 0, NightMap.Exit.y - 4f)));
        Caption("門から脱出", "開いた門を通るとクリア。クリアまでの時間と倒した敵の数でスコアが決まり、マップごとのランキングに載る");
        float tt = 0f;
        while (g.World.Gate.motion < 0.9f && tt < 3f) { tt += Time.deltaTime; KeepAlive(); yield return null; }
        yield return WalkTo(new Vector3(NightMap.Exit.x, 0, NightMap.Exit.y + 2f), 4f, 0.3f);
        yield return Wait(6.5f);

        chapter = "";
        recording = false;
        SaveChapters();
        Debug.Log("NightDemoVideo: frames=" + frame + " (" + (frame / (float)Fps).ToString("0.0") + "秒)");
        yield return new WaitForSecondsRealtime(0.5f);
        Application.Quit();
    }

    // ---- 字幕 ----
    GUIStyle S(string key, int size, Color color, TextAnchor align, bool wrap = false)
    {
        if (!styles.TryGetValue(key, out var s))
        {
            s = new GUIStyle { font = font, fontSize = size, alignment = align, wordWrap = wrap, fontStyle = FontStyle.Bold };
            styles[key] = s;
        }
        s.normal.textColor = color;
        return s;
    }

    void OnGUI()
    {
        GUI.depth = -100; // ゲームの画面表示より手前に描く
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float W = Screen.width / scale, H = 720f;

        if (clean) return;
        if (fade > 0f)
        {
            GUI.color = new Color(0, 0, 0, fade);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        if (chapter.Length == 0) return;

        float appear = Mathf.Clamp01((Time.time - captionTime) / 0.3f);
        float w = Mathf.Min(900f, W - 80f), h = 96f;
        Rect r = new Rect(W / 2f - w / 2f, H - h - 64f + (1f - appear) * 16f, w, h);
        GUI.color = new Color(0.02f, 0.05f, 0.05f, 0.82f * appear);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.55f, 0.9f, 0.7f, appear);
        GUI.DrawTexture(new Rect(r.x, r.y, 6f, r.height), Texture2D.whiteTexture);
        GUI.color = new Color(1, 1, 1, appear);
        GUI.Label(new Rect(r.x + 22, r.y + 10, r.width - 40, 32), chapter, S("chap", 24, new Color(1f, 0.9f, 0.55f), TextAnchor.MiddleLeft));
        GUI.Label(new Rect(r.x + 22, r.y + 44, r.width - 40, 46), caption, S("cap", 17, new Color(0.92f, 0.97f, 0.94f), TextAnchor.UpperLeft, true));
        GUI.color = Color.white;
    }
}
