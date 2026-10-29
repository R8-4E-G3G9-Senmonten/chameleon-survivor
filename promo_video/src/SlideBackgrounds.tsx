// 発表スライドの背景(16:9)。地は単色(白 or 夜の暗い緑)で、灯り役の光の輪やホタルをワンポイントにする。
// 表紙(title)はロゴを大きく中央に、本文(content)はロゴを右上に小さく置き、左上を見出し用に空けておく。
import React from 'react';
import {AbsoluteFill, Img, staticFile} from 'remotion';
import {LIGHT_COLORS} from './fx';

export const SLIDE_W = 1920;
export const SLIDE_H = 1080;

type Props = {theme: 'light' | 'dark'; kind: 'title' | 'content'; logo?: boolean};

// 灯り役の光の輪(白の地に淡く)
const LightRing: React.FC<{x: number; y: number; r: number; color: string; opacity?: number}> = ({x, y, r, color, opacity = 0.16}) => (
  <div
    style={{
      position: 'absolute', left: x - r, top: y - r, width: r * 2, height: r * 2, borderRadius: '50%',
      background: `radial-gradient(circle, ${color} 0%, ${color} 45%, transparent 72%)`, opacity,
    }}
  />
);

// ホタル(暗い地に光る点)
const Firefly: React.FC<{x: number; y: number; s: number; o?: number}> = ({x, y, s, o = 1}) => (
  <div
    style={{
      position: 'absolute', left: x - s / 2, top: y - s / 2, width: s, height: s, borderRadius: '50%', background: '#e9ff9a', opacity: o,
      boxShadow: `0 0 ${s * 2}px ${s * 0.8}px rgba(215,255,107,0.55)`,
    }}
  />
);

// 夜の森のシルエット(下端)
const Forest: React.FC<{height: number; color: string}> = ({height, color}) => {
  const trees: string[] = [];
  for (let i = 0; i < 26; i++) {
    const x = i * 78 - 20 + ((i * 37) % 23);
    const h = height * (0.55 + ((i * 53) % 40) / 100);
    const w = h * 0.42;
    trees.push(`M ${x} ${SLIDE_H} L ${x + w / 2} ${SLIDE_H - h} L ${x + w} ${SLIDE_H} Z`);
  }
  return (
    <svg width={SLIDE_W} height={SLIDE_H} style={{position: 'absolute', left: 0, top: 0}}>
      <path d={trees.join(' ')} fill={color} />
      <rect x={0} y={SLIDE_H - height * 0.18} width={SLIDE_W} height={height * 0.18} fill={color} />
    </svg>
  );
};

// 灯り役の7色を並べた細い帯
const ColorBar: React.FC<{y: number; h: number}> = ({y, h}) => (
  <div style={{position: 'absolute', left: 0, right: 0, top: y, height: h, display: 'flex'}}>
    {LIGHT_COLORS.map((c) => (
      <div key={c} style={{flex: 1, background: c}} />
    ))}
  </div>
);

// logo: false にするとロゴを入れない(スライド側にすでにロゴを置いている時用)
export const SlideBackground: React.FC<Props> = ({theme, kind, logo = true}) => {
  const dark = theme === 'dark';
  const bg = dark ? '#0d1b16' : '#ffffff';
  const fireflies: [number, number, number][] = [
    [1560, 690, 10], [1690, 610, 7], [1780, 760, 12], [1460, 800, 6], [1840, 560, 5], [1630, 860, 8], [300, 760, 7], [190, 650, 5],
  ];
  return (
    <AbsoluteFill style={{background: bg, overflow: 'hidden'}}>
      {/* ---- 白: 隅に灯り役の光の輪 ---- */}
      {!dark && kind === 'title' && (
        <>
          <LightRing x={140} y={130} r={260} color={LIGHT_COLORS[1]} />
          <LightRing x={330} y={40} r={170} color={LIGHT_COLORS[2]} />
          <LightRing x={1790} y={960} r={300} color={LIGHT_COLORS[0]} />
          <LightRing x={1560} y={1060} r={200} color={LIGHT_COLORS[3]} />
          <LightRing x={1880} y={760} r={150} color={LIGHT_COLORS[4]} />
          <ColorBar y={SLIDE_H - 14} h={14} />
        </>
      )}
      {!dark && kind === 'content' && (
        <>
          <LightRing x={1880} y={40} r={220} color={LIGHT_COLORS[1]} opacity={0.13} />
          <LightRing x={1740} y={-30} r={150} color={LIGHT_COLORS[2]} opacity={0.13} />
          <ColorBar y={SLIDE_H - 10} h={10} />
        </>
      )}

      {/* ---- 暗: 夜の森の影とホタル ---- */}
      {dark && (
        <>
          <div style={{position: 'absolute', inset: 0, background: 'radial-gradient(circle at 50% 38%, rgba(60,140,100,0.22), transparent 60%)'}} />
          {kind === 'title' && (
            <>
              <div style={{position: 'absolute', left: 1580, top: 110, width: 110, height: 110, borderRadius: '50%', background: '#f3f0d8', boxShadow: '0 0 80px 20px rgba(243,240,216,0.25)'}} />
              <Forest height={300} color="#081410" />
              {fireflies.map(([x, y, s], i) => (
                <Firefly key={i} x={x} y={y - 120} s={s} o={0.75 + (i % 3) * 0.1} />
              ))}
            </>
          )}
          {kind === 'content' && (
            <>
              <Forest height={130} color="#0a1813" />
              {fireflies.slice(0, 5).map(([x, y, s], i) => (
                <Firefly key={i} x={x} y={y + 120} s={s * 0.8} o={0.7} />
              ))}
            </>
          )}
        </>
      )}

      {/* ---- ロゴ ---- */}
      {!logo ? null : kind === 'title' ? (
        <Img src={staticFile('diagram/logo.png')} style={{position: 'absolute', left: SLIDE_W / 2 - 700, top: dark ? 330 : 360, width: 1400}} />
      ) : (
        <Img src={staticFile('diagram/logo.png')} style={{position: 'absolute', right: 40, top: 30, height: 84}} />
      )}
    </AbsoluteFill>
  );
};
