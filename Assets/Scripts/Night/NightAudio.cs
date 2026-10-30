using UnityEngine;

// 光の球収集版の効果音。モックアップ(WebAudio)の音を、波形をコードで生成して再現する。
// 常時: 雨音+低いうなり。閃光の前に雷鳴の予兆、閃光で落雷音。
public class NightAudio : MonoBehaviour
{
    const int SR = 44100;
    // BGM に対して効果音が聞こえやすいように、BGM は小さめ・効果音は作るときに大きめにする
    const float BgmVolume = 0.4f, SfxBoost = 1.6f;
    public const float FlashWarn = 1.3f;

    AudioSource sfx, ambient, bgm, lobby, charge;
    AudioClip hitBuzz, downChime, hurt, pulse, rumble, thunder, clear, gameOver;
    AudioClip countBeep, countGo, orbCharge, orbReady, orbGet;

    void Awake()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        ambient = gameObject.AddComponent<AudioSource>();
        ambient.playOnAwake = false;
        ambient.loop = true;
        ambient.volume = 0.25f; // 雨音は控えめに(効果音が聞こえやすいように)
        // ステージのBGM(Assets/Resources/Bgm/stage_bgm.mp3)
        bgm = gameObject.AddComponent<AudioSource>();
        bgm.playOnAwake = false;
        bgm.loop = true;
        bgm.volume = BgmVolume;
        bgm.clip = Resources.Load<AudioClip>("Bgm/stage_bgm");
        // ロビー(タイトル画面)のBGM(Assets/Resources/Bgm/lobby_bgm.mp3 = 時計図書館)
        lobby = gameObject.AddComponent<AudioSource>();
        lobby.playOnAwake = false;
        lobby.loop = true;
        lobby.volume = BgmVolume;
        lobby.clip = Resources.Load<AudioClip>("Bgm/lobby_bgm");

        hitBuzz = Clip("hit", Tone(140, 0.08f, true, 0.05f, 0, 90));
        downChime = Clip("down", Mix(Tone(700, 0.1f, false, 0.09f), Tone(1000, 0.14f, false, 0.08f, 0.07f)));
        hurt = Clip("hurt", Tone(180, 0.16f, true, 0.1f, 0, 70));
        pulse = Clip("pulse", Tone(220, 0.3f, false, 0.12f, 0, 660));
        rumble = Clip("rumble", Mix(Noise(false, 200, 120, FlashWarn, 0.12f), Tone(58, FlashWarn, false, 0.06f, 0, 90)));
        // 雷: 閃光の光り方(FlashShape: 0秒で一撃 → 一瞬暗く → 0.15秒で二撃目 → 光ったまま → 余韻)に合わせて
        // 「バリッ」「バリバリッ」と2回裂ける音を鳴らし、そのあと低い「ゴロゴロゴロ…」を長く響かせる。
        // 前の音は小さくてBGMに埋もれていたので、全体を大きくした(最後に音割れしない大きさへそろえる)
        float[] th = Noise(true, 5000, 1400, 0.22f, 0.7f);                    // 一撃目の「バリッ」
        th = Mix(th, Noise(true, 4200, 900, 0.35f, 0.65f, 0.15f));             // 二撃目の「バリバリッ」
        th = Mix(th, Noise(false, 1200, 70, 3.4f, 1.3f, 0.08f));               // 低い「ゴロゴロ」
        th = Mix(th, Noise(false, 400, 45, 2.6f, 0.9f, 0.9f));                 // 遠くへ転がっていく余韻
        th = Mix(th, Tone(62, 2.4f, false, 0.28f, 0.1f, 32));                 // お腹に響く低音
        thunder = Clip("thunder", Normalize(th, 0.95f), 1f); // 雷はもう最大の大きさにそろえてある
        // クリア: 「タタタ・ターン！」のファンファーレ(ソ・ド・ミ と上がって、ドミソの和音を長く鳴らす。
        // 和音には少しだけノコギリ波を混ぜて、金管楽器っぽい明るさを出す)
        float[] c = new float[0];
        float[] run = { 784, 1047, 1319 };
        for (int i = 0; i < run.Length; i++) c = Mix(c, Tone(run[i], 0.13f, false, 0.11f, i * 0.12f));
        float[] chord = { 1047, 1319, 1568, 2093 };
        foreach (float f in chord)
        {
            c = Mix(c, Tone(f, 1.1f, false, 0.06f, 0.4f));
            c = Mix(c, Tone(f, 0.9f, true, 0.012f, 0.4f));
        }
        c = Mix(c, Tone(523, 1.1f, false, 0.07f, 0.4f)); // 下で支える低いド
        for (int i = 0; i < 6; i++) c = Mix(c, Tone(2637 + i * 220, 0.12f, false, 0.025f, 0.55f + i * 0.07f)); // キラキラ
        clear = Clip("clear", c);

