// 発表スライド「システム構成」に貼る図(背景は透明)。スライドの背景・タイトル・ロゴはスライド側にあるので、図の部分だけ。
// variant: 'flow' 横一列の流れ / 'green' スライドの他のページと同じ緑の枠のカード
import React from 'react';
import {AbsoluteFill, Img} from 'remotion';
import {loadFont} from '@remotion/fonts';
import {staticFile} from 'remotion';
import {LIGHT_COLORS} from './fx';
import {ChipLine, DisplayLine, LaptopLine, MatLine, PhoneLine} from './SystemMinimal';

loadFont({family: 'Sen', url: staticFile('fonts/Sen.ttf'), weight: '400 800'});

export const SLIDE_SYS_W = 1920;
export const SLIDE_SYS_H = 900;

const FONT = "'BIZ UDPGothic', 'Yu Gothic UI', sans-serif";
const INK = '#212529';
const SUB = '#5f6b66';
const GREEN = '#8cc63f';
const CYAN = '#22c4d6';

type V = {variant: 'flow' | 'green'};

const Card: React.FC<{x: number; y: number; icon: React.ReactNode; name: string; note: string; who?: string; v: V['variant']}> = ({x, y, icon, name, note, who, v}) => (
  <div style={{position: 'absolute', left: x - 170, top: y - 150, width: 340, textAlign: 'center', fontFamily: FONT, color: INK}}>
    <div
      style={{
        width: 240, height: 240, margin: '0 auto', borderRadius: 36, background: '#fff', position: 'relative',
        border: v === 'green' ? `8px solid ${GREEN}` : 'none',
        boxShadow: v === 'green' ? 'none' : '0 12px 34px rgba(0,0,0,0.10)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', boxSizing: 'border-box',
      }}
    >
      {icon}
      {who && (
        <div
          style={{
            position: 'absolute', top: -26, left: '50%', transform: 'translateX(-50%)', whiteSpace: 'nowrap',
            background: v === 'green' ? GREEN : INK, color: '#fff', fontSize: 26, fontWeight: 700, padding: '6px 20px', borderRadius: 30,
          }}
        >
          {who}
        </div>
      )}
    </div>
    <div style={{fontSize: 50, fontWeight: 700, marginTop: 18}}>{name}</div>
    <div style={{fontSize: 30, color: SUB, marginTop: 4, whiteSpace: 'nowrap'}}>{note}</div>
  </div>
);

const Arrow: React.FC<{d: string; color: string; dashed?: boolean; head: [number, number, number]}> = ({d, color, dashed, head}) => {
  const [x, y, ang] = head;
  const s = 26;
  const p = (a: number) => `${x - s * Math.cos(ang + a)},${y - s * Math.sin(ang + a)}`;
  return (
    <>
      <path d={d} fill="none" stroke={color} strokeWidth={8} strokeLinecap="round" strokeDasharray={dashed ? '2 18' : undefined} />
      <polygon points={`${x},${y} ${p(0.5)} ${p(-0.5)}`} fill={color} />
    </>
  );
};

const Label: React.FC<{x: number; y: number; text: string; color?: string}> = ({x, y, text, color = INK}) => (
  <div
    style={{
      position: 'absolute', left: x, top: y, transform: 'translate(-50%, -50%)', fontFamily: FONT, fontSize: 32, fontWeight: 700,
      color, background: '#fff', padding: '4px 16px', borderRadius: 24, whiteSpace: 'nowrap', border: `3px solid ${color}`,
    }}
  >
    {text}
  </div>
);

