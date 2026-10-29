// タイトルロゴ: デフォルメしたカメレオン(ランタンを持つ)と「カメレオンサバイバー」の文字。
// 背景は透明。`npm run logo` で out/TitleLogo.png に書き出し、まわりの透明な余白を切り取ってから
// ゲームの Assets/Resources/UI/TitleLogo.png に置く(タイトル画面で NightHud が表示する)。
import React from 'react';
import {AbsoluteFill} from 'remotion';
import {HEAVY, LIGHT_COLORS} from './fx';

const OUTLINE = '#0d1f17';

// ちびキャラのカメレオン(viewBox 0 0 520 520)
const Chameleon: React.FC<{size?: number}> = ({size = 520}) => (
  <svg viewBox="0 0 520 520" width={size} height={size} style={{overflow: 'visible'}}>
    <defs>
      <radialGradient id="skin" cx="40%" cy="35%" r="75%">
        <stop offset="0%" stopColor="#b8ff7a" />
        <stop offset="55%" stopColor="#4fd17a" />
        <stop offset="100%" stopColor="#1f9a6a" />
      </radialGradient>
      <radialGradient id="belly" cx="50%" cy="30%" r="70%">
        <stop offset="0%" stopColor="#fff6b0" />
        <stop offset="100%" stopColor="#ffd36b" />
      </radialGradient>
      <radialGradient id="glow" cx="50%" cy="50%" r="50%">
        <stop offset="0%" stopColor="#fff3a0" stopOpacity="0.95" />
        <stop offset="45%" stopColor="#ffd34d" stopOpacity="0.55" />
        <stop offset="100%" stopColor="#ffb020" stopOpacity="0" />
      </radialGradient>
    </defs>

    {/* ランタンの光(後ろ) */}
    <circle cx="455" cy="395" r="125" fill="url(#glow)" />

    {/* しっぽ(くるっと巻いた渦) */}
    <path
      d="M150 360 C 70 370, 40 300, 80 260 C 115 225, 170 245, 165 290 C 160 320, 125 325, 115 305 C 108 290, 120 278, 132 284"
      fill="none"
      stroke={OUTLINE}
      strokeWidth="46"
      strokeLinecap="round"
    />
    <path
      d="M150 360 C 70 370, 40 300, 80 260 C 115 225, 170 245, 165 290 C 160 320, 125 325, 115 305 C 108 290, 120 278, 132 284"
      fill="none"
      stroke="url(#skin)"
      strokeWidth="30"
      strokeLinecap="round"
    />

    {/* 足(後ろ) */}
    <ellipse cx="190" cy="430" rx="34" ry="26" fill="#2fb070" stroke={OUTLINE} strokeWidth="9" />
    {/* からだ */}
    <ellipse cx="240" cy="360" rx="120" ry="95" fill="url(#skin)" stroke={OUTLINE} strokeWidth="10" />
    <ellipse cx="262" cy="385" rx="70" ry="52" fill="url(#belly)" />
    {/* 背中の模様(灯り役の7色) */}
    {LIGHT_COLORS.slice(0, 5).map((c, i) => (
      <circle key={i} cx={160 + i * 34} cy={300 - Math.sin((i / 4) * Math.PI) * 26} r={11} fill={c} stroke={OUTLINE} strokeWidth="5" />
    ))}
    {/* 足(前) */}
    <ellipse cx="300" cy="440" rx="36" ry="26" fill="#3cc47a" stroke={OUTLINE} strokeWidth="9" />

    {/* 頭 */}
    <path
      d="M250 250 C 240 160, 300 95, 375 105 C 455 115, 485 190, 465 255 C 448 312, 380 330, 320 315 C 280 305, 255 285, 250 250 Z"
      fill="url(#skin)"
      stroke={OUTLINE}
      strokeWidth="10"
    />
    {/* かぶと(頭のとさか) */}
    <path d="M285 150 C 300 70, 380 55, 420 100 C 380 95, 330 110, 300 165 Z" fill="#2fb070" stroke={OUTLINE} strokeWidth="9" strokeLinejoin="round" />
    {/* ほっぺ */}
    <ellipse cx="420" cy="265" rx="24" ry="14" fill="#ff8fb0" opacity="0.8" />
    {/* 口(にっこり) */}
    <path d="M378 282 C 405 300, 440 296, 458 272" fill="none" stroke={OUTLINE} strokeWidth="9" strokeLinecap="round" />
    {/* 大きな目(カメレオンらしい丸い目玉) */}
    <circle cx="355" cy="200" r="62" fill="url(#skin)" stroke={OUTLINE} strokeWidth="10" />
    <circle cx="362" cy="198" r="42" fill="#fff" stroke={OUTLINE} strokeWidth="7" />
    <circle cx="372" cy="200" r="24" fill="#16241d" />
    <circle cx="380" cy="190" r="9" fill="#fff" />
    <circle cx="366" cy="212" r="4" fill="#fff" />

    {/* ランタンを持つ手 */}
    <path d="M335 375 C 370 372, 400 360, 425 340" fill="none" stroke={OUTLINE} strokeWidth="30" strokeLinecap="round" />
    <path d="M335 375 C 370 372, 400 360, 425 340" fill="none" stroke="#3cc47a" strokeWidth="16" strokeLinecap="round" />
    {/* ランタン(手から下げる) */}
    <path d="M440 342 C 440 322, 470 322, 470 342" fill="none" stroke={OUTLINE} strokeWidth="8" />
    <rect x="430" y="344" width="50" height="14" rx="5" fill="#ffb84d" stroke={OUTLINE} strokeWidth="7" />
    <rect x="435" y="358" width="40" height="52" rx="12" fill="#fff4a8" stroke={OUTLINE} strokeWidth="7" />
    <circle cx="455" cy="384" r="11" fill="#ffd34d" />
    <rect x="430" y="408" width="50" height="12" rx="5" fill="#ffb84d" stroke={OUTLINE} strokeWidth="7" />
  </svg>
);

// 文字: 紹介動画のエンドカードと同じく、1文字ずつ灯り役の7色にして左右に少し傾ける
const Word: React.FC<{text: string; size: number}> = ({text, size}) => (
  <div style={{display: 'flex'}}>
    {[...text].map((ch, i) => (
      <span
        key={i}
        style={{
          fontFamily: HEAVY,
          fontSize: size,
          lineHeight: 1.15,
          marginRight: -size * 0.07, // 文字を少し詰めて、ロゴを横に長くしすぎない
          display: 'inline-block',
          color: LIGHT_COLORS[i % LIGHT_COLORS.length],
          transform: `rotate(${i % 2 ? 4 : -4}deg)`,
          WebkitTextStroke: `${size * 0.12}px #111`,
          paintOrder: 'stroke fill',
          textShadow: `0 ${size * 0.08}px 0 #111`,
        }}
      >
        {ch}
      </span>
    ))}
  </div>
);

export const LOGO_W = 2300;
export const LOGO_H = 460;

// 左にカメレオン、右に1行のタイトル(文字は紹介動画のロゴと同じ作り)
export const TitleLogo: React.FC = () => (
  <AbsoluteFill style={{flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 0}}>
    <div style={{width: 420, height: 420, flexShrink: 0, marginRight: -10}}>
      <Chameleon size={420} />
    </div>
    <Word text="カメレオンサバイバー" size={160} />
  </AbsoluteFill>
);
