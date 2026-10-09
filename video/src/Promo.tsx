import React, { useState } from "react";
import {
  AbsoluteFill,
  Audio,
  Img,
  Sequence,
  continueRender,
  delayRender,
  interpolate,
  random,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import data from "./chapters.json";

// ---- 全体の長さ ----
// 冒頭(つかみ+タイトル) → ゲーム映像(1.2倍速・16場面) → 締め
const SPEED = 1.2;
const INTRO = 105;
const OUTRO = 135;
const FOOTAGE = data.frames;
const BODY = Math.floor(FOOTAGE / SPEED);
export const totalFrames = INTRO + BODY + OUTRO;

const FONT = "RocknRoll";
const ACCENTS = ["#9dffb0", "#ffd76a", "#8fe6ff", "#ff8fc8", "#ffb15c"];

// 場面ごとの大見出し・説明・数字の演出(chapters.json の no と対応)
type Copy = { hype: string; sub: string; stat?: { value: number; unit: string } };
const COPY: Record<number, Copy> = {
  1: { hype: "協力して、夜を抜けろ", sub: "カメレオン × スマホの灯り役" },
  2: { hype: "選べる3マップ", sub: "やさしい / ふつう / むずかしい", stat: { value: 3, unit: "MAPS" } },
  3: { hype: "名前を入れてGO!", sub: "灯り役はQRを読むだけで参加" },
  4: { hype: "足踏みで進め！", sub: "圧力マットでカメレオンを操作" },
  5: { hype: "スマホが懐中電灯に！", sub: "光を当て続けて敵を倒す", stat: { value: 7, unit: "人まで参加" } },
  6: { hype: "倒して回復！", sub: "緑のオーブでHPアップ" },
  7: { hype: "止まれば、消える。", sub: "地面の色に擬態して敵をまく", stat: { value: 3, unit: "秒 無敵" } },
  8: { hype: "見つかった！", sub: "擬態ゲージ切れに注意" },
  9: { hype: "光をためろ", sub: "照らした時間がたまっていく" },
  10: { hype: "まとめて一掃！", sub: "点灯したランタンが周りの敵を倒す" },
  11: { hype: "道が光る", sub: "花を照らすと蛍の石までナビ" },
  12: { hype: "蛍を集めろ！", sub: "石を照らすと蛍が集まってくる", stat: { value: 2, unit: "か所" } },
  13: { hype: "敵が増えた！？", sub: "進むほど難しくなる" },
  14: { hype: "落雷ドーン！", sub: "約12秒ごとに森ごと攻撃" },
  15: { hype: "門が開いた！", sub: "2つの蛍で脱出ルート解放" },
  16: { hype: "脱出成功！", sub: "タイム＋撃破数でランキング" },
};

const chapters = data.chapters.map((c, i) => {
  const start = Math.floor(c.frame / SPEED);
  const next = i + 1 < data.chapters.length ? Math.floor(data.chapters[i + 1].frame / SPEED) : BODY;
  return { ...c, start, length: next - start, accent: ACCENTS[i % ACCENTS.length], copy: COPY[c.no] };
});

const frameSrc = (i: number) =>
  staticFile(`frames/f_${String(Math.max(0, Math.min(FOOTAGE - 1, i))).padStart(5, "0")}.jpg`);

// 太い縁取りの文字(ゲームのフォント)
const bigText = (size: number, color: string, stroke = 10): React.CSSProperties => ({
  fontFamily: FONT,
  fontSize: size,
  color,
  WebkitTextStroke: `${stroke}px #0a1410`,
  paintOrder: "stroke fill",
  textShadow: `0 6px 0 rgba(0,0,0,0.45), 0 0 28px ${color}88`,
  lineHeight: 1.1,
  whiteSpace: "nowrap",
});

// ---- 蛍の光の粒(全体にうっすら漂う) ----
const Fireflies: React.FC<{ count?: number }> = ({ count = 26 }) => {
  const frame = useCurrentFrame();
  const { width, height } = useVideoConfig();
  return (
    <AbsoluteFill style={{ pointerEvents: "none" }}>
      {new Array(count).fill(0).map((_, i) => {
        const x = random(`x${i}`) * width + Math.sin(frame / (30 + i) + i) * 40;
        const y = ((random(`y${i}`) * height - frame * (0.4 + random(`s${i}`) * 0.6)) % height + height) % height;
        const blink = 0.35 + 0.65 * Math.max(0, Math.sin(frame / 9 + i * 1.7));
        const r = 3 + random(`r${i}`) * 4;
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              left: x,
              top: y,
              width: r,
              height: r,
              borderRadius: r,
              background: "#efff9a",
              opacity: blink * 0.8,
              boxShadow: `0 0 ${r * 4}px ${r * 1.5}px rgba(220,255,120,0.55)`,
            }}
          />
        );
      })}
    </AbsoluteFill>
  );
};