export const SlideSystem: React.FC<V> = ({variant}) => {
  const row = 220, low = 680;
  const X = {mat: 230, esp: 690, pc: 1190, disp: 1680};
  const line = variant === 'green' ? GREEN : CYAN;
  const right = 0;
  return (
    <AbsoluteFill>
      <svg width={SLIDE_SYS_W} height={SLIDE_SYS_H} style={{position: 'absolute', left: 0, top: 0}}>
        <Arrow d={`M ${X.mat + 130} ${row - 30} L ${X.esp - 140} ${row - 30}`} color={line} head={[X.esp - 128, row - 30, right]} />
        <Arrow d={`M ${X.esp + 130} ${row - 30} L ${X.pc - 140} ${row - 30}`} color={line} head={[X.pc - 128, row - 30, right]} />
        <Arrow d={`M ${X.pc + 130} ${row - 30} L ${X.disp - 140} ${row - 30}`} color={line} head={[X.disp - 128, row - 30, right]} />
        {/* スマホ → パソコン(5G) */}
        <Arrow d={`M ${X.esp + 130} ${low - 30} L ${X.pc - 200} ${low - 30} L ${X.pc - 200} ${row + 40} L ${X.pc - 140} ${row + 40}`} color={LIGHT_COLORS[0]} dashed
          head={[X.pc - 128, row + 40, right]} />
      </svg>
      <Label x={(X.mat + X.esp) / 2} y={row - 90} text="踏んだ信号" color={variant === 'green' ? '#5d8f22' : INK} />
      <Label x={(X.esp + X.pc) / 2} y={row - 90} text="USB" color={variant === 'green' ? '#5d8f22' : INK} />
      <Label x={(X.pc + X.disp) / 2} y={row - 90} text="映像" color={variant === 'green' ? '#5d8f22' : INK} />
      <Label x={X.pc - 200} y={(row + low) / 2 + 40} text="5G(インターネット経由)" color="#d9483b" />

      <Card v={variant} x={X.mat} y={row} icon={<MatLine />} name="圧力マット" note="カメレオン役が踏む" who="カメレオン役" />
      <Card v={variant} x={X.esp} y={row} icon={<ChipLine />} name="ESP32" note="踏んだ向きを判定" />
      <Card v={variant} x={X.pc} y={row} icon={<LaptopLine />} name="PC(Unity)" note="ゲームの司令塔" />
      <Card v={variant} x={X.disp} y={row} icon={<DisplayLine />} name="ゲーム画面" note="みんなで見る" />
      <Card v={variant} x={X.esp} y={low} icon={<PhoneLine />} name="スマホ" note="光で照らしてサポート" who="灯り役" />
    </AbsoluteFill>
  );
};

// ---- 案C: 入力 → 処理 → 出力 の3列(見出しの帯つき)。スライドの他のページと同じ緑の枠を使う ----
export const SLIDE_IPO_W = 1920;
export const SLIDE_IPO_H = 820;

const COLS = [
  {head: '入力', color: '#3f8f4f', box: '#f1f8ea', x: 40},
  {head: '処理', color: '#2d5a3a', box: '#e6f3e2', x: 700},
  {head: '出力', color: '#d4622b', box: '#fff6e0', x: 1360},
];
const BOX_W = 520, BOX_H = 220;

