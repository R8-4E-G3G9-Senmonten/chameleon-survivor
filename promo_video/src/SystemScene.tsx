// システム構成図(シーン版): 会場の様子をそのまま描き、光る線で機器のつながりを示す。
// `npx remotion still SystemScene out/system_scene.png` で書き出す。
import React from 'react';
import {AbsoluteFill, Img, staticFile} from 'remotion';
import {HEAVY, LIGHT_COLORS, POP} from './fx';
import {ChipIcon, LaptopIcon, MatIcon, PhoneIcon} from './SystemDiagram';

export const SCENE_W = 1920;
export const SCENE_H = 1080;

const BODY = "'Yu Gothic UI', 'Meiryo', sans-serif";
const LINE = {signal: '#6bff95', usb: '#f2f2f2', wifi: '#6bdfff', video: '#ffe066'};

// 画面の中の、灯り役の光が当たっている場所
const SPOTS: {from: [number, number]; to: [number, number]; color: string}[] = [
  {from: [1010, 585], to: [600, 330], color: LIGHT_COLORS[0]},
  {from: [1110, 625], to: [430, 420], color: LIGHT_COLORS[1]},
  {from: [1050, 715], to: [700, 470], color: LIGHT_COLORS[2]},
];

const Glow: React.FC<{d: string; color: string; dashed?: boolean; width?: number}> = ({d, color, dashed, width = 6}) => (
  <>
    <path d={d} fill="none" stroke={color} strokeWidth={width + 10} opacity={0.18} strokeLinecap="round" />
    <path d={d} fill="none" stroke={color} strokeWidth={width} strokeLinecap="round" strokeDasharray={dashed ? '14 12' : undefined} />
  </>
);

const Tag: React.FC<{x: number; y: number; text: string; color: string}> = ({x, y, text, color}) => (
  <div
    style={{
      position: 'absolute', left: x, top: y, transform: 'translate(-50%, -50%)', padding: '6px 16px', borderRadius: 20,
      background: '#0d1b16', border: `3px solid ${color}`, color, fontFamily: POP, fontSize: 24, whiteSpace: 'nowrap',
    }}
  >
    {text}
  </div>
);

const Caption: React.FC<{x: number; y: number; who: string; what: string; align?: 'left' | 'center'}> = ({x, y, who, what, align = 'center'}) => (
  <div style={{position: 'absolute', left: x, top: y, transform: align === 'center' ? 'translateX(-50%)' : undefined, textAlign: align, color: '#fff'}}>
    <div style={{fontFamily: POP, fontSize: 34, lineHeight: 1.2}}>{who}</div>
    <div style={{fontFamily: BODY, fontSize: 22, fontWeight: 700, color: '#bfe8cf', marginTop: 2}}>{what}</div>
  </div>
);

// 後ろ姿の人(マットの上に立つ主人公役)
const Person: React.FC = () => (
  <svg width={120} height={170} viewBox="0 0 120 170">
    <circle cx={60} cy={30} r={24} fill="#f3c9a0" stroke="#111" strokeWidth={4} />
    <path d="M38 22 C40 4 80 4 82 22 C76 14 44 14 38 22 Z" fill="#3a2a1e" />
    <path d="M24 80 C24 58 96 58 96 80 L92 118 L28 118 Z" fill="#4fd17a" stroke="#111" strokeWidth={4} strokeLinejoin="round" />
    <rect x={36} y={116} width={20} height={44} rx={8} fill="#2b3a55" stroke="#111" strokeWidth={4} />
    <rect x={64} y={116} width={20} height={44} rx={8} fill="#2b3a55" stroke="#111" strokeWidth={4} />
  </svg>
);

const Step: React.FC<{n: number; title: string; text: string; color: string}> = ({n, title, text, color}) => (
  <div style={{display: 'flex', gap: 16, alignItems: 'flex-start', marginBottom: 30}}>
    <div
      style={{
        flexShrink: 0, width: 54, height: 54, borderRadius: 27, background: color, color: '#0d1b16', fontFamily: HEAVY, fontSize: 30,
        display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: `0 0 24px ${color}`,
      }}
    >
      {n}
    </div>
    <div>
      <div style={{fontFamily: POP, fontSize: 30, color: '#fff', lineHeight: 1.25}}>{title}</div>
      <div style={{fontFamily: BODY, fontSize: 20, fontWeight: 600, color: '#a9c8b8', marginTop: 4, lineHeight: 1.45}}>{text}</div>
    </div>
  </div>
);

