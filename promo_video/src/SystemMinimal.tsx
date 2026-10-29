// システム構成図(ミニマル版): 薄いグレーの背景・黒の太い見出し・水色1色の線画で、余白を広くとった図。
// `npx remotion still SystemMinimal out/system_minimal.png` で書き出す。
import React from 'react';
import {AbsoluteFill, Img, staticFile} from 'remotion';
import {loadFont} from '@remotion/fonts';

loadFont({family: 'Sen', url: staticFile('fonts/Sen.ttf'), weight: '400 800'});

export const MINIMAL_W = 1920;
export const MINIMAL_H = 1080;

const FONT = "Sen, 'Yu Gothic UI', 'Meiryo', sans-serif";
const INK = '#212529';
const SUB = '#6c757d';
const CYAN = '#22c4d6';
const BG = '#f4f4f4';

// ---- 線画のアイコン(黒い細線 + 水色をひとさし) ----
const S = {fill: 'none', stroke: INK, strokeWidth: 4, strokeLinejoin: 'round' as const, strokeLinecap: 'round' as const};

export const MatLine: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    <rect x={8} y={8} width={134} height={134} rx={14} {...S} />
    <rect x={42} y={20} width={66} height={20} rx={5} fill={CYAN} stroke={INK} strokeWidth={4} />
    <rect x={42} y={110} width={66} height={20} rx={5} {...S} />
    <rect x={20} y={42} width={20} height={66} rx={5} {...S} />
    <rect x={110} y={42} width={20} height={66} rx={5} {...S} />
  </svg>
);

export const ChipLine: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    <rect x={30} y={30} width={90} height={90} rx={10} {...S} />
    <rect x={52} y={52} width={46} height={46} rx={4} fill={CYAN} opacity={0.9} />
    {[45, 65, 85, 105].map((v) => (
      <React.Fragment key={v}>
        <line x1={v} y1={30} x2={v} y2={14} {...S} />
        <line x1={v} y1={120} x2={v} y2={136} {...S} />
        <line x1={30} y1={v} x2={14} y2={v} {...S} />
        <line x1={120} y1={v} x2={136} y2={v} {...S} />
      </React.Fragment>
    ))}
  </svg>
);

export const LaptopLine: React.FC = () => (
  <svg width={170} height={150} viewBox="0 0 170 150">
    <rect x={28} y={24} width={114} height={78} rx={8} {...S} />
    <path d="M10 112 H160 L150 128 H20 Z" {...S} />
    <path d="M44 88 L66 62 L82 78 L100 54 L126 88" fill="none" stroke={CYAN} strokeWidth={5} strokeLinejoin="round" strokeLinecap="round" />
  </svg>
);

export const DisplayLine: React.FC = () => (
  <svg width={170} height={150} viewBox="0 0 170 150">
    <rect x={12} y={14} width={146} height={92} rx={8} {...S} />
    <line x1={85} y1={106} x2={85} y2={126} {...S} />
    <line x1={55} y1={130} x2={115} y2={130} {...S} />
    <circle cx={98} cy={60} r={20} fill={CYAN} opacity={0.35} />
    <circle cx={98} cy={60} r={20} fill="none" stroke={CYAN} strokeWidth={4} />
  </svg>
);

export const PhoneLine: React.FC<{dx?: number}> = () => (
  <svg width={170} height={150} viewBox="0 0 170 150">
    {[0, 1, 2].map((i) => (
      <g key={i} transform={`translate(${18 + i * 46}, ${i === 1 ? 6 : 18}) rotate(${(i - 1) * 6} 22 60)`}>
        <rect x={0} y={0} width={44} height={84 + (i === 1 ? 12 : 0)} rx={9} fill={BG} stroke={INK} strokeWidth={4} />
        <circle cx={22} cy={(84 + (i === 1 ? 12 : 0)) - 24} r={10} fill={i === 1 ? CYAN : 'none'} stroke={INK} strokeWidth={3} />
      </g>
    ))}
  </svg>
);

// ---- 部品 ----
const Node: React.FC<{x: number; y: number; icon: React.ReactNode; name: string; note: string}> = ({x, y, icon, name, note}) => (
  <div style={{position: 'absolute', left: x - 210, top: y - 130, width: 420, textAlign: 'center', fontFamily: FONT, color: INK}}>
    <div
      style={{
        width: 230, height: 230, margin: '0 auto', borderRadius: 30, background: '#fff', boxShadow: '0 10px 30px rgba(0,0,0,0.06)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
      }}
    >
      {icon}
    </div>
    <div style={{fontSize: 46, fontWeight: 700, marginTop: 16}}>{name}</div>
    <div style={{fontSize: 30, color: SUB, marginTop: 2, whiteSpace: 'nowrap'}}>{note}</div>
  </div>
);