const IpoBox: React.FC<{col: number; y: number; icon: React.ReactNode; name: string; note: string; who?: string}> = ({col, y, icon, name, note, who}) => {
  const c = COLS[col];
  return (
    <div
      style={{
        position: 'absolute', left: c.x, top: y, width: BOX_W, height: BOX_H, background: c.box, border: `6px solid ${c.color}`,
        borderRadius: 28, boxSizing: 'border-box', display: 'flex', alignItems: 'center', gap: 18, padding: '0 22px', fontFamily: FONT, color: INK,
      }}
    >
      <div style={{flexShrink: 0, width: 170, height: 170, borderRadius: 24, background: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
        <div style={{transform: 'scale(0.95)'}}>{icon}</div>
      </div>
      <div>
        {who && (
          <div style={{display: 'inline-block', background: c.color, color: '#fff', fontSize: 24, fontWeight: 700, padding: '3px 14px', borderRadius: 20, marginBottom: 6}}>
            {who}
          </div>
        )}
        <div style={{fontSize: 46, fontWeight: 700, lineHeight: 1.15}}>{name}</div>
        <div style={{fontSize: 28, color: SUB, marginTop: 6}}>{note}</div>
      </div>
    </div>
  );
};

export const SlideSystemIPO: React.FC = () => {
  const top = 100, bottom = 420;
  const mid = (y: number) => y + BOX_H / 2;
  const R = (col: number) => COLS[col].x + BOX_W, L = (col: number) => COLS[col].x;
  const ink = '#2b3a33';
  return (
    <AbsoluteFill>
      {COLS.map((c) => (
        <div
          key={c.head}
          style={{
            position: 'absolute', left: c.x, top: 0, width: BOX_W, height: 64, borderRadius: 32, background: c.color, color: '#fff',
            fontFamily: FONT, fontSize: 34, fontWeight: 700, letterSpacing: 8, display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}
        >
          {c.head}
        </div>
      ))}
      <svg width={SLIDE_IPO_W} height={SLIDE_IPO_H} style={{position: 'absolute', left: 0, top: 0}}>
        <Arrow d={`M ${R(0)} ${mid(top)} L ${L(1) - 14} ${mid(top)}`} color={ink} head={[L(1) - 2, mid(top), 0]} />
        <Arrow d={`M ${L(1) + 120} ${top + BOX_H} L ${L(1) + 120} ${bottom - 14}`} color={ink} head={[L(1) + 120, bottom - 2, Math.PI / 2]} />
        <Arrow d={`M ${R(0)} ${mid(bottom)} L ${L(1) - 14} ${mid(bottom)}`} color={LIGHT_COLORS[0]} dashed head={[L(1) - 2, mid(bottom), 0]} />
        <Arrow d={`M ${R(1)} ${bottom + 60} L ${R(1) + 50} ${bottom + 60} L ${R(1) + 50} ${mid(top)} L ${L(2) - 14} ${mid(top)}`} color={ink} head={[L(2) - 2, mid(top), 0]} />
        <Arrow d={`M ${R(1)} ${mid(bottom) + 40} L ${L(2) - 14} ${mid(bottom) + 40}`} color={ink} head={[L(2) - 2, mid(bottom) + 40, 0]} />
        <Arrow d={`M ${L(2) + BOX_W / 2} ${bottom + BOX_H} L ${L(2) + BOX_W / 2} 730 L ${L(0) + BOX_W / 2} 730 L ${L(0) + BOX_W / 2} ${bottom + BOX_H + 14}`}
          color="#e07a2b" dashed head={[L(0) + BOX_W / 2, bottom + BOX_H + 2, -Math.PI / 2]} />
      </svg>
      <Label x={(R(0) + L(1)) / 2} y={mid(top) - 46} text="信号" color={ink} />
      <Label x={L(1) + 210} y={(top + BOX_H + bottom) / 2} text="USB" color={ink} />
      <Label x={(R(0) + L(1)) / 2} y={mid(bottom) - 46} text="5G" color="#d9483b" />
      <Label x={R(1) + 50} y={(mid(top) + bottom + 60) / 2 + 20} text="映像" color={ink} />
      <Label x={(R(1) + L(2)) / 2 - 10} y={mid(bottom) + 92} text="結果" color={ink} />
      <div style={{position: 'absolute', left: 0, right: 0, top: 752, textAlign: 'center', fontFamily: FONT, fontSize: 32, fontWeight: 700, color: '#c45a1c'}}>
        画面を見て、カメレオン役は踏む・灯り役は照らす
      </div>

      <IpoBox col={0} y={top} icon={<MatLine />} name="圧力マット" note="踏んで動かす" who="カメレオン役" />
      <IpoBox col={0} y={bottom} icon={<PhoneLine />} name="スマホ" note="光で照らす" who="灯り役" />
      <IpoBox col={1} y={top} icon={<ChipLine />} name="ESP32" note="踏んだ向きを判定" />
      <IpoBox col={1} y={bottom} icon={<LaptopLine />} name="PC(Unity)" note="ゲームの司令塔" />
      <IpoBox col={2} y={top} icon={<DisplayLine />} name="ゲーム画面" note="みんなで見る" />
      <IpoBox col={2} y={bottom} icon={<PhoneLine />} name="スマホの画面" note="撃破数・振動" />
    </AbsoluteFill>
  );
};

// ---- 案D: 黒いシンプルなアイコンと細い水色の矢印だけの図(参考スライド「概要」のような最小限の形) ----
export const SLIDE_SIMPLE_W = 1920;
export const SLIDE_SIMPLE_H = 760;

const BLK = '#111';
const SKY = '#28b8d8';

export const MatSil: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    {/* 白いマットに黒いパッド */}
    <rect x={8} y={8} width={134} height={134} rx={18} fill="#fff" />
    {[[45, 20, 60, 20], [45, 110, 60, 20], [20, 45, 20, 60], [110, 45, 20, 60]].map(([x, y, w, h], i) => (
      <rect key={i} x={x} y={y} width={w} height={h} rx={5} fill={BLK} />
    ))}
  </svg>
);
export const ChipSil: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    <rect x={28} y={28} width={94} height={94} rx={10} fill={BLK} />
    <rect x={52} y={52} width={46} height={46} rx={4} fill="#fff" />
    {[45, 65, 85, 105].map((v) => (
      <React.Fragment key={v}>
        <rect x={v - 3} y={10} width={6} height={20} fill={BLK} /><rect x={v - 3} y={120} width={6} height={20} fill={BLK} />
        <rect x={10} y={v - 3} width={20} height={6} fill={BLK} /><rect x={120} y={v - 3} width={20} height={6} fill={BLK} />
      </React.Fragment>
    ))}
  </svg>
);
export const PhoneSil: React.FC = () => (
  <svg width={170} height={150} viewBox="0 0 170 150">
    {[0, 1, 2].map((i) => (
      <g key={i} transform={`translate(${14 + i * 50}, ${i === 1 ? 4 : 18})`}>
        <rect x={0} y={0} width={44} height={i === 1 ? 104 : 90} rx={9} fill={BLK} stroke="#fff" strokeWidth={3} />
        <rect x={6} y={10} width={32} height={i === 1 ? 70 : 56} rx={3} fill="#fff" />
      </g>
    ))}
  </svg>
);
export const CloudSil: React.FC = () => (
  <svg width={190} height={150} viewBox="0 0 190 150">
    <path d="M48 118 C18 118 10 84 34 74 C30 44 66 30 86 48 C96 22 140 22 148 54 C176 54 184 92 160 112 C156 116 150 118 144 118 Z" fill={BLK} />
    <circle cx={74} cy={88} r={7} fill="#fff" /><circle cx={98} cy={88} r={7} fill="#fff" /><circle cx={122} cy={88} r={7} fill="#fff" />
  </svg>
);
export const MonitorSil: React.FC = () => (
  <svg width={170} height={150} viewBox="0 0 170 150">
    <rect x={10} y={14} width={150} height={96} rx={8} fill={BLK} />
    <rect x={20} y={24} width={130} height={76} rx={3} fill="#fff" />
    <rect x={76} y={110} width={18} height={20} fill={BLK} />
    <rect x={50} y={128} width={70} height={10} rx={4} fill={BLK} />
  </svg>
);

// role: アイコンの上に出す役割(カメレオン役・灯り役)
const Item: React.FC<{x: number; y: number; icon: React.ReactNode; name: string; sub?: string; role?: string}> = ({x, y, icon, name, sub, role}) => (
  <div style={{position: 'absolute', left: x - 150, top: y - 85, width: 300, textAlign: 'center', fontFamily: FONT, color: BLK}}>
    {role && (
      <div style={{position: 'absolute', left: 0, right: 0, top: -44, fontSize: 26, fontWeight: 700, color: '#4f7f2e', whiteSpace: 'nowrap'}}>{role}</div>
    )}
    <div style={{height: 150, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>{icon}</div>
    <div style={{fontSize: 34, fontWeight: 700, marginTop: 6}}>{name}</div>
    {sub && <div style={{fontSize: 26, color: SUB, marginTop: 2}}>{sub}</div>}
  </div>
);

const ThinArrow: React.FC<{x1: number; y1: number; x2: number; y2: number}> = ({x1, y1, x2, y2}) => {
  const a = Math.atan2(y2 - y1, x2 - x1), s = 22;
  const p = (d: number) => `${x2 - s * Math.cos(a + d)},${y2 - s * Math.sin(a + d)}`;
  return (
    <>
      <line x1={x1} y1={y1} x2={x2 - 14 * Math.cos(a)} y2={y2 - 14 * Math.sin(a)} stroke={SKY} strokeWidth={6} />
      <polygon points={`${x2},${y2} ${p(0.45)} ${p(-0.45)}`} fill={SKY} />
    </>
  );
};

const Small: React.FC<{x: number; y: number; text: string}> = ({x, y, text}) => (
  <div style={{position: 'absolute', left: x, top: y, transform: 'translate(-50%, -50%)', fontFamily: FONT, fontSize: 30, fontWeight: 700, color: BLK, whiteSpace: 'nowrap'}}>
    {text}
  </div>
);

export const SlideSystemSimple: React.FC = () => {
  const r1 = 230, r2 = 560;
  const F = {x: 1080, y: 150, w: 470, h: 470};
  return (
    <AbsoluteFill>
      {/* 参加者の入力(圧力マット・スマホ)をまとめる薄い背景 */}
      <div style={{position: 'absolute', left: 30, top: 30, width: 380, height: 700, borderRadius: 32, background: '#e8f5e0'}} />
      <div style={{position: 'absolute', left: 30, top: 44, width: 380, textAlign: 'center', fontFamily: FONT, fontSize: 26, fontWeight: 700, color: '#4f7f2e'}}>参加者の入力</div>
      {/* PC(Unity)の枠。下にノートPCのアイコン */}
      <div style={{position: 'absolute', left: F.x, top: F.y, width: F.w, height: F.h, border: '5px solid #8a8f94', borderRadius: 30, boxSizing: 'border-box'}} />
      <div style={{position: 'absolute', left: F.x, top: F.y, width: F.w, height: F.h - 40, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', fontFamily: FONT, color: BLK}}>
        {/* Unity の公式ロゴ(黒) */}
        <Img src={staticFile('logos/unity.svg')} style={{width: 330}} />
        <div style={{fontSize: 30, color: SUB, marginTop: 4}}>ゲームの司令塔</div>
      </div>
      <svg width={180} height={110} viewBox="0 0 180 110" style={{position: 'absolute', left: F.x + F.w / 2 - 90, top: F.y + F.h - 60}}>
        <rect x={0} y={0} width={180} height={110} fill="#fff" />
        <rect x={30} y={8} width={120} height={76} rx={6} fill="none" stroke={BLK} strokeWidth={8} />
        <path d="M10 92 H170" stroke={BLK} strokeWidth={10} strokeLinecap="round" />
      </svg>
      <Small x={F.x + F.w / 2} y={F.y + F.h + 78} text="PC" />

      <svg width={SLIDE_SIMPLE_W} height={SLIDE_SIMPLE_H} style={{position: 'absolute', left: 0, top: 0}}>
        <ThinArrow x1={430} y1={r1} x2={570} y2={r1} />
        <ThinArrow x1={830} y1={r1} x2={F.x - 10} y2={r1} />
        {/* スマホ ⇄ PC は双方向(傾きのデータを送り、撃破数などが返ってくる)。1本の線の両端に矢じり */}
        <ThinArrow x1={430} y1={r2 - 16} x2={570} y2={r2 - 16} />
        <ThinArrow x1={570} y1={r2 + 16} x2={430} y2={r2 + 16} />
        <ThinArrow x1={850} y1={r2} x2={F.x - 10} y2={r2} />
        <ThinArrow x1={F.x - 10} y1={r2} x2={850} y2={r2} />
        <ThinArrow x1={F.x + F.w + 10} y1={F.y + F.h / 2} x2={1660} y2={F.y + F.h / 2} />
      </svg>
      <Small x={500} y={r1 - 40} text="踏んだ信号" />
      <Small x={955} y={r1 - 40} text="USB" />
      <Small x={500} y={r2 - 52} text="ジャイロデータ" />
      <Small x={500} y={r2 + 52} text="撃破データ" />
      <Small x={965} y={r2 - 40} text="データ送受信" />
      <Small x={1605} y={F.y + F.h / 2 - 40} text="映像" />

      <Item x={220} y={r1} icon={<MatSil />} name="圧力マット" role="カメレオン役" />
      <Item x={700} y={r1} icon={<ChipSil />} name="ESP32" />
      <Item x={220} y={r2} icon={<PhoneSil />} name="スマホ" sub="4G・5G回線" role="灯り役" />
      {/* Cloudflare のマーク(Simple Icons, CC0。ブランドカラーのオレンジ) */}
      <Item x={700} y={r2} icon={<Img src={staticFile('logos/cloudflare_orange.svg')} style={{width: 150}} />} name="Cloudflare" sub="インターネットの中継" />
      <Item x={1790} y={F.y + F.h / 2} icon={<MonitorSil />} name="ゲーム画面" />
    </AbsoluteFill>
  );
};

// ---- アイコン単体(背景透過)。PowerPoint で図を組み直すための素材 ----
export const LaptopSil: React.FC = () => (
  <svg width={180} height={110} viewBox="0 0 180 110">
    <rect x={30} y={8} width={120} height={76} rx={6} fill="none" stroke={BLK} strokeWidth={8} />
    <path d="M10 92 H170" stroke={BLK} strokeWidth={10} strokeLinecap="round" />
  </svg>
);
export const ICON_SIZE = 800;
export const IconOnly: React.FC<{name: string}> = ({name}) => {
  const icons: Record<string, React.ReactNode> = {
    mat: <MatSil />, esp32: <ChipSil />, phone: <PhoneSil />, monitor: <MonitorSil />, laptop: <LaptopSil />,
    cloudflare: <Img src={staticFile('logos/cloudflare_orange.svg')} style={{width: 150}} />,
    unity: <Img src={staticFile('logos/unity.svg')} style={{width: 180}} />,
    unityMark: <Img src={staticFile('logos/unity_mark.svg')} style={{height: 120}} />,
    server: <ServerSil />, browser: <BrowserSil />, ai3d: <Ai3dSil />, tree: <TreeSil />,
  };
  return (
    <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center'}}>
      <div style={{transform: 'scale(4)'}}>{icons[name]}</div>
    </AbsoluteFill>
  );
};

// ---- 「使った技術」のページ(構成図Dと同じ黒いアイコン・薄い色の背景でまとまりを示す) ----
export const SLIDE_TECH_W = 1920;
export const SLIDE_TECH_H = 560;

const ServerSil: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    {[14, 56, 98].map((y) => (
      <g key={y}>
        <rect x={22} y={y} width={106} height={38} rx={7} fill={BLK} />
        <circle cx={42} cy={y + 19} r={6} fill="#fff" />
        <rect x={62} y={y + 15} width={52} height={8} rx={4} fill="#fff" />
      </g>
    ))}
  </svg>
);
const BrowserSil: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    <rect x={44} y={8} width={62} height={134} rx={12} fill={BLK} />
    <rect x={52} y={24} width={46} height={98} rx={3} fill="#fff" />
    <circle cx={75} cy={132} r={5} fill="#fff" />
    <circle cx={75} cy={58} r={16} fill="none" stroke={BLK} strokeWidth={5} />
    <path d="M59 58 H91 M75 42 C66 50 66 66 75 74 M75 42 C84 50 84 66 75 74" stroke={BLK} strokeWidth={4} fill="none" />
  </svg>
);
const Ai3dSil: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    <path d="M70 30 L120 55 L120 110 L70 135 L20 110 L20 55 Z" fill={BLK} />
    <path d="M70 80 L120 55 M70 80 L20 55 M70 80 L70 135" stroke="#fff" strokeWidth={5} />
    <path d="M122 8 L128 22 L142 28 L128 34 L122 48 L116 34 L102 28 L116 22 Z" fill={BLK} />
  </svg>
);
const TreeSil: React.FC = () => (
  <svg width={150} height={150} viewBox="0 0 150 150">
    <path d="M75 10 L122 72 H98 L128 112 H22 L52 72 H28 Z" fill={BLK} />
    <rect x={66} y={112} width={18} height={30} fill={BLK} />
  </svg>
);

const TechItem: React.FC<{x: number; y: number; icon: React.ReactNode; name: string; note: string}> = ({x, y, icon, name, note}) => (
  <div style={{position: 'absolute', left: x, top: y, width: 400, height: 170, display: 'flex', alignItems: 'center', gap: 18, fontFamily: FONT, color: BLK}}>
    <div style={{width: 130, height: 130, flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
      <div style={{transform: 'scale(0.85)'}}>{icon}</div>
    </div>
    <div>
      <div style={{fontSize: 38, fontWeight: 700, lineHeight: 1.15, whiteSpace: 'nowrap'}}>{name}</div>
      <div style={{fontSize: 27, color: SUB, marginTop: 6, whiteSpace: 'nowrap'}}>{note}</div>
    </div>
  </div>
);

export const SlideTech: React.FC = () => {
  const groups: {label: string; color: string; text: string; x: number; w: number; y: number; items: [React.ReactNode, string, string][]}[] = [
    {label: 'ソフト', color: '#e3f1fb', text: '#2a6f97', x: 30, w: 1860, y: 0, items: [
      [<Img src={staticFile('logos/unity_mark.svg')} style={{height: 120}} />, 'Unity', 'ゲーム本体'],
      [<ServerSil />, 'Webサーバー', 'PCの中で動く'],
      [<BrowserSil />, 'ブラウザ', 'アプリ不要で参加'],
      [<Img src={staticFile('logos/cloudflare_orange.svg')} style={{width: 140}} />, 'Cloudflare', '4G・5Gからつなぐ'],
    ]},
    {label: 'ハード', color: '#e8f5e0', text: '#4f7f2e', x: 30, w: 915, y: 280, items: [
      [<ChipSil />, 'ESP32', 'マットを読むマイコン'],
      [<MatSil />, '圧力マット', '足で踏んで操作'],
    ]},
    {label: '素材', color: '#fdf0e3', text: '#b35a1f', x: 975, w: 915, y: 280, items: [
      [<Ai3dSil />, 'Meshy AI', '敵とカメレオンの3D'],
      [<TreeSil />, 'Kenney', '木・岩の素材'],
    ]},
  ];
  return (
    <AbsoluteFill>
      {groups.map((g) => (
        <React.Fragment key={g.label}>
          <div style={{position: 'absolute', left: g.x, top: g.y + 10, width: g.w, height: 240, borderRadius: 32, background: g.color}} />
          <div style={{position: 'absolute', left: g.x, top: g.y + 10, width: 150, height: 240, display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontFamily: FONT, fontSize: 36, fontWeight: 700, color: g.text, writingMode: 'vertical-rl', letterSpacing: 8}}>{g.label}</div>
          {g.items.map(([icon, name, note], i) => (
            <TechItem key={name} x={g.x + 140 + i * 400} y={g.y + 45} icon={icon} name={name} note={note} />
          ))}
        </React.Fragment>
      ))}
    </AbsoluteFill>
  );
};
