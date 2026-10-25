// スマホ(灯り役)と圧力マット(主人公)の絵
import React from 'react';
import {interpolate, spring, useCurrentFrame, useVideoConfig} from 'remotion';
import {HEAVY, LIGHT_COLORS, POP} from './fx';

// 灯り役のスマホ画面: 上半分にミニマップ、下半分に「ライトON」ボタン。pressAt で指が押す
export const PhoneMock: React.FC<{x: number; y: number; at?: number; pressAt: number; color?: string; scale?: number}> = ({
  x,
  y,
  at = 0,
  pressAt,
  color = LIGHT_COLORS[0],
  scale = 1,
}) => {
  const f = useCurrentFrame();
  const {fps} = useVideoConfig();
  if (f < at) return null;
  const s = spring({frame: f - at, fps, config: {damping: 11, stiffness: 160}});
  const pressed = f >= pressAt;
  const glow = pressed ? 0.6 + 0.4 * Math.sin((f - pressAt) / 2) : 0;
  const kills = pressed ? Math.min(9, Math.floor((f - pressAt) / 9)) : 0;
  const dotX = 50 + Math.sin(f / 9) * 22;
  const dotY = 50 + Math.cos(f / 13) * 18;
  return (
    <div
      style={{
        position: 'absolute',
        left: 960 + x - 190,
        top: 540 + y - 380,
        width: 380,
        height: 760,
        transform: `translateY(${(1 - s) * 900}px) rotate(${-8 + Math.sin(f / 10) * 3}deg) scale(${scale})`,
        background: '#111',
        borderRadius: 56,
        border: '10px solid #2b2b2b',
        boxShadow: `0 0 0 6px #111, 0 30px 60px rgba(0,0,0,0.6), 0 0 ${pressed ? 90 : 0}px ${color}`,
      }}
    >
      <div
        style={{
          position: 'absolute',
          inset: 14,
          borderRadius: 40,
          background: '#07140f',
          border: `8px solid ${pressed ? color : '#335'}`,
          overflow: 'hidden',
          fontFamily: POP,
          color: '#e6f5ec',
        }}
      >
        <div style={{position: 'absolute', top: 18, left: 22, fontSize: 24, color: '#a3c4b5'}}>灯り役</div>
        <div style={{position: 'absolute', top: 10, right: 22, fontSize: 52, fontFamily: HEAVY, color}}>{kills}</div>
        <div style={{position: 'absolute', top: 66, right: 22, fontSize: 20, color: '#a3c4b5'}}>体撃破</div>
        {/* ミニマップ */}
        <div
          style={{
            position: 'absolute',
            top: 100,
            left: 20,
            right: 20,
            height: 250,
            borderRadius: 20,
            background: 'radial-gradient(circle at 50% 50%, #1f3a2c, #0b1712)',
            border: '2px solid #2a4a3a',
          }}
        >
          <div style={{position: 'absolute', left: '48%', top: '48%', width: 22, height: 22, borderRadius: 11, background: '#9dffb0'}} />
          {pressed && (
            <div
              style={{
                position: 'absolute',
                left: `${dotX}%`,
                top: `${dotY}%`,
                width: 70,
                height: 70,
                marginLeft: -35,
                marginTop: -35,
                borderRadius: 35,
                background: color,
                opacity: 0.35 + glow * 0.4,
                boxShadow: `0 0 40px ${color}`,
              }}
            />
          )}
        </div>
        {/* ライトONボタン */}
        <div
          style={{
            position: 'absolute',
            left: 40,
            right: 40,
            bottom: 50,
            height: 280,
            borderRadius: 140,
            background: pressed ? color : '#1d2b25',
            border: `6px solid ${color}`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontFamily: HEAVY,
            fontSize: 54,
            color: pressed ? '#111' : color,
            transform: `scale(${pressed ? 0.95 : 1})`,
            boxShadow: pressed ? `0 0 ${60 + glow * 60}px ${color}` : 'none',
          }}
        >
          {pressed ? '点灯中!' : 'ライトON'}
        </div>
      </div>
      {/* 押す指 */}
      <div
        style={{
          position: 'absolute',
          left: 170,
          top: interpolate(f, [pressAt - 8, pressAt], [900, 560], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}),
          fontSize: 150,
          transform: 'rotate(-10deg)',
        }}
      >
        👆
      </div>
    </div>
  );
};

// 圧力マット: 4つのパッドが足踏みに合わせて光る
export const MatMock: React.FC<{x: number; y: number; at?: number}> = ({x, y, at = 0}) => {
  const f = useCurrentFrame();
  const {fps} = useVideoConfig();
  if (f < at) return null;
  const s = spring({frame: f - at, fps, config: {damping: 12, stiffness: 160}});
  const step = Math.floor((f - at) / 6) % 4;
  const pads = [
    {l: 150, t: 20, label: '▲'},
    {l: 20, t: 150, label: '◀'},
    {l: 280, t: 150, label: '▶'},
    {l: 150, t: 280, label: '▼'},
  ];
  return (
    <div
      style={{
        position: 'absolute',
        left: 960 + x - 210,
        top: 540 + y - 210,
        width: 420,
        height: 420,
        transform: `perspective(900px) rotateX(35deg) rotate(${(1 - s) * -90}deg) scale(${s})`,
        background: '#1c1c28',
        borderRadius: 30,
        border: '10px solid #111',
        boxShadow: '0 30px 0 #0a0a10, 0 40px 60px rgba(0,0,0,0.6)',
      }}
    >
      {pads.map((p, i) => {
        const on = i === step;
        return (
          <div
            key={i}
            style={{
              position: 'absolute',
              left: p.l,
              top: p.t,
              width: 110,
              height: 110,
              borderRadius: 22,
              background: on ? LIGHT_COLORS[4] : '#33334a',
              boxShadow: on ? `0 0 50px ${LIGHT_COLORS[4]}` : 'none',
              border: '6px solid #111',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontFamily: 'Arial, sans-serif',
              fontSize: 48,
              color: on ? '#111' : '#8888aa',
            }}
          >
            {p.label}
          </div>
        );
      })}
    </div>
  );
};

// 「最大7人」: 色の丸が拍ごとに1つずつ増える
export const PlayerDots: React.FC<{at: number; every: number; y?: number}> = ({at, every, y = 0}) => {
  const f = useCurrentFrame();
  const {fps} = useVideoConfig();
  const n = Math.max(0, Math.min(7, Math.floor((f - at) / every) + 1));
  if (f < at) return null;
  return (
    <div style={{position: 'absolute', left: 0, right: 0, top: 540 + y, display: 'flex', justifyContent: 'center', gap: 26}}>
      {LIGHT_COLORS.map((c, i) => {
        const s = i < n ? spring({frame: f - at - i * every, fps, config: {damping: 8, stiffness: 300}}) : 0;
        return (
          <div
            key={i}
            style={{
              width: 96,
              height: 96,
              borderRadius: 48,
              background: c,
              border: '7px solid #111',
              transform: `scale(${s}) translateY(${Math.sin((f + i * 6) / 4) * 8}px)`,
              boxShadow: `0 0 40px ${c}`,
            }}
          />
        );
      })}
    </div>
  );
};
