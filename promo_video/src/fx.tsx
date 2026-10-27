// 紹介動画で使う演出部品(文字の飛び出し・揺れ・フラッシュ・集中線・紙吹雪・場面の入り方 など)
import React from 'react';
import {
  AbsoluteFill,
  interpolate,
  OffthreadVideo,
  random,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from 'remotion';
import {loadFont} from '@remotion/fonts';

// フォントは public/fonts に置いたもの(どちらも SIL Open Font License)。ネットから読むと回線が不安定な時に書き出しが失敗するため
export const HEAVY = 'Dela Gothic One'; // 極太(見出し)
export const POP = 'Mochiy Pop One'; // 丸っこい(小見出し・ステッカー)
loadFont({family: HEAVY, url: staticFile('fonts/DelaGothicOne-Regular.ttf')});
loadFont({family: POP, url: staticFile('fonts/MochiyPopOne-Regular.ttf')});

// BGM(ドラムンベース1)は 172BPM。1拍 = 30fps で約10.47フレーム。カットや演出はこの拍に合わせる
export const BEAT = (30 * 60) / 172;
export const b = (beats: number) => Math.round(beats * BEAT);

// 灯り役(スマホ)の色。ゲームと同じ並び
export const LIGHT_COLORS = ['#ff6b6b', '#6bdfff', '#ffe066', '#b388ff', '#6bff95', '#ff9f6b', '#ff6bd6'];

const clamp = {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'} as const;

// ---- ゲームの録画 ----
// from: 録画(30fps)の何フレーム目から再生するか。暗い夜のゲームなので明るさと色を持ち上げる
export const Footage: React.FC<{
  from: number;
  dur: number;
  rate?: number;
  zoom?: [number, number];
  focus?: [number, number];
  filter?: string;
}> = ({from, dur, rate = 1, zoom = [1.12, 1.3], focus = [50, 45], filter = ''}) => {
  const f = useCurrentFrame();
  const z = interpolate(f, [0, dur], zoom, clamp);
  return (
    <AbsoluteFill style={{overflow: 'hidden', background: '#000'}}>
      <OffthreadVideo
        src={staticFile('footage.mp4')}
        startFrom={from}
        playbackRate={rate}
        muted
        style={{
          width: '100%',
          height: '100%',
          objectFit: 'cover',
          transform: `scale(${z})`,
          transformOrigin: `${focus[0]}% ${focus[1]}%`,
          filter: `brightness(1.5) contrast(1.12) saturate(1.5) ${filter}`,
        }}
      />
    </AbsoluteFill>
  );
};

// ---- 文字 ----
type TextProps = {
  text: string;
  at?: number; // 出てくるフレーム
  size?: number;
  color?: string;
  stroke?: string;
  x?: number; // 中心からのずれ(px)
  y?: number;
  rotate?: number;
  font?: string;
  from?: 'pop' | 'drop' | 'left' | 'right' | 'spin';
  rainbow?: boolean;
  until?: number; // 消えるフレーム
};

const strokeStyle = (size: number, stroke: string): React.CSSProperties => ({
  WebkitTextStroke: `${Math.max(6, size * 0.12)}px ${stroke}`,
  paintOrder: 'stroke fill',
  textShadow: `0 ${size * 0.08}px 0 ${stroke}, 0 ${size * 0.12}px ${size * 0.25}px rgba(0,0,0,0.6)`,
});

export const Punch: React.FC<TextProps> = ({
  text,
  at = 0,
  size = 150,
  color = '#fff',
  stroke = '#111',
  x = 0,
  y = 0,
  rotate = -4,
  font = HEAVY,
  from = 'pop',
  rainbow = false,
  until,
}) => {
  const f = useCurrentFrame();
  const {fps} = useVideoConfig();
  if (f < at || (until !== undefined && f >= until)) return null;
  const s = spring({frame: f - at, fps, config: {damping: 9, stiffness: 220, mass: 0.6}});
  let transform = '';
  if (from === 'pop') transform = `scale(${interpolate(s, [0, 1], [2.6, 1])})`;
  if (from === 'drop') transform = `translateY(${interpolate(s, [0, 1], [-700, 0])}px)`;
  if (from === 'left') transform = `translateX(${interpolate(s, [0, 1], [-1400, 0])}px) skewX(${interpolate(s, [0, 1], [-30, 0])}deg)`;
  if (from === 'right') transform = `translateX(${interpolate(s, [0, 1], [1400, 0])}px) skewX(${interpolate(s, [0, 1], [30, 0])}deg)`;
  if (from === 'spin') transform = `rotate(${interpolate(s, [0, 1], [-200, 0])}deg) scale(${s})`;
  const wobble = Math.sin((f - at) / 4) * 1.5;
  const out = until !== undefined ? interpolate(f, [until - 4, until], [1, 0], clamp) : 1;
  // 虹色は1文字ずつ色を変える(背景を文字で切り抜く方法だと、縁取りと影で黒くつぶれるため)
  const body = rainbow
    ? [...text].map((ch, i) => (
        <span key={i} style={{color: ch === '\n' ? undefined : LIGHT_COLORS[(i + Math.floor(f / 4)) % 7]}}>
          {ch}
        </span>
      ))
    : <span style={{color}}>{text}</span>;
  return (
    <AbsoluteFill style={{justifyContent: 'center', alignItems: 'center', pointerEvents: 'none'}}>
      <div
        style={{
          transform: `translate(${x}px, ${y}px) rotate(${rotate + wobble}deg) ${transform}`,
          opacity: Math.min(1, (f - at) / 2) * out,
          fontFamily: font,
          fontSize: size,
          lineHeight: 1.12,
          textAlign: 'center',
          whiteSpace: 'pre',
          ...strokeStyle(size, stroke),
        }}
      >
        {body}
      </div>
    </AbsoluteFill>
  );
};

// 1文字ずつ跳ねながら落ちてくる文字
export const BounceChars: React.FC<{
  text: string;
  at?: number;
  step?: number;
  size?: number;
  y?: number;
  colors?: string[];
  stroke?: string;
}> = ({text, at = 0, step = 3, size = 170, y = 0, colors = LIGHT_COLORS, stroke = '#111'}) => {
  const f = useCurrentFrame();
  const {fps} = useVideoConfig();
  return (
    <AbsoluteFill style={{justifyContent: 'center', alignItems: 'center'}}>
      <div style={{display: 'flex', transform: `translateY(${y}px)`}}>
        {[...text].map((ch, i) => {
          const t = f - at - i * step;
          if (t < 0) return <span key={i} style={{fontSize: size, opacity: 0, fontFamily: HEAVY}}>{ch}</span>;
          const s = spring({frame: t, fps, config: {damping: 7, stiffness: 260, mass: 0.5}});
          const hop = Math.sin((f + i * 5) / 5) * 6;
          return (
            <span
              key={i}
              style={{
                fontFamily: HEAVY,
                fontSize: size,
                color: colors[(i + Math.floor(f / 6)) % colors.length],
                display: 'inline-block',
                transform: `translateY(${interpolate(s, [0, 1], [-600, 0]) + hop}px) rotate(${interpolate(s, [0, 1], [-40, (i % 2 ? 4 : -4)])}deg)`,
                ...strokeStyle(size, stroke),
              }}
            >
              {ch}
            </span>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};

// 斜めに貼られるステッカー
export const Sticker: React.FC<{
  text: string;
  at?: number;
  x: number;
  y: number;
  bg?: string;
  color?: string;
  size?: number;
  rotate?: number;
}> = ({text, at = 0, x, y, bg = '#ffe066', color = '#111', size = 54, rotate = -8}) => {
  const f = useCurrentFrame();
  const {fps} = useVideoConfig();
  if (f < at) return null;
  const s = spring({frame: f - at, fps, config: {damping: 8, stiffness: 300, mass: 0.5}});
  return (
    <div
      style={{
        position: 'absolute',
        left: 960 + x,
        top: 540 + y,
        transform: `translate(-50%, -50%) rotate(${rotate + Math.sin(f / 6) * 2}deg) scale(${s})`,
        background: bg,
        color,
        fontFamily: POP,
        fontSize: size,
        padding: `${size * 0.22}px ${size * 0.5}px`,
        borderRadius: size,
        border: `${Math.max(5, size * 0.1)}px solid #111`,
        boxShadow: `0 ${size * 0.15}px 0 #111`,
        whiteSpace: 'pre',
        textAlign: 'center',
      }}
    >
      {text}
    </div>
  );
};

// ---- 画面全体の演出 ----
export const Flash: React.FC<{at: number; color?: string; len?: number; peak?: number}> = ({
  at,
  color = '#fff',
  len = 8,
  peak = 0.95,
}) => {
  const f = useCurrentFrame();
  if (f < at || f > at + len) return null;
  return <AbsoluteFill style={{background: color, opacity: peak * (1 - (f - at) / len)}} />;
};

// 揺れ: hits のフレームで大きく揺れて、すぐ収まる
export const Shake: React.FC<{hits: number[]; power?: number; children: React.ReactNode}> = ({
  hits,
  power = 34,
  children,
}) => {
  const f = useCurrentFrame();
  let a = 0;
  for (const h of hits) if (f >= h) a += Math.exp(-(f - h) / 5);
  const dx = (random(`sx${f}`) - 0.5) * 2 * power * a;
  const dy = (random(`sy${f}`) - 0.5) * 2 * power * a;
  const rot = (random(`sr${f}`) - 0.5) * 3 * a;
  return <AbsoluteFill style={{transform: `translate(${dx}px, ${dy}px) rotate(${rot}deg) scale(${1 + 0.06 * Math.min(1, a)})`}}>{children}</AbsoluteFill>;
};

// 集中線
export const SpeedLines: React.FC<{color?: string; opacity?: number; count?: number}> = ({
  color = '#fff',
  opacity = 0.55,
  count = 70,
}) => {
  const f = useCurrentFrame();
  const lines = [];
  for (let i = 0; i < count; i++) {
    const a = (i / count) * Math.PI * 2 + random(`a${i}`) * 0.08;
    const r0 = 380 + random(`r${i}-${Math.floor(f / 2)}`) * 260;
    const w = 4 + random(`w${i}`) * 14;
    const x1 = 960 + Math.cos(a) * r0;
    const y1 = 540 + Math.sin(a) * r0;
    const x2 = 960 + Math.cos(a) * 1500;
    const y2 = 540 + Math.sin(a) * 1500;
    const n = Math.cos(a + Math.PI / 2) * w;
    const m = Math.sin(a + Math.PI / 2) * w;
    lines.push(<polygon key={i} points={`${x1},${y1} ${x2 + n},${y2 + m} ${x2 - n},${y2 - m}`} fill={color} />);
  }
  return (
    <AbsoluteFill style={{opacity}}>
      <svg width={1920} height={1080}>{lines}</svg>
    </AbsoluteFill>
  );
};

// 回るギザギザの爆発マーク(文字の後ろに置く)
export const Burst: React.FC<{color?: string; size?: number; x?: number; y?: number; at?: number; points?: number}> = ({
  color = '#ffe066',
  size = 520,
  x = 0,
  y = 0,
  at = 0,
  points = 14,
}) => {
  const f = useCurrentFrame();
  const {fps} = useVideoConfig();
  if (f < at) return null;
  const s = spring({frame: f - at, fps, config: {damping: 10, stiffness: 200}});
  const pts = [];
  for (let i = 0; i < points * 2; i++) {
    const r = i % 2 ? size * 0.36 : size * 0.5;
    const a = (i / (points * 2)) * Math.PI * 2;
    pts.push(`${size / 2 + Math.cos(a) * r},${size / 2 + Math.sin(a) * r}`);
  }
  return (
    <div
      style={{
        position: 'absolute',
        left: 960 + x - size / 2,
        top: 540 + y - size / 2,
        width: size,
        height: size,
        transform: `rotate(${(f - at) * 2}deg) scale(${s})`,
      }}
    >
      <svg width={size} height={size}>
        <polygon points={pts.join(' ')} fill={color} stroke="#111" strokeWidth={10} strokeLinejoin="round" />
      </svg>
    </div>
  );
};

// 紙吹雪
export const Confetti: React.FC<{count?: number; at?: number}> = ({count = 90, at = 0}) => {
  const f = useCurrentFrame() - at;
  if (f < 0) return null;
  return (
    <AbsoluteFill style={{pointerEvents: 'none'}}>
      {new Array(count).fill(0).map((_, i) => {
        const x = random(`cx${i}`) * 1920 + Math.sin((f + i * 7) / 9) * 40;
        const y = -60 + ((f * (6 + random(`cv${i}`) * 8) + random(`cy${i}`) * 1200) % 1240);
        const w = 14 + random(`cw${i}`) * 16;
        return (
          <div
            key={i}
            style={{
              position: 'absolute',
              left: x,
              top: y,
              width: w,
              height: w * 0.55,
              background: LIGHT_COLORS[i % 7],
              transform: `rotate(${f * (4 + i % 5) + i * 30}deg) scaleY(${Math.cos((f + i) / 4)})`,
              borderRadius: 3,
            }}
          />
        );
      })}
    </AbsoluteFill>
  );
};

// きらきら(蛍・星)
export const Sparkles: React.FC<{count?: number; color?: string}> = ({count = 40, color = '#d7ff6b'}) => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill style={{pointerEvents: 'none'}}>
      {new Array(count).fill(0).map((_, i) => {
        const x = random(`sx${i}`) * 1920 + Math.sin((f + i * 11) / 14) * 50;
        const y = 1080 - ((f * (2 + random(`sv${i}`) * 4) + random(`sy${i}`) * 1080) % 1180);
        const tw = 0.5 + 0.5 * Math.sin((f + i * 13) / 3);
        const s = 10 + random(`ss${i}`) * 22;
        return (
          <div
            key={i}
            style={{
              position: 'absolute',
              left: x,
              top: y,
              width: s,
              height: s,
              borderRadius: '50%',
              background: color,
              opacity: tw,
              boxShadow: `0 0 ${s * 2}px ${s}px ${color}`,
            }}
          />
        );
      })}
    </AbsoluteFill>
  );
};

// 拍ごとに光る画面の縁
export const BeatBorder: React.FC<{color?: string; every?: number}> = ({color, every = 1}) => {
  const f = useCurrentFrame();
  const beat = Math.floor(f / BEAT);
  const ph = (f % (BEAT * every)) / 3;
  const c = color ?? LIGHT_COLORS[Math.floor(beat / every) % 7];
  return (
    <AbsoluteFill
      style={{
        boxShadow: `inset 0 0 0 ${10 + 16 * Math.exp(-ph)}px ${c}, inset 0 0 ${80 * Math.exp(-ph)}px ${c}`,
        opacity: 0.9,
        pointerEvents: 'none',
      }}
    />
  );
};

// 警告テープ(上下を流れる)
export const WarningTape: React.FC<{text: string}> = ({text}) => {
  const f = useCurrentFrame();
  const band = (top: boolean) => (
    <div
      style={{
        position: 'absolute',
        left: -200,
        right: -200,
        top: top ? 30 : undefined,
        bottom: top ? undefined : 30,
        height: 92,
        transform: `rotate(${top ? -2 : 2}deg)`,
        background: 'repeating-linear-gradient(-45deg, #ffd600 0 40px, #111 40px 80px)',
        border: '6px solid #111',
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          position: 'absolute',
          inset: 10,
          background: '#111',
          color: '#ffd600',
          fontFamily: HEAVY,
          fontSize: 50,
          lineHeight: '72px',
          whiteSpace: 'nowrap',
          transform: `translateX(${((top ? -1 : 1) * f * 14) % 900 - 450}px)`,
        }}
      >
        {(text + '　').repeat(12)}
      </div>
    </div>
  );
  return (
    <AbsoluteFill>
      {band(true)}
      {band(false)}
    </AbsoluteFill>
  );
};

// 雷のいなずま
export const Bolt: React.FC<{at: number; x?: number}> = ({at, x = 0}) => {
  const f = useCurrentFrame();
  if (f < at || f > at + 14) return null;
  const on = (f - at) % 3 !== 2;
  return (
    <AbsoluteFill style={{opacity: on ? 1 : 0.2}}>
      <svg width={1920} height={1080} style={{filter: 'drop-shadow(0 0 30px #fff) drop-shadow(0 0 60px #9be7ff)'}}>
        <polyline
          points={`${960 + x},-20 ${900 + x},260 ${1010 + x},300 ${880 + x},620 ${980 + x},650 ${860 + x},1100`}
          fill="none"
          stroke="#fff"
          strokeWidth={26}
          strokeLinejoin="miter"
        />
        <polyline
          points={`${900 + x},260 ${760 + x},420 ${800 + x},500`}
          fill="none"
          stroke="#fff"
          strokeWidth={12}
        />
      </svg>
    </AbsoluteFill>
  );
};

// 衝撃波の輪
export const Shockwave: React.FC<{at: number; color?: string; x?: number; y?: number}> = ({at, color = '#ffe066', x = 0, y = 0}) => {
  const f = useCurrentFrame();
  const t = f - at;
  if (t < 0 || t > 22) return null;
  const r = interpolate(t, [0, 22], [40, 1300]);
  return (
    <div
      style={{
        position: 'absolute',
        left: 960 + x - r,
        top: 540 + y - r,
        width: r * 2,
        height: r * 2,
        borderRadius: '50%',
        border: `${interpolate(t, [0, 22], [60, 4])}px solid ${color}`,
        opacity: interpolate(t, [0, 22], [1, 0]),
        boxShadow: `0 0 60px ${color}`,
      }}
    />
  );
};

// 上下の黒帯(スローモーションの映画っぽさ)
export const Letterbox: React.FC<{at?: number}> = ({at = 0}) => {
  const f = useCurrentFrame();
  const h = interpolate(f - at, [0, 6], [0, 120], clamp);
  return (
    <AbsoluteFill>
      <div style={{position: 'absolute', top: 0, left: 0, right: 0, height: h, background: '#000'}} />
      <div style={{position: 'absolute', bottom: 0, left: 0, right: 0, height: h, background: '#000'}} />
    </AbsoluteFill>
  );
};

// グリッチ(色ずれの横じま)
export const Glitch: React.FC<{at: number; len?: number}> = ({at, len = 7}) => {
  const f = useCurrentFrame();
  if (f < at || f > at + len) return null;
  return (
    <AbsoluteFill style={{mixBlendMode: 'screen'}}>
      {new Array(9).fill(0).map((_, i) => {
        const y = random(`gy${i}${f}`) * 1080;
        const h = 12 + random(`gh${i}${f}`) * 90;
        return (
          <div
            key={i}
            style={{
              position: 'absolute',
              left: (random(`gx${i}${f}`) - 0.5) * 300,
              top: y,
              width: 1920,
              height: h,
              background: i % 2 ? 'rgba(0,255,255,0.55)' : 'rgba(255,0,200,0.55)',
            }}
          />
        );
      })}
    </AbsoluteFill>
  );
};

// ---- 場面の入り方 ----
export type EnterKind = 'zoom' | 'whip' | 'up' | 'spin' | 'iris' | 'shutter' | 'glitch' | 'diag' | 'none';

export const Enter: React.FC<{kind: EnterKind; children: React.ReactNode}> = ({kind, children}) => {
  const f = useCurrentFrame();
  const p = interpolate(f, [0, 8], [0, 1], {...clamp, easing: (t) => 1 - Math.pow(1 - t, 3)});
  let style: React.CSSProperties = {};
  if (kind === 'zoom') style = {transform: `scale(${interpolate(p, [0, 1], [2.2, 1])})`, filter: `blur(${(1 - p) * 18}px)`};
  if (kind === 'whip') style = {transform: `translateX(${(1 - p) * 1920}px) skewX(${(1 - p) * -25}deg)`, filter: `blur(${(1 - p) * 30}px)`};
  if (kind === 'up') style = {transform: `translateY(${(1 - p) * 1080}px)`};
  if (kind === 'spin') style = {transform: `rotate(${(1 - p) * -30}deg) scale(${0.3 + 0.7 * p})`};
  if (kind === 'iris') style = {clipPath: `circle(${p * 120}% at 50% 50%)`};
  if (kind === 'shutter') style = {clipPath: `inset(0 ${(1 - p) * 50}% 0 ${(1 - p) * 50}%)`};
  if (kind === 'diag') style = {clipPath: `polygon(0 0, ${p * 220}% 0, ${p * 220 - 120}% 100%, 0 100%)`};
  if (kind === 'glitch')
    style = {
      transform: f < 8 ? `translateX(${(random(`gl${f}`) - 0.5) * 120}px)` : undefined,
      filter: f < 8 ? `hue-rotate(${random(`gh${f}`) * 360}deg) saturate(2)` : undefined,
    };
  return (
    <AbsoluteFill style={style}>
      {children}
      {kind === 'glitch' && <Glitch at={0} len={8} />}
    </AbsoluteFill>
  );
};