// ---- 放射状の集中線(見出しが飛び出すときの勢い) ----
const SpeedLines: React.FC<{ color: string; t: number }> = ({ color, t }) => {
  const opacity = interpolate(t, [0, 4, 22], [0, 0.85, 0], { extrapolateRight: "clamp" });
  const grow = interpolate(t, [0, 22], [0.6, 1.25], { extrapolateRight: "clamp" });
  return (
    <svg width={1280} height={720} style={{ position: "absolute", opacity }}>
      {new Array(36).fill(0).map((_, i) => {
        const a = (i / 36) * Math.PI * 2 + random(`a${i}`) * 0.1;
        const r0 = 170 * grow, r1 = (380 + random(`l${i}`) * 300) * grow;
        return (
          <line
            key={i}
            x1={640 + Math.cos(a) * r0}
            y1={330 + Math.sin(a) * r0}
            x2={640 + Math.cos(a) * r1}
            y2={330 + Math.sin(a) * r1}
            stroke={i % 3 === 0 ? color : "#ffffff"}
            strokeWidth={i % 2 ? 3 : 6}
            strokeLinecap="round"
          />
        );
      })}
    </svg>
  );
};

// ---- 1つの場面の演出 ----
const ChapterOverlay: React.FC<{ index: number }> = ({ index }) => {
  const c = chapters[index];
  const t = useCurrentFrame(); // 場面の頭からのフレーム
  const { fps } = useVideoConfig();
  const pop = spring({ frame: t, fps, config: { damping: 9, stiffness: 160 } });
  const hypeOut = interpolate(t, [52, 64], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const scale = interpolate(pop, [0, 1], [2.4, 1]);
  const rot = interpolate(pop, [0, 1], [-10, -3]);
  const barIn = spring({ frame: t - 10, fps, config: { damping: 14 } });
  const statIn = spring({ frame: t - 14, fps, config: { damping: 8, stiffness: 140 } });
  const statOut = interpolate(t, [80, 92], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const count = c.copy.stat ? Math.round(interpolate(t, [14, 34], [0, c.copy.stat.value], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })) : 0;

  return (
    <AbsoluteFill>
      {/* 切り替わりの白い光 */}
      <AbsoluteFill style={{ background: "#fff", opacity: interpolate(t, [0, 7], [0.85, 0], { extrapolateRight: "clamp" }) }} />
      <AbsoluteFill style={{ background: c.accent, mixBlendMode: "overlay", opacity: interpolate(t, [0, 14], [0.5, 0], { extrapolateRight: "clamp" }) }} />

      <SpeedLines color={c.accent} t={t} />

      {/* 大見出し(画面中央に飛び出して、少しして消える) */}
      <AbsoluteFill style={{ justifyContent: "center", alignItems: "center", opacity: hypeOut, paddingBottom: 60 }}>
        <div style={{ transform: `scale(${scale}) rotate(${rot}deg)`, ...bigText(86, "#ffffff", 14) }}>
          {c.copy.hype}
        </div>
      </AbsoluteFill>

      {/* 数字の演出(右側でカウントアップ) */}
      {c.copy.stat && (
        <div
          style={{
            position: "absolute",
            right: 70,
            top: 120,
            transform: `scale(${statIn}) rotate(6deg)`,
            opacity: statOut,
            textAlign: "center",
          }}
        >
          <div
            style={{
              width: 230,
              height: 230,
              borderRadius: 230,
              background: `radial-gradient(circle, ${c.accent} 0%, ${c.accent}cc 55%, ${c.accent}00 72%)`,
              display: "flex",
              flexDirection: "column",
              justifyContent: "center",
              alignItems: "center",
            }}
          >
            <div style={bigText(120, "#ffffff", 12)}>{count}</div>
            <div style={{ ...bigText(30, "#ffffff", 8), marginTop: -6 }}>{c.copy.stat.unit}</div>
          </div>
        </div>
      )}

      {/* 下の帯: 場面の番号・名前・説明(場面のあいだずっと出る) */}
      <div
        style={{
          position: "absolute",
          left: 40,
          bottom: 42,
          display: "flex",
          alignItems: "stretch",
          transform: `translateX(${interpolate(barIn, [0, 1], [-700, 0])}px)`,
          filter: "drop-shadow(0 8px 18px rgba(0,0,0,0.55))",
        }}
      >
        <div
          style={{
            background: c.accent,
            color: "#0a1410",
            fontFamily: FONT,
            fontSize: 38,
            padding: "10px 18px",
            borderRadius: "14px 0 0 14px",
            display: "flex",
            alignItems: "center",
          }}
        >
          {String(c.no).padStart(2, "0")}
        </div>
        <div style={{ background: "rgba(6,16,14,0.88)", padding: "10px 26px 12px", borderRadius: "0 14px 14px 0", borderTop: `3px solid ${c.accent}` }}>
          <div style={{ fontFamily: FONT, fontSize: 30, color: c.accent }}>{c.title}</div>
          <div style={{ fontFamily: FONT, fontSize: 21, color: "#eef8f1", marginTop: 2 }}>{c.copy.sub}</div>
        </div>
      </div>
    </AbsoluteFill>
  );
};

// ---- ゲーム映像(場面の頭でズームとゆれ) ----
const Footage: React.FC = () => {
  const frame = useCurrentFrame();
  const src = Math.floor(frame * SPEED);
  let since = 999;
  for (const c of chapters) if (frame >= c.start) since = frame - c.start;
  const punch = Math.exp(-since / 6);
  const shakeX = Math.sin(frame * 2.7) * 9 * punch;
  const shakeY = Math.cos(frame * 3.1) * 7 * punch;
  return (
    <AbsoluteFill style={{ transform: `translate(${shakeX}px, ${shakeY}px) scale(${1.02 + 0.08 * punch})` }}>
      <Img src={frameSrc(src)} style={{ width: "100%", height: "100%" }} />
    </AbsoluteFill>
  );
};

// ---- 上の進み具合のバー ----
const Progress: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <div style={{ position: "absolute", left: 40, right: 40, top: 18, display: "flex", gap: 5 }}>
      {chapters.map((c, i) => {
        const fill = interpolate(frame, [c.start, c.start + c.length], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
        return (
          <div key={i} style={{ flex: c.length, height: 7, borderRadius: 4, background: "rgba(255,255,255,0.18)", overflow: "hidden" }}>
            <div style={{ width: `${fill * 100}%`, height: "100%", background: c.accent, boxShadow: `0 0 10px ${c.accent}` }} />
          </div>
        );
      })}
    </div>
  );
};

