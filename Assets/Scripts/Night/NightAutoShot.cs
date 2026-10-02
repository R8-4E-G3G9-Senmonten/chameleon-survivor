using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// 動作確認用の自動撮影。exe を「-nightAutoShot <保存先フォルダ>」付きで起動した時だけ動き、
// タイトル画面(各マップ)・プレイ中・敵への照射・擬態・光の球・ランタン・閃光・結果画面を順に撮影して終了する。
// 通常の起動では何もしない。
public class NightAutoShot : MonoBehaviour
{
    string dir;
    NightGame g;

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-nightAutoShot");
        if (i < 0 || i + 1 >= args.Length) { enabled = false; return; }
        dir = args[i + 1];
        Directory.CreateDirectory(dir);
        g = NightGame.Instance;
        int m = System.Array.IndexOf(args, "-nightMap");
        StartCoroutine(m >= 0 && m + 1 < args.Length ? Tour(args[m + 1]) : Run());
    }

    // 指定マップを開始し、広場を順に巡って撮影する(階段・橋・湖などの地形確認用)
    IEnumerator Tour(string id)
    {
        yield return new WaitForSeconds(1f);
        g.SelectMap(id);
        yield return new WaitForSeconds(0.5f);
        g.StartGame();
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

    void Teleport(Vector3 p)
    {
        g.Player.transform.position = new Vector3(p.x, g.Map.SurfaceAt(p.x, p.z), p.z);
        g.Player.Velocity = Vector3.zero;
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(1.5f);
        yield return Shot("01_title_A");
        g.SelectMap("B");
        yield return new WaitForSeconds(0.6f);
        yield return Shot("02_title_B");
        g.SelectMap("C");
        yield return new WaitForSeconds(0.6f);
        yield return Shot("03_title_C");
        g.SelectMap("A");
        yield return new WaitForSeconds(0.6f);

        g.StartGame();
        g.Flashlight.DebugAim = new Vector2(0.5f, 0.35f);
        yield return new WaitForSeconds(2.5f);
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
                var mr = target.go.GetComponentInChildren<MeshRenderer>();
                Debug.Log("NightAutoShot: lit enemy " + target.kind + " color=" + mr.sharedMaterial.color + " lightHp=" + target.lightHp.ToString("0.0") + "/" + target.lightHpMax);
            }
            yield return Shot("05_enemy_lit");
        }

        // 止まっていれば擬態に入る
        g.Flashlight.DebugAim = new Vector2(0.5f, 0.2f);
        yield return new WaitForSeconds(0.2f);
        yield return Shot("06_camo");

        // 光の球を照らして実体化させる
        var orb = g.World.Orbs[0];
        Teleport(orb.Position + new Vector3(0, 0, 4f));
        yield return new WaitForSeconds(1.2f);
        yield return AimAt(() => orb.Position, 2.3f);
        yield return Shot("07_orb_materialized");

        // 近くのランタンを点灯
        NightLamp lamp = g.World.Lamps[0];
        foreach (var l in g.World.Lamps) if (Vector3.Distance(new Vector3(l.x, 0, l.z), g.PlayerPos) < Vector3.Distance(new Vector3(lamp.x, 0, lamp.z), g.PlayerPos)) lamp = l;
        yield return AimAt(() => new Vector3(lamp.x, lamp.height, lamp.z), 2.3f);
        yield return new WaitForSeconds(0.4f);
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

        // 閃光
        wait = 0f;
        while (wait < 14f && g.FlashT < 0.25f) { wait += Time.deltaTime; yield return null; }
        yield return Shot("10_flash");

        g.SendMessage("End", false);
        yield return new WaitForSeconds(0.5f);
        yield return Shot("11_end");
        yield return new WaitForSeconds(0.5f);
        Application.Quit();
    }
}