        // 3・2・1 のカウントダウン: 数字ごとに短く「ピッ」、スタートで高く「ピーン」(和音)
        countBeep = Clip("countBeep", Tone(880, 0.12f, false, 0.14f));
        countGo = Clip("countGo", Mix(Mix(Tone(1760, 0.45f, false, 0.12f), Tone(1319, 0.45f, false, 0.07f)), Tone(880, 0.45f, false, 0.06f)));
        // ホタルチャージ中: 短い「ピッ」。鳴らすたびに音の高さを上げる(PlayOrbCharge)
        orbCharge = Clip("orbCharge", Mix(Tone(660, 0.07f, false, 0.07f), Tone(1320, 0.05f, false, 0.025f)));
        // ホタルチャージ完了: 「キラリラリン」と駆け上がるアルペジオ + 高い余韻
        float[] ready = new float[0];
        float[] up = { 1047, 1319, 1568, 2093, 2637 };
        for (int i = 0; i < up.Length; i++) ready = Mix(ready, Tone(up[i], 0.2f, false, 0.08f, i * 0.055f));
        ready = Mix(ready, Tone(3136, 0.6f, false, 0.035f, 0.28f));
        orbReady = Clip("orbReady", ready);
        // ホタル取得: 「シュワーン」と音が上がって、明るい和音で「ポロン」
        float[] get = Tone(600, 0.22f, false, 0.09f, 0f, 1900f);
        get = Mix(get, Tone(1568, 0.5f, false, 0.08f, 0.18f));
        get = Mix(get, Tone(2093, 0.5f, false, 0.06f, 0.18f));
        get = Mix(get, Tone(2637, 0.45f, false, 0.05f, 0.26f));
        get = Mix(get, Tone(3136, 0.4f, false, 0.03f, 0.34f));
        orbGet = Clip("orbGet", get);
        charge = gameObject.AddComponent<AudioSource>();
        charge.playOnAwake = false;
        float[] g = new float[0];
        float[] down = { 392, 349, 294, 220 };
        for (int i = 0; i < down.Length; i++) g = Mix(g, Tone(down[i], 0.3f, false, 0.1f, i * 0.16f));
        gameOver = Clip("gameover", g);

