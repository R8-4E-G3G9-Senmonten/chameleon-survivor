using UnityEngine;

// 光の球収集版の効果音。モックアップ(WebAudio)の音を、波形をコードで生成して再現する。
// 常時: 雨音+低いうなり。閃光の前に雷鳴の予兆、閃光で落雷音。
public class NightAudio : MonoBehaviour
{
    const int SR = 44100;
    public const float FlashWarn = 1.3f;

    AudioSource sfx, ambient;
    AudioClip hitBuzz, downChime, hurt, pulse, rumble, thunder, clear, gameOver;

    void Awake()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        ambient = gameObject.AddComponent<AudioSource>();
        ambient.playOnAwake = false;
        ambient.loop = true;

        hitBuzz = Clip("hit", Tone(140, 0.08f, true, 0.05f, 0, 90));
        downChime = Clip("down", Mix(Tone(700, 0.1f, false, 0.09f), Tone(1000, 0.14f, false, 0.08f, 0.07f)));
        hurt = Clip("hurt", Tone(180, 0.16f, true, 0.1f, 0, 70));
        pulse = Clip("pulse", Tone(220, 0.3f, false, 0.12f, 0, 660));
        rumble = Clip("rumble", Mix(Noise(false, 200, 120, FlashWarn, 0.12f), Tone(58, FlashWarn, false, 0.06f, 0, 90)));
        thunder = Clip("thunder", Mix(Mix(Noise(true, 2500, 900, 0.35f, 0.22f), Noise(false, 500, 60, 2.6f, 0.5f, 0.12f)),
            Tone(70, 1.8f, false, 0.14f, 0.12f, 30)));
        float[] c = new float[0];
        float[] notes = { 523, 659, 784, 1047, 1319 };
        for (int i = 0; i < notes.Length; i++) c = Mix(c, Tone(notes[i], 0.22f, false, 0.1f, i * 0.1f));
        clear = Clip("clear", c);
        float[] g = new float[0];
        float[] down = { 392, 349, 294, 220 };
        for (int i = 0; i < down.Length; i++) g = Mix(g, Tone(down[i], 0.3f, false, 0.1f, i * 0.16f));
        gameOver = Clip("gameover", g);

        ambient.clip = Clip("ambient", Ambient());
    }

    public void StartAmbient() { if (!ambient.isPlaying) ambient.Play(); }
    public void PlayHitBuzz() => sfx.PlayOneShot(hitBuzz);
    public void PlayDownChime() => sfx.PlayOneShot(downChime);
    public void PlayHurt() => sfx.PlayOneShot(hurt);
    public void PlayPulse() => sfx.PlayOneShot(pulse);
    public void PlayRumble() => sfx.PlayOneShot(rumble);
    public void PlayThunder() => sfx.PlayOneShot(thunder);
    public void PlayClear() => sfx.PlayOneShot(clear);
    public void PlayGameOver() => sfx.PlayOneShot(gameOver);

    static AudioClip Clip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, Mathf.Max(1, data.Length), 1, SR, false);
        clip.SetData(data, 0);
        return clip;
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