export const SystemScene: React.FC = () => (
  <AbsoluteFill style={{background: 'radial-gradient(circle at 35% 40%, #1d3a2b 0%, #0d1b16 60%, #08120e 100%)', overflow: 'hidden'}}>
    {/* 星 */}
    {Array.from({length: 40}).map((_, i) => (
      <div key={i} style={{position: 'absolute', left: (i * 397) % 1920, top: (i * 211) % 140, width: 3, height: 3, borderRadius: 2, background: '#cfe9dc', opacity: 0.25 + (i % 3) * 0.2}} />
    ))}

    {/* タイトル */}
    <Img src={staticFile('diagram/logo.png')} style={{position: 'absolute', left: 40, top: 26, height: 96}} />
    <div style={{position: 'absolute', left: 540, top: 52, fontFamily: POP, fontSize: 36, color: '#e6f5ec', letterSpacing: 6}}>システム構成</div>

    {/* ディスプレイ(ゲーム画面) */}
    <div style={{position: 'absolute', left: 80, top: 170, width: 800, height: 450, borderRadius: 12, border: '12px solid #1c2126', boxShadow: '0 0 0 3px #3b444c, 0 0 50px rgba(160,255,190,0.18)', overflow: 'hidden', background: '#000'}}>
      <Img src={staticFile('diagram/game.png')} style={{width: '100%', height: '100%', objectFit: 'cover', filter: 'brightness(1.25) saturate(1.2)'}} />
    </div>

    <svg width={SCENE_W} height={SCENE_H} style={{position: 'absolute', left: 0, top: 0}}>
      {/* 灯り役の光(スマホ → 画面の中) */}
      {SPOTS.map((s, i) => (
        <React.Fragment key={i}>
          <polygon points={`${s.from[0] - 10},${s.from[1]} ${s.to[0] - 40},${s.to[1]} ${s.to[0] + 40},${s.to[1]} ${s.from[0] + 10},${s.from[1]}`} fill={s.color} opacity={0.22} />
          <circle cx={s.to[0]} cy={s.to[1]} r={44} fill={s.color} opacity={0.35} />
          <circle cx={s.to[0]} cy={s.to[1]} r={44} fill="none" stroke={s.color} strokeWidth={4} />
        </React.Fragment>
      ))}
      {/* マット → ESP32(踏んだ信号) */}
      <Glow d="M 560 860 C 620 900, 660 930, 720 960" color={LINE.signal} />
      {/* ESP32 → パソコン(USB) */}
      <Glow d="M 850 975 C 960 1010, 1080 1000, 1180 965" color={LINE.usb} />
      {/* スマホ ⇄ パソコン(Wi-Fi / 5G) */}
      <Glow d="M 1100 790 C 1150 830, 1190 860, 1225 900" color={LINE.wifi} dashed />
      {/* パソコン → ディスプレイ(映像)。ここは主役ではないので細く控えめに */}
      <path d="M 1330 925 L 1380 925 L 1380 250 L 892 250" fill="none" stroke={LINE.video} strokeWidth={3} opacity={0.55} />
    </svg>

    {/* 機器 */}
    {SPOTS.map((s, i) => (
      <div key={i} style={{position: 'absolute', left: s.from[0] - 42, top: s.from[1] - 10, transform: `rotate(${-30 + i * 8}deg)`}}>
        <PhoneIcon color={s.color} size={1.2} />
      </div>
    ))}
    <div style={{position: 'absolute', left: 385, top: 760, transform: 'perspective(700px) rotateX(58deg) scale(1.15)', transformOrigin: 'center'}}>
      <MatIcon />
    </div>
    <div style={{position: 'absolute', left: 420, top: 660}}>
      <Person />
    </div>
    <div style={{position: 'absolute', left: 720, top: 925}}>
      <ChipIcon />
    </div>
    <div style={{position: 'absolute', left: 1180, top: 900}}>
      <LaptopIcon />
    </div>

    {/* 線のラベル */}
    <Tag x={655} y={865} text="踏んだ信号" color={LINE.signal} />
    <Tag x={1015} y={1035} text="USB" color={LINE.usb} />
    <Tag x={1250} y={820} text="Wi-Fi / 5G" color={LINE.wifi} />
    <div style={{position: 'absolute', left: 1392, top: 560, fontFamily: BODY, fontSize: 18, fontWeight: 700, color: LINE.video, opacity: 0.7}}>映像</div>

    {/* 役割の説明 */}
    <Caption x={250} y={890} who="主人公役" what="マットを踏んで動かす" />
    <Caption x={1110} y={430} who="灯り役(最大7人)" what="スマホで照らして倒す" />
    <Caption x={785} y={1015} who="ESP32" what="" />
    <Caption x={1255} y={1005} who="パソコン" what="" />

    {/* 流れ */}
    <div style={{position: 'absolute', left: 1450, top: 270, width: 430}}>
      <div style={{fontFamily: POP, fontSize: 30, color: '#ffe066', marginBottom: 26}}>遊びの流れ</div>
      <Step n={1} title="マットを踏む" text="主人公役が踏んだ向きに、カメレオンが動く" color={LINE.signal} />
      <Step n={2} title="ESP32が判定" text="踏まれたパッドをUSBでパソコンへ送る" color={LINE.usb} />
      <Step n={3} title="スマホで照らす" text="QRを読むだけで参加。光で敵を倒す" color={LINE.wifi} />
      <Step n={4} title="画面に映す" text="パソコンがゲームを動かし、ディスプレイとスマホに映す" color={LINE.video} />
    </div>
  </AbsoluteFill>
);
