// 概要図(発表スライド用): 下から上へ「マット → ESP32 → シリアル通信 → パソコン(Unity)」、横からスマホ。
// それぞれに「名前:説明」を添える。`npx remotion still Overview out/overview.png` で書き出す。
import React from 'react';
import {AbsoluteFill, Img, staticFile} from 'remotion';
import {HEAVY, LIGHT_COLORS} from './fx';
import {ChipIcon, MatIcon, PhoneIcon} from './SystemDiagram';

export const OVERVIEW_W = 1920;
export const OVERVIEW_H = 1080;

const BODY = "'Yu Gothic UI', 'Meiryo', sans-serif";
const BLUE = '#1f6fd1';
const RED = '#e0302b';

// 「名前:説明」のラベル(名前は太字、説明は改行して続ける)
const Term: React.FC<{x: number; y: number; name: string; desc: string; nameColor?: string; size?: number}> = ({x, y, name, desc, nameColor = '#111', size = 34}) => (
  <div style={{position: 'absolute', left: x, top: y, fontFamily: BODY, fontWeight: 800, fontSize: size, lineHeight: 1.35, color: '#111', whiteSpace: 'pre'}}>
    <span style={{color: nameColor}}>{name}</span>
    {'\n'}
    {desc}
  </div>
);

const SERIAL_LINES = ['0,0,0,0', '1,0,0,0', '1,0,0,1', '0,0,0,1', '0,1,1,0', '0,0,0,0'];

export const Overview: React.FC = () => (
  <AbsoluteFill style={{background: '#fff'}}>
    <div style={{position: 'absolute', left: 60, top: 26, fontFamily: HEAVY, fontSize: 96, color: '#111'}}>概要図</div>

    {/* パソコン(Unity)。画面に実際のゲーム画面 */}
    <div style={{position: 'absolute', left: 1230, top: 50, width: 600}}>
      <div style={{width: 600, height: 350, background: '#1c2126', borderRadius: '16px 16px 0 0', padding: 16, boxSizing: 'border-box'}}>
        <Img src={staticFile('diagram/game.png')} style={{width: '100%', height: '100%', objectFit: 'cover'}} />
      </div>
      <div style={{width: 680, height: 26, marginLeft: -40, background: '#b9bfc6', borderRadius: '0 0 18px 18px', border: '3px solid #8a9198'}} />
    </div>
    <Term x={560} y={120} name="Unity" desc={'：ゲームエンジン。\nカメレオンサバイバーを動かす'} />

    {/* シリアルモニタの表示例 */}
    <div style={{position: 'absolute', left: 820, top: 430, width: 380, background: '#1b1e22', borderRadius: 8, padding: '12px 18px', boxSizing: 'border-box', fontFamily: 'Consolas, monospace', fontSize: 22, lineHeight: 1.3, color: '#d9dde2'}}>
      <div style={{color: '#8fd3ff', fontSize: 18, marginBottom: 4}}>前,後,左,右</div>
      {SERIAL_LINES.map((l, i) => (
        <div key={i}>{l}</div>
      ))}
    </div>
    <Term x={80} y={440} name="シリアル通信" nameColor={BLUE} desc={'：ESP32が判定した\n踏んだ向きをPCに送信'} />

    {/* ESP32 */}
    <div style={{position: 'absolute', left: 860, top: 700, transform: 'scale(1.6)', transformOrigin: 'top left'}}>
      <ChipIcon />
    </div>
    <Term x={1100} y={712} name="ESP32" desc={'：Wi-Fi付きのマイコン。\nマットの状態を読む'} />
    <Term x={80} y={630} name="Arduino IDE" desc={'：ESP32に\nプログラムを書き込める'} />

    {/* 圧力マット */}
    <div style={{position: 'absolute', left: 420, top: 830, transform: 'scale(0.8)', transformOrigin: 'top left'}}>
      <MatIcon />
    </div>
    <Term x={80} y={830} name="圧力マット" desc={'：踏むとONになる\nスイッチ×4(前後左右)'} size={30} />

    {/* スマホ */}
    <div style={{position: 'absolute', left: 1600, top: 520, display: 'flex', gap: 12, alignItems: 'flex-end'}}>
      {LIGHT_COLORS.slice(0, 3).map((c, i) => (
        <PhoneIcon key={i} color={c} size={i === 0 ? 1.2 : 1} />
      ))}
    </div>
    <Term x={1520} y={712} name="スマホ(灯り役)" desc={'：QRを読んで参加。\n照らす位置をPCへ'} size={30} />

    <svg width={OVERVIEW_W} height={OVERVIEW_H} style={{position: 'absolute', left: 0, top: 0}}>
      <defs>
        <marker id="red" viewBox="0 0 10 10" refX={9} refY={5} markerWidth={7} markerHeight={7} orient="auto-start-reverse">
          <path d="M0 0 L10 5 L0 10 Z" fill={RED} />
        </marker>
        <marker id="ink" viewBox="0 0 10 10" refX={9} refY={5} markerWidth={7} markerHeight={7} orient="auto-start-reverse">
          <path d="M0 0 L10 5 L0 10 Z" fill="#111" />
        </marker>
      </defs>
      {/* マットの4枚 → ESP32(赤い線が集まる) */}
      {[
        [496, 857], [496, 955], [447, 906], [545, 906],
      ].map(([x, y], i) => (
        <line key={i} x1={x} y1={y} x2={905} y2={812} stroke={RED} strokeWidth={4} markerEnd="url(#red)" />
      ))}
      {/* ESP32 → シリアル通信 → パソコン */}
      <line x1={964} y1={712} x2={964} y2={660} stroke="#111" strokeWidth={5} markerEnd="url(#ink)" />
      <line x1={1205} y1={440} x2={1290} y2={412} stroke="#111" strokeWidth={5} markerEnd="url(#ink)" />
      {/* スマホ → パソコン(無線) */}
      <path d="M 1690 508 C 1690 470, 1660 450, 1630 428" fill="none" stroke={BLUE} strokeWidth={5} strokeDasharray="12 10" markerEnd="url(#ink)" />
    </svg>
    <div style={{position: 'absolute', left: 1705, top: 440, fontFamily: BODY, fontWeight: 800, fontSize: 24, color: BLUE}}>Wi-Fi / 5G</div>

    <div style={{position: 'absolute', left: 60, bottom: 26, fontFamily: BODY, fontWeight: 800, fontSize: 32, color: '#111'}}>
      <span style={{color: BLUE}}>シリアル通信</span>：USBケーブル1本で、マットの状態をずっと送り続ける通信　シンプル
    </div>
  </AbsoluteFill>
);