        ambient.clip = Clip("ambient", Ambient(), 1f);
    }

    public void PlayLobby() { if (lobby.clip != null && !lobby.isPlaying) lobby.Play(); }
    public void StartAmbient()
    {
        if (lobby.isPlaying) lobby.Stop(); // ゲームが始まったらロビーのBGMからステージのBGMに切り替える
        if (!ambient.isPlaying) ambient.Play();
        if (bgm.clip != null && !bgm.isPlaying) bgm.Play();
    }
    public void StopBgm() { if (bgm.isPlaying) bgm.Stop(); }
    public void PlayHitBuzz() => sfx.PlayOneShot(hitBuzz);
    public void PlayDownChime() => sfx.PlayOneShot(downChime);
    public void PlayHurt() => sfx.PlayOneShot(hurt);
    public void PlayPulse() => sfx.PlayOneShot(pulse);
    public void PlayRumble() => sfx.PlayOneShot(rumble);
    public void PlayThunder() => sfx.PlayOneShot(thunder);
    public void PlayClear() => sfx.PlayOneShot(clear);
    public void PlayGameOver() => sfx.PlayOneShot(gameOver);
    public void PlayCountBeep() => sfx.PlayOneShot(countBeep);
    public void PlayCountGo() => sfx.PlayOneShot(countGo);
    public void PlayOrbReady() => sfx.PlayOneShot(orbReady);
    public void PlayOrbGet() => sfx.PlayOneShot(orbGet);
    // ホタルチャージ中の音。progress(0〜1)が進むほど高い音にする(最後は約2.5倍の高さ)
    public void PlayOrbCharge(float progress)
    {
        charge.pitch = 1f + 1.5f * Mathf.Clamp01(progress);
        charge.PlayOneShot(orbCharge);
    }

    // boost: 効果音を大きくする倍率(1 を超えたところは割れないよう、なめらかに頭打ちにする)
    static AudioClip Clip(string name, float[] data, float boost = SfxBoost)
    {
        if (boost != 1f)
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] * boost;
                data[i] = v > 0.8f ? 0.8f + 0.2f * (float)System.Math.Tanh((v - 0.8f) / 0.2f)
                        : v < -0.8f ? -0.8f - 0.2f * (float)System.Math.Tanh((-v - 0.8f) / 0.2f) : v;
            }
        var clip = AudioClip.Create(name, Mathf.Max(1, data.Length), 1, SR, false);
        clip.SetData(data, 0);
        return clip;
    }

    // いちばん大きいところが peak になるように全体の大きさをそろえる(音割れを防ぐ)
    static float[] Normalize(float[] s, float peak)
    {
        float max = 0f;
        foreach (float v in s) max = Mathf.Max(max, Mathf.Abs(v));
        if (max < 1e-5f) return s;
        float k = peak / max;
        for (int i = 0; i < s.Length; i++) s[i] *= k;
        return s;
    }

    static float[] Mix(float[] a, float[] b)
    {
        var o = new float[Mathf.Max(a.Length, b.Length)];
        for (int i = 0; i < a.Length; i++) o[i] += a[i];
        for (int i = 0; i < b.Length; i++) o[i] += b[i];
        return o;
    }

    // WebAudioの「20msで立ち上がり、指数的に減衰」するエンベロープ
    static float Env(float t, float duration, float gain)
    {
        const float floor = 0.0001f;
        if (t < 0.02f) return floor * Mathf.Pow(gain / floor, t / 0.02f);
        float k = Mathf.Clamp01((t - 0.02f) / Mathf.Max(0.001f, duration - 0.02f));
        return gain * Mathf.Pow(floor / gain, k);
    }

    // サイン波またはノコギリ波。freqEnd を指定すると周波数を直線的に変化させる。
    static float[] Tone(float freq, float duration, bool saw, float gain, float delay = 0f, float freqEnd = -1f)
    {
        int offset = Mathf.CeilToInt(delay * SR);
        int n = Mathf.CeilToInt((duration + 0.05f) * SR);
        var s = new float[offset + n];
        float phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float f = freqEnd > 0 ? Mathf.Lerp(freq, freqEnd, Mathf.Clamp01(t / duration)) : freq;
            phase += f / SR;
            phase -= Mathf.Floor(phase);
            float w = saw ? phase * 2f - 1f : Mathf.Sin(phase * Mathf.PI * 2f);
            s[offset + i] = w * (t <= duration ? Env(t, duration, gain) : 0f);
        }
        return s;
    }

    // フィルタを掛けたノイズの一撃(雷の「バリッ」や「ゴロゴロ」)。カットオフは指数的に変化させる。
    static float[] Noise(bool highpass, float freqStart, float freqEnd, float duration, float gain, float delay = 0f)
    {
        int offset = Mathf.CeilToInt(delay * SR);
        int n = Mathf.CeilToInt((duration + 0.05f) * SR);
        var s = new float[offset + n];
        var rng = new System.Random(highpass ? 11 : 23);
        float low = 0, low2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float cutoff = freqStart * Mathf.Pow(freqEnd / freqStart, Mathf.Clamp01(t / duration));
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SR);
            float x = (float)(rng.NextDouble() * 2 - 1);
            low += (x - low) * a;
            low2 += (low - low2) * a;
            float y = highpass ? x - low2 : low2 * 1.6f;
            s[offset + i] = y * (t <= duration ? Env(t, duration, gain) : 0f);
        }
        return s;
    }

    // 雨(高域寄りの「サー」+低めの「ザーッ」)と、55Hz/58.5Hzの低いうなり。2秒でちょうど周期が揃うのでループの継ぎ目が出ない。
    static float[] Ambient()
    {
        int n = SR * 2;
        var s = new float[n];
        var rng = new System.Random(7);
        float l1 = 0, l1b = 0, h1 = 0, l2 = 0, l2b = 0, h2 = 0;
        float aHp1 = 1f - Mathf.Exp(-2f * Mathf.PI * 1400f / SR), aLp1 = 1f - Mathf.Exp(-2f * Mathf.PI * 7000f / SR);
        float aHp2 = 1f - Mathf.Exp(-2f * Mathf.PI * 350f / SR), aLp2 = 1f - Mathf.Exp(-2f * Mathf.PI * 1800f / SR);
        for (int i = 0; i < n; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1);
            h1 += (x - h1) * aHp1; float hp1 = x - h1;
            l1 += (hp1 - l1) * aLp1; l1b += (l1 - l1b) * aLp1;
            h2 += (x - h2) * aHp2; float hp2 = x - h2;
            l2 += (hp2 - l2) * aLp2; l2b += (l2 - l2b) * aLp2;
            float t = i / (float)SR;
            float drone = (Mathf.Sin(2f * Mathf.PI * 55f * t) + Mathf.Sin(2f * Mathf.PI * 58.5f * t)) * 0.026f;
            s[i] = l1b * 0.055f * 1.6f + l2b * 0.03f * 1.6f + drone;
        }
        // ノイズのループの継ぎ目をなじませる(最初と最後の20msをクロスフェード)
        int fade = SR / 50;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            s[i] = s[i] * k + s[n - fade + i] * (1f - k);
        }
        System.Array.Resize(ref s, n - fade);
        return s;
    }
}
