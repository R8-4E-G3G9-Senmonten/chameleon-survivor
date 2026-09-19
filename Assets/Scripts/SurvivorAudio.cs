using UnityEngine;

// 効果音。敵を倒した時の音だけを鳴らす(常時鳴る音や被ダメージ音は、うるさく不快になるため入れていない)。
// 音声ファイルを使わず、波形をコードで生成する(SpriteFactory等と同じく素材管理が不要)。
public class SurvivorAudio : MonoBehaviour
{
    const int SampleRate = 44100;

    AudioSource sfx;
    AudioClip downClip;

    void Awake()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        downClip = CreateDownChime();
    }

    public void PlayDown() => sfx.PlayOneShot(downClip);

    // 700Hz→1000Hzの短い2音のチャイム
    static AudioClip CreateDownChime()
    {
        float[] a = Tone(700f, 0.10f, 0.09f);
        float[] b = Tone(1000f, 0.14f, 0.08f);
        int offsetB = Mathf.CeilToInt(0.07f * SampleRate);
        var data = new float[Mathf.Max(a.Length, offsetB + b.Length)];
        for (int i = 0; i < a.Length; i++) data[i] += a[i];
        for (int i = 0; i < b.Length; i++) data[offsetB + i] += b[i];

        var clip = AudioClip.Create("down", data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float[] Tone(float freq, float duration, float gain)
    {
        int n = Mathf.CeilToInt(duration * SampleRate);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float attack = Mathf.Clamp01(i / (0.02f * SampleRate)); // 立ち上がり20ms
            float env = attack * (1f - t) * (1f - t);               // なめらかに減衰
            s[i] = Mathf.Sin(2f * Mathf.PI * freq * i / SampleRate) * env * gain;
        }
        return s;
    }
}