// 映像に映る、このPCのIPアドレス入りのQRコードと IP の文字をぼかす(共有する動画なので)
const Mask: React.FC<{ x: number; y: number; w: number; h: number }> = ({ x, y, w, h }) => (
  <div style={{ position: "absolute", left: x, top: y, width: w, height: h, backdropFilter: "blur(14px)", background: "rgba(10,20,16,0.35)", borderRadius: 8 }} />
);

const PrivacyMasks: React.FC = () => {
  const frame = useCurrentFrame();
  const onTitle = frame < chapters[3].start; // 01〜03: タイトル・名前入力の画面
  return onTitle ? (
    <>
      <Mask x={1026} y={28} w={254} h={252} />
      <Mask x={0} y={660} w={320} h={60} />
    </>
  ) : (
    <Mask x={976} y={590} w={136} h={130} />
  );
};

const Body: React.FC = () => (
  <AbsoluteFill>
    <Footage />
    <PrivacyMasks />
    <AbsoluteFill style={{ background: "radial-gradient(ellipse at center, rgba(0,0,0,0) 55%, rgba(0,0,0,0.55) 100%)" }} />
    {chapters.map((c, i) => (
      <Sequence key={c.no} from={c.start} durationInFrames={c.length}>
        <ChapterOverlay index={i} />
      </Sequence>
    ))}
    <Progress />
  </AbsoluteFill>
);

// ---- 冒頭: 短いカットで言葉を叩き込み、タイトルをドン ----
const HOOK = [
  { word: "夜の森で", frame: chapters[6].frame + 40, color: "#8fe6ff" },
  { word: "光が", frame: chapters[4].frame + 60, color: "#ffd76a" },
  { word: "武器になる！", frame: chapters[9].frame + 20, color: "#ff8fc8" },
];