// 水色の線。両端に黒い点(参考のスライドの、点と線だけの図のように)
const Link: React.FC<{d: string; a: [number, number]; b: [number, number]; dashed?: boolean}> = ({d, a, b, dashed}) => (
  <>
    <path d={d} fill="none" stroke={CYAN} strokeWidth={5} strokeLinecap="round" strokeDasharray={dashed ? '2 14' : undefined} />
    <circle cx={a[0]} cy={a[1]} r={9} fill={INK} />
    <circle cx={b[0]} cy={b[1]} r={9} fill={INK} />
  </>
);

const LinkLabel: React.FC<{x: number; y: number; text: string}> = ({x, y, text}) => (
  <div style={{position: 'absolute', left: x, top: y, transform: 'translate(-50%, -50%)', fontFamily: FONT, fontSize: 30, fontWeight: 700, color: INK, background: BG, padding: '2px 14px', whiteSpace: 'nowrap'}}>
    {text}
  </div>
);

// phoneLink: スマホとパソコンをつなぐ線のラベル(5G前提の資料では「5G」だけにする)
export const SystemMinimal: React.FC<{phoneLink?: string}> = ({phoneLink = 'Wi-Fi / 5G'}) => {
  // 上の段: マット → ESP32 → パソコン → ディスプレイ / 下の段: スマホ ⇄ パソコン
  const row = 470, low = 850;
  const X = {mat: 250, esp: 690, pc: 1130, disp: 1570};
  return (
    <AbsoluteFill style={{background: BG}}>
      {/* 右上の水色のリボン(参考のスライドの表紙のような流れる線) */}
      <svg width={MINIMAL_W} height={MINIMAL_H} style={{position: 'absolute', left: 0, top: 0}}>
        <defs>
          <linearGradient id="ribbon" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" stopColor={CYAN} stopOpacity={0} />
            <stop offset="0.5" stopColor={CYAN} stopOpacity={0.55} />
            <stop offset="1" stopColor="#3be0c8" stopOpacity={0.9} />
          </linearGradient>
        </defs>
        <path d="M 900 300 C 1300 260, 1500 120, 1700 20 C 1780 -20, 1880 -10, 1920 30" fill="none" stroke="url(#ribbon)" strokeWidth={34} strokeLinecap="round" />
      </svg>

      {/* ロゴと見出し */}
      <Img src={staticFile('diagram/logo.png')} style={{position: 'absolute', left: 70, top: 40, height: 92}} />
      <div style={{position: 'absolute', left: 66, top: 150, fontFamily: FONT, fontSize: 84, fontWeight: 800, color: INK}}>システム構成</div>
      <div style={{position: 'absolute', left: 72, top: 262, fontFamily: FONT, fontSize: 32, color: SUB}}>カメレオンサバイバー | 高専祭 専門展</div>

      {/* つながり */}
      <svg width={MINIMAL_W} height={MINIMAL_H} style={{position: 'absolute', left: 0, top: 0}}>
        <Link d={`M ${X.mat + 120} ${row - 30} L ${X.esp - 120} ${row - 30}`} a={[X.mat + 120, row - 30]} b={[X.esp - 120, row - 30]} />
        <Link d={`M ${X.esp + 120} ${row - 30} L ${X.pc - 120} ${row - 30}`} a={[X.esp + 120, row - 30]} b={[X.pc - 120, row - 30]} />
        <Link d={`M ${X.pc + 120} ${row - 30} L ${X.disp - 120} ${row - 30}`} a={[X.pc + 120, row - 30]} b={[X.disp - 120, row - 30]} />
        <Link d={`M ${X.esp + 120} ${low - 30} C ${X.esp + 260} ${low - 30}, ${X.pc - 260} ${row + 70}, ${X.pc - 120} ${row + 70}`} a={[X.esp + 120, low - 30]} b={[X.pc - 120, row + 70]} dashed />
      </svg>
      <LinkLabel x={(X.mat + X.esp) / 2} y={row - 72} text="踏んだ信号" />
      <LinkLabel x={(X.esp + X.pc) / 2} y={row - 72} text="USB" />
      <LinkLabel x={(X.pc + X.disp) / 2} y={row - 72} text="映像" />
      <LinkLabel x={(X.esp + X.pc) / 2 + 40} y={low - 95} text={phoneLink} />

      {/* 機器 */}
      <Node x={X.mat} y={row} icon={<MatLine />} name="圧力マット" note="主人公役が踏む" />
      <Node x={X.esp} y={row} icon={<ChipLine />} name="ESP32" note="踏んだ所を判定" />
      <Node x={X.pc} y={row} icon={<LaptopLine />} name="パソコン" note="Unityでゲームを動かす" />
      <Node x={X.disp} y={row} icon={<DisplayLine />} name="ディスプレイ" note="ゲーム画面" />
      <Node x={X.esp} y={low} icon={<PhoneLine />} name="スマホ" note="灯り役が照らす(最大7人)" />
    </AbsoluteFill>
  );
};