const Intro: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const cut = 13;
  if (frame < cut * HOOK.length) {
    const k = Math.floor(frame / cut);
    const t = frame - k * cut;
    const h = HOOK[k];
    const s = spring({ frame: t, fps, config: { damping: 10, stiffness: 220 } });
    return (
      <AbsoluteFill style={{ background: "#000" }}>
        <Img src={frameSrc(h.frame + t)} style={{ width: "100%", height: "100%", transform: `scale(${1.25 - t * 0.01})`, filter: "brightness(1.7) saturate(1.4) contrast(1.1)" }} />
        <Mask x={1000} y={570} w={280} h={150} />
        <AbsoluteFill style={{ background: "#fff", opacity: interpolate(t, [0, 4], [0.9, 0], { extrapolateRight: "clamp" }) }} />
        <AbsoluteFill style={{ justifyContent: "center", alignItems: "center" }}>
          <div style={{ transform: `scale(${interpolate(s, [0, 1], [3, 1])}) rotate(-4deg)`, ...bigText(130, h.color, 16) }}>{h.word}</div>
        </AbsoluteFill>
      </AbsoluteFill>
    );
  }
  const t = frame - cut * HOOK.length;
  const title = "CHAMELEON NIGHT";
  return (
    <AbsoluteFill style={{ background: "radial-gradient(circle at 50% 45%, #1d4a3c 0%, #07120f 60%, #020605 100%)" }}>
      <AbsoluteFill style={{ background: "#fff", opacity: interpolate(t, [0, 8], [1, 0], { extrapolateRight: "clamp" }) }} />
      <SpeedLines color="#9dffb0" t={t} />
      <Fireflies count={40} />
      <AbsoluteFill style={{ justifyContent: "center", alignItems: "center", flexDirection: "column" }}>
        <div style={{ display: "flex" }}>
          {title.split("").map((ch, i) => {
            const s = spring({ frame: t - i * 1.5, fps, config: { damping: 9, stiffness: 180 } });
            return (
              <span key={i} style={{ ...bigText(96, "#e8fff0", 12), display: "inline-block", width: ch === " " ? 40 : undefined, transform: `translateY(${interpolate(s, [0, 1], [-260, 0])}px)`, opacity: s }}>
                {ch}
              </span>
            );
          })}
        </div>
        <div
          style={{
            ...bigText(52, "#9dffb0", 10),
            marginTop: 14,
            transform: `scale(${spring({ frame: t - 22, fps, config: { damping: 8 } })})`,
          }}
        >
          カメレオンナイト
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// ---- 締め ----
const Outro: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const l1 = spring({ frame, fps, config: { damping: 10 } });
  const l2 = spring({ frame: frame - 22, fps, config: { damping: 9, stiffness: 170 } });
  const l3 = spring({ frame: frame - 50, fps, config: { damping: 12 } });
  const fadeOut = interpolate(frame, [OUTRO - 18, OUTRO], [1, 0], { extrapolateLeft: "clamp" });
  return (
    <AbsoluteFill style={{ background: "#020605", opacity: fadeOut }}>
      <Img src={frameSrc(FOOTAGE - 40)} style={{ width: "100%", height: "100%", filter: "blur(10px) brightness(0.45)", transform: "scale(1.1)" }} />
      <Fireflies count={46} />
      <AbsoluteFill style={{ justifyContent: "center", alignItems: "center", flexDirection: "column", gap: 18 }}>
        <div style={{ ...bigText(44, "#ffffff", 9), opacity: l1, transform: `translateY(${(1 - l1) * 40}px)` }}>スマホでQRを読むだけ</div>
        <div style={{ ...bigText(92, "#ffd76a", 14), transform: `scale(${interpolate(l2, [0, 1], [2.2, 1])}) rotate(-3deg)`, opacity: Math.min(1, l2 * 2) }}>
          みんなで灯せ、夜の森。
        </div>
        <div style={{ display: "flex", gap: 16, opacity: l3, transform: `translateY(${(1 - l3) * 30}px)` }}>
          {["最大7人で協力", "3つのマップ", "ランキング"].map((s, i) => (
            <div key={s} style={{ fontFamily: FONT, fontSize: 26, color: "#0a1410", background: ACCENTS[i], padding: "8px 18px", borderRadius: 999 }}>
              {s}
            </div>
          ))}
        </div>
        <div style={{ ...bigText(40, "#e8fff0", 8), marginTop: 26, opacity: l3 }}>CHAMELEON NIGHT</div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

export const Promo: React.FC = () => {
  const { fps } = useVideoConfig();
  // ゲームと同じ RocknRoll One を読み込み終わるまで描画を待つ
  const [handle] = useState(() => {
    const h = delayRender("font");
    const face = new FontFace(FONT, `url(${staticFile("RocknRollOne-Regular.ttf")})`);
    face
      .load()
      .then(() => {
        document.fonts.add(face);
        continueRender(h);
      })
      .catch(() => continueRender(h));
    return h;
  });
  void handle;
  return (
    <AbsoluteFill style={{ background: "#000" }}>
      <Audio
        src={staticFile("bgm.mp3")}
        volume={(f) => interpolate(f, [0, fps * 0.5, totalFrames - fps * 1.5, totalFrames], [0, 0.85, 0.85, 0], { extrapolateRight: "clamp" })}
      />
      <Sequence durationInFrames={INTRO}>
        <Intro />
      </Sequence>
      <Sequence from={INTRO} durationInFrames={BODY}>
        <Body />
      </Sequence>
      <Sequence from={INTRO + BODY} durationInFrames={OUTRO}>
        <Outro />
      </Sequence>
    </AbsoluteFill>
  );
};
