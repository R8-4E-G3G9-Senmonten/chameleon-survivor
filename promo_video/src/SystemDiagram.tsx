// システム構成図(入力 → 処理 → 出力)。`npx remotion still SystemDiagram out/system_diagram.png` で書き出す。
import React from 'react';
import {AbsoluteFill, Img, staticFile} from 'remotion';
import {LIGHT_COLORS, POP} from './fx';

export const DIAGRAM_W = 1920;
export const DIAGRAM_H = 1110;

const BODY = "'Yu Gothic UI', 'Meiryo', sans-serif";
const INK = '#23312a';
const RED = '#d9482b';

const COL = {
  input: {x: 30, w: 580, head: '#3f8f4f', box: '#eef6e8', line: '#2f6b3b'},
  process: {x: 710, w: 520, head: '#2d5a3a', box: '#d6ecd4', line: '#2d5a3a'},
  output: {x: 1330, w: 560, head: '#d4622b', box: '#fff6d8', line: '#7a5a1c'},
};

const Head: React.FC<{c: typeof COL.input; text: string}> = ({c, text}) => (
  <div
    style={{
      position: 'absolute', left: c.x, top: 26, width: c.w, height: 54, borderRadius: 27, background: c.head,
      color: '#fff', fontFamily: POP, fontSize: 28, display: 'flex', alignItems: 'center', justifyContent: 'center', letterSpacing: 4,
    }}
  >
    {text}
  </div>
);

const Box: React.FC<{x: number; y: number; w: number; h: number; bg: string; line: string; children: React.ReactNode}> = ({x, y, w, h, bg, line, children}) => (
  <div style={{position: 'absolute', left: x, top: y, width: w, height: h, background: bg, border: `3px solid ${line}`, borderRadius: 18, padding: '18px 22px', boxSizing: 'border-box', fontFamily: BODY, color: INK}}>
    {children}
  </div>
);

const Title: React.FC<{text: string; sub?: string; size?: number}> = ({text, sub, size = 30}) => (
  <div style={{marginBottom: 10}}>
    <div style={{fontFamily: POP, fontSize: size, lineHeight: 1.2}}>{text}</div>
    {sub && <div style={{fontSize: 17, opacity: 0.75, marginTop: 2}}>{sub}</div>}
  </div>
);

const Steps: React.FC<{items: React.ReactNode[]; size?: number}> = ({items, size = 19}) => (
  <div style={{fontSize: size, lineHeight: 1.5}}>
    {items.map((t, i) => (
      <div key={i} style={{display: 'flex', gap: 8, marginBottom: 4}}>
        <span style={{flexShrink: 0, width: 26, height: 26, borderRadius: 13, border: `2px solid ${INK}`, fontSize: 15, display: 'flex', alignItems: 'center', justifyContent: 'center', marginTop: 2, fontWeight: 700}}>
          {i + 1}
        </span>
        <span>{t}</span>
      </div>
    ))}
  </div>
);

const Note: React.FC<{children: React.ReactNode; color?: string}> = ({children, color = INK}) => (
  <div style={{fontSize: 17, lineHeight: 1.55, color}}>{children}</div>
);

// ---- 絵 ----
// 圧力マット: 上下に横長、左右に縦長のパッド(実物と同じ並び)。前が踏まれている状態
export const MatIcon: React.FC = () => (
  <svg width={190} height={190} viewBox="0 0 190 190">
    <rect x={5} y={5} width={180} height={180} rx={18} fill="#3a3f4a" stroke={INK} strokeWidth={4} />
    <rect x={50} y={20} width={90} height={28} rx={6} fill="#6bff95" stroke={INK} strokeWidth={3} />
    <rect x={50} y={142} width={90} height={28} rx={6} fill="#c9ced6" stroke={INK} strokeWidth={3} />
    <rect x={20} y={50} width={28} height={90} rx={6} fill="#c9ced6" stroke={INK} strokeWidth={3} />
    <rect x={142} y={50} width={28} height={90} rx={6} fill="#c9ced6" stroke={INK} strokeWidth={3} />
    <text x={95} y={40} fontSize={16} textAnchor="middle" fontFamily={POP} fill={INK}>前</text>
    <text x={95} y={162} fontSize={16} textAnchor="middle" fontFamily={POP} fill={INK}>後</text>
    <text x={34} y={101} fontSize={16} textAnchor="middle" fontFamily={POP} fill={INK}>左</text>
    <text x={156} y={101} fontSize={16} textAnchor="middle" fontFamily={POP} fill={INK}>右</text>
    {/* 足あと */}
    <ellipse cx={82} cy={98} rx={11} ry={18} fill="#f3c9a0" stroke={INK} strokeWidth={2.5} />
    <ellipse cx={108} cy={92} rx={11} ry={18} fill="#f3c9a0" stroke={INK} strokeWidth={2.5} />
  </svg>
);

export const PhoneIcon: React.FC<{color: string; on?: boolean; size?: number}> = ({color, on = true, size = 1}) => (
  <svg width={70 * size} height={120 * size} viewBox="0 0 70 120">
    <rect x={3} y={3} width={64} height={114} rx={12} fill="#1b1f24" stroke={INK} strokeWidth={3} />
    <rect x={9} y={12} width={52} height={96} rx={6} fill="#0b1712" stroke={color} strokeWidth={3} />
    <rect x={14} y={18} width={42} height={36} rx={4} fill="#1f3a2c" />
    <circle cx={30} cy={36} r={4} fill="#9dffb0" />
    <circle cx={42} cy={30} r={7} fill={color} opacity={0.7} />
    <rect x={15} y={62} width={40} height={38} rx={19} fill={on ? color : '#1d2b25'} stroke={color} strokeWidth={2} />
    <text x={35} y={85} fontSize={9} textAnchor="middle" fontFamily={POP} fill={on ? '#111' : color}>ライトON</text>
  </svg>
);

export const ChipIcon: React.FC = () => (
  <svg width={130} height={86} viewBox="0 0 130 86">
    {Array.from({length: 9}).map((_, i) => (
      <React.Fragment key={i}>
        <rect x={14 + i * 12} y={2} width={5} height={12} fill="#c9a24a" />
        <rect x={14 + i * 12} y={72} width={5} height={12} fill="#c9a24a" />
      </React.Fragment>
    ))}
    <rect x={6} y={12} width={118} height={62} rx={6} fill="#1e2228" stroke={INK} strokeWidth={3} />
    <rect x={18} y={24} width={94} height={38} rx={3} fill="#8a8f98" />
    <text x={65} y={49} fontSize={18} textAnchor="middle" fontFamily={BODY} fontWeight={700} fill="#1e2228">ESP32</text>
  </svg>
);

export const LaptopIcon: React.FC = () => (
  <svg width={150} height={100} viewBox="0 0 150 100">
    <rect x={20} y={6} width={110} height={70} rx={6} fill="#2b3036" stroke={INK} strokeWidth={3} />
    <rect x={28} y={13} width={94} height={56} fill="#2f6b3b" />
    <polygon points="28,69 55,40 75,58 95,32 122,69" fill="#7fc98a" />
    <path d="M6 80 H144 L134 94 H16 Z" fill="#9aa1aa" stroke={INK} strokeWidth={3} strokeLinejoin="round" />
  </svg>
);

export const ProjectorIcon: React.FC = () => (
  <svg width={150} height={90} viewBox="0 0 150 90">
    <path d="M100 38 L148 10 L148 80 L100 52 Z" fill="#ffe58a" opacity={0.75} />
    <rect x={8} y={24} width={96} height={44} rx={10} fill="#f4f1ea" stroke={INK} strokeWidth={3} />
    <circle cx={84} cy={46} r={14} fill="#2b3036" stroke={INK} strokeWidth={3} />
    <circle cx={84} cy={46} r={6} fill="#8fd3ff" />
    <rect x={20} y={36} width={34} height={6} rx={3} fill="#c9ced6" />
    <rect x={20} y={50} width={24} height={6} rx={3} fill="#c9ced6" />
    <rect x={20} y={68} width={10} height={10} fill={INK} />
    <rect x={82} y={68} width={10} height={10} fill={INK} />
  </svg>
);

const CloudIcon: React.FC = () => (
  <svg width={64} height={42} viewBox="0 0 64 42">
    <path d="M16 38 C4 38 2 24 12 21 C10 9 26 4 32 13 C36 4 54 6 52 20 C62 21 62 38 50 38 Z" fill="#fff" stroke="#e8822b" strokeWidth={3} />
  </svg>
);

// ---- 矢印 ----
type Pt = [number, number];
export const Arrow: React.FC<{pts: Pt[]; color?: string; dashed?: boolean; width?: number}> = ({pts, color = INK, dashed = false, width = 4}) => {
  const [a, b] = [pts[pts.length - 2], pts[pts.length - 1]];
  const ang = Math.atan2(b[1] - a[1], b[0] - a[0]);
  const s = 16;
  const head = [
    [b[0], b[1]],
    [b[0] - s * Math.cos(ang - 0.45), b[1] - s * Math.sin(ang - 0.45)],
    [b[0] - s * Math.cos(ang + 0.45), b[1] - s * Math.sin(ang + 0.45)],
  ];
  const line = pts.slice(0, -1).concat([[b[0] - (s - 4) * Math.cos(ang), b[1] - (s - 4) * Math.sin(ang)]]);
  return (
    <>
      <polyline points={line.map((p) => p.join(',')).join(' ')} fill="none" stroke={color} strokeWidth={width} strokeDasharray={dashed ? '12 9' : undefined} strokeLinejoin="round" />
      <polygon points={head.map((p) => p.join(',')).join(' ')} fill={color} />
    </>
  );
};

const Label: React.FC<{x: number; y: number; w: number; children: React.ReactNode; align?: 'left' | 'center' | 'right'; color?: string}> = ({x, y, w, children, align = 'center', color = INK}) => (
  <div style={{position: 'absolute', left: x, top: y, width: w, textAlign: align, fontFamily: BODY, fontSize: 16, lineHeight: 1.4, color, fontWeight: 600}}>
    {children}
  </div>
);

export const SystemDiagram: React.FC = () => {
  const I = COL.input, P = COL.process, O = COL.output;
  return (
    <AbsoluteFill style={{background: '#fbfaf5'}}>
      <Head c={I} text="入力" />
      <Head c={P} text="処理" />
      <Head c={O} text="出力" />

      {/* ---- 入力 ---- */}
      <Box x={I.x} y={110} w={I.w} h={420} bg={I.box} line={I.line}>
        <Title text="圧力マット(主人公役)" sub="カメレオンを動かす人" />
        <div style={{display: 'flex', gap: 20, alignItems: 'center'}}>
          <MatIcon />
          <Note>
            前・後・左・右の4枚のパッドに<b>圧力センサ</b>が入っている。
            <br />
            踏んだ向きにカメレオンが進み、
            <br />
            <b>止まると擬態</b>して敵から隠れる。
          </Note>
        </div>
        <div style={{marginTop: 14, borderTop: `2px dashed ${I.line}`, paddingTop: 10}}>
          <Note>圧力センサ … 踏まれた力を電気の信号に変える部品。踏んだパッドのセンサだけが強く反応する。</Note>
        </div>
      </Box>

      <Box x={I.x} y={570} w={I.w} h={380} bg={I.box} line={I.line}>
        <Title text="スマホ(灯り役・最大7人)" sub="ライトで敵を照らす人" />
        <div style={{display: 'flex', gap: 14, alignItems: 'flex-end', marginBottom: 12}}>
          {LIGHT_COLORS.slice(0, 4).map((c, i) => (
            <PhoneIcon key={i} color={c} on={i !== 2} size={i === 0 ? 1.15 : 0.9} />
          ))}
        </div>
        <Note>
          画面の<b>QRコードを読むだけ</b>で参加(アプリ不要)。
          <br />
          <b>「ライトON」を押している間</b>だけ光り、スマホを動かすと
          <br />
          画面の中のライトが動く。ライトの色は人ごとに違う。
        </Note>
      </Box>

      {/* ---- 処理 ---- */}
      <Box x={P.x} y={110} w={P.w} h={300} bg={P.box} line={P.line}>
        <div style={{display: 'flex', gap: 16, alignItems: 'center', marginBottom: 8}}>
          <ChipIcon />
          <Title text="ESP32" sub="マイコン" />
        </div>
        <Steps items={['4枚の圧力センサの値を読む', 'どのパッドが踏まれているか判定する', 'パソコンへ送る']} />
        <div style={{marginTop: 8}}>
          <Note>USBケーブルで送る(Wi-Fiで送ることもできる)。パソコンは差したESP32を自動で見つける。</Note>
        </div>
      </Box>

      <Box x={P.x} y={490} w={P.w} h={460} bg="#fde6d8" line="#8a4a2a">
        <div style={{display: 'flex', gap: 16, alignItems: 'center', marginBottom: 8}}>
          <LaptopIcon />
          <Title text="パソコン" sub="Unity(ゲームエンジン)" />
        </div>
        <Steps
          size={18}
          items={[
            'マットの入力でカメレオンを動かす・擬態',
            <>スマホの光が当たった敵にダメージ<br />(光を重ねると2倍)</>,
            '雷・ランタン・蛍の石・門などの仕掛け',
            'スコアを計算し、ランキングを記録(CSV)',
            <>スマホと通信するサーバー<br />(参加用のQRコードもここで作る)</>,
          ]}
        />
      </Box>

      {/* ---- 出力 ---- */}
      <Box x={O.x} y={110} w={O.w} h={420} bg={O.box} line={O.line}>
        <div style={{display: 'flex', gap: 14, alignItems: 'center', marginBottom: 8}}>
          <ProjectorIcon />
          <Title text="モニター・プロジェクター" size={25} />
        </div>
        <div style={{display: 'flex', gap: 14}}>
          <Img src={staticFile('diagram/game.png')} style={{width: 270, height: 152, objectFit: 'cover', borderRadius: 8, border: `2px solid ${INK}`, flexShrink: 0}} />
          <Note>
            夜の森のゲーム画面・敵・スコア・ランキング、参加用のQRコード、マットの反応を映す。
          </Note>
        </div>
        <div style={{marginTop: 10}}>
          <Note>灯り役の光が当たった敵は赤く光り、光を重ねると<b>白く焼けるように光る</b>。</Note>
        </div>
        <div style={{marginTop: 8, fontFamily: POP, fontSize: 22, color: RED}}>敵を照らして倒し、門から脱出！</div>
      </Box>

      <Box x={O.x} y={570} w={O.w} h={380} bg={O.box} line={O.line}>
        <div style={{display: 'flex', gap: 16, alignItems: 'center', marginBottom: 8}}>
          <PhoneIcon color={LIGHT_COLORS[1]} size={1.1} />
          <Title text="スマホの画面" sub="灯り役の手元" />
        </div>
        <Note>
          画面の縁がライトの色に光る。
          <br />
          倒した敵の数を表示し、倒すと縁が点滅。
          <br />
          敵に当てている間は<b>振動</b>(Android)。
          <br />
          ゲームが終わると結果画面(撃破数・順位)。
        </Note>
      </Box>

      {/* ---- 矢印とラベル ---- */}
      <svg width={DIAGRAM_W} height={DIAGRAM_H} style={{position: 'absolute', left: 0, top: 0}}>
        {/* マット → ESP32 */}
        <Arrow pts={[[I.x + I.w, 260], [P.x, 260]]} />
        {/* ESP32 → パソコン */}
        <Arrow pts={[[P.x + 120, 410], [P.x + 120, 490]]} />
        {/* スマホ → パソコン */}
        <Arrow pts={[[I.x + I.w, 790], [P.x, 790]]} />
        {/* パソコン → モニター */}
        <Arrow pts={[[P.x + P.w, 620], [1280, 620], [1280, 320], [O.x, 320]]} />
        {/* パソコン → スマホの画面 */}
        <Arrow pts={[[P.x + P.w, 790], [O.x, 790]]} />
        {/* 画面を見て操作する(ループ) */}
        <Arrow pts={[[O.x + O.w / 2, 950], [O.x + O.w / 2, 1040], [I.x + I.w / 2, 1040], [I.x + I.w / 2, 950]]} color="#e07a2b" dashed width={4} />
      </svg>

      <Label x={I.x + I.w - 2} y={212} w={P.x - I.x - I.w + 4}>踏んだ<br />信号</Label>
      <Label x={P.x + 140} y={418} w={360} align="left">USB(シリアル通信)で<br />踏んだパッドを送る</Label>
      <Label x={I.x + I.w - 6} y={700} w={P.x - I.x - I.w + 12}>照らす<br />位置</Label>
      <div style={{position: 'absolute', left: I.x + I.w + 18, top: 812}}>
        <CloudIcon />
      </div>
      <Label x={I.x + I.w - 30} y={858} w={P.x - I.x - I.w + 60} color="#b4561c">Wi-Fi<br />または5G</Label>
      <Label x={1232} y={445} w={46}>映像</Label>
      <Label x={P.x + P.w - 4} y={726} w={O.x - P.x - P.w + 8}>撃破数・<br />状態</Label>
      <Label x={I.x + 300} y={1052} w={1300} color="#c45a1c">
        <span style={{fontSize: 20}}>画面を見て、主人公役はマットを踏んで動き、灯り役はスマホで敵を照らす</span>
      </Label>

      {/* 通信のくわしさ(小さな注記) */}
      <div style={{position: 'absolute', left: P.x, top: 958, width: P.w, fontFamily: BODY, fontSize: 14, color: '#5b6b62', lineHeight: 1.45}}>
        スマホとパソコンは WebSocket で通信。5G のスマホは Cloudflare トンネル(外から入れる一時的なURL)を通って参加する。
      </div>
    </AbsoluteFill>
  );
};

// ---- シンプル版: 文字を減らし、絵と見出しを大きくした図 ----
export const SIMPLE_W = 1920;
export const SIMPLE_H = 1080;

const Big: React.FC<{x: number; y: number; w: number; h: number; bg: string; line: string; icon: React.ReactNode; title: string; line1: string; who?: string}> = ({
  x, y, w, h, bg, line, icon, title, line1, who,
}) => (
  <div
    style={{
      position: 'absolute', left: x, top: y, width: w, height: h, background: bg, border: `4px solid ${line}`, borderRadius: 24,
      display: 'flex', alignItems: 'center', gap: 24, padding: '0 28px', boxSizing: 'border-box', color: INK,
    }}
  >
    <div style={{flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>{icon}</div>
    <div>
      {who && <div style={{fontFamily: BODY, fontSize: 24, fontWeight: 700, opacity: 0.7, marginBottom: 2}}>{who}</div>}
      <div style={{fontFamily: POP, fontSize: 46, lineHeight: 1.15}}>{title}</div>
      <div style={{fontFamily: POP, fontSize: 28, color: RED, marginTop: 8}}>{line1}</div>
    </div>
  </div>
);

const BigLabel: React.FC<{x: number; y: number; w: number; text: string; color?: string}> = ({x, y, w, text, color = INK}) => (
  <div style={{position: 'absolute', left: x, top: y, width: w, textAlign: 'center', fontFamily: POP, fontSize: 24, lineHeight: 1.25, color, whiteSpace: 'pre'}}>{text}</div>
);

const Scale: React.FC<{k: number; children: React.ReactNode}> = ({k, children}) => <div style={{transform: `scale(${k})`, transformOrigin: 'center'}}>{children}</div>;

export const SystemDiagramSimple: React.FC = () => {
  const I = {x: 30, w: 560}, P = {x: 690, w: 540}, O = {x: 1330, w: 560};
  const top = 130, bottom = 560, h = 360;
  return (
    <AbsoluteFill style={{background: '#fbfaf5'}}>
      <Head c={{...COL.input, ...I}} text="入力" />
      <Head c={{...COL.process, ...P}} text="処理" />
      <Head c={{...COL.output, ...O}} text="出力" />

      <Big x={I.x} y={top} w={I.w} h={h} bg={COL.input.box} line={COL.input.line} who="主人公役" title="圧力マット" line1="踏んで動かす"
        icon={<Scale k={1}><MatIcon /></Scale>} />
      <Big x={I.x} y={bottom} w={I.w} h={h} bg={COL.input.box} line={COL.input.line} who="灯り役(最大7人)" title="スマホ" line1="照らして倒す"
        icon={<PhoneIcon color={LIGHT_COLORS[0]} size={1.6} />} />

      <Big x={P.x} y={top} w={P.w} h={h} bg={COL.process.box} line={COL.process.line} who="マイコン" title="ESP32" line1="踏んだ所を判定"
        icon={<Scale k={1.2}><ChipIcon /></Scale>} />
      <Big x={P.x} y={bottom} w={P.w} h={h} bg="#fde6d8" line="#8a4a2a" who="Unity" title="パソコン" line1="ゲームを動かす"
        icon={<Scale k={1.15}><LaptopIcon /></Scale>} />

      <div
        style={{
          position: 'absolute', left: O.x, top, width: O.w, height: h, background: COL.output.box, border: `4px solid ${COL.output.line}`, borderRadius: 24,
          boxSizing: 'border-box', padding: 24, color: INK, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12,
        }}
      >
        <div style={{display: 'flex', alignItems: 'center', gap: 14}}>
          <ProjectorIcon />
          <div style={{fontFamily: POP, fontSize: 40}}>プロジェクター</div>
        </div>
        <Img src={staticFile('diagram/game.png')} style={{width: 400, height: 210, objectFit: 'cover', borderRadius: 10, border: `3px solid ${INK}`}} />
      </div>
      <Big x={O.x} y={bottom} w={O.w} h={h} bg={COL.output.box} line={COL.output.line} who="灯り役の手元" title="スマホの画面" line1="撃破数・振動"
        icon={<PhoneIcon color={LIGHT_COLORS[1]} size={1.6} />} />

      <svg width={SIMPLE_W} height={SIMPLE_H} style={{position: 'absolute', left: 0, top: 0}}>
        <Arrow pts={[[I.x + I.w, top + h / 2], [P.x, top + h / 2]]} width={6} />
        <Arrow pts={[[P.x + P.w / 2, top + h], [P.x + P.w / 2, bottom]]} width={6} />
        <Arrow pts={[[I.x + I.w, bottom + h / 2], [P.x, bottom + h / 2]]} width={6} />
        <Arrow pts={[[P.x + P.w, bottom + 90], [1282, bottom + 90], [1282, top + h / 2], [O.x, top + h / 2]]} width={6} />
        <Arrow pts={[[P.x + P.w, bottom + h / 2 + 40], [O.x, bottom + h / 2 + 40]]} width={6} />
        <Arrow pts={[[O.x + O.w / 2, bottom + h], [O.x + O.w / 2, 1000], [I.x + I.w / 2, 1000], [I.x + I.w / 2, bottom + h]]} color="#e07a2b" dashed width={5} />
      </svg>

      <BigLabel x={I.x + I.w - 10} y={top + h / 2 - 66} w={P.x - I.x - I.w + 20} text={'踏んだ\n信号'} />
      <BigLabel x={P.x + P.w / 2 + 10} y={top + h + 18} w={140} text="USB" />
      <BigLabel x={I.x + I.w - 14} y={bottom + h / 2 - 66} w={P.x - I.x - I.w + 28} text={'Wi-Fi\n/ 5G'} />
      <BigLabel x={1234} y={top + h + 30} w={50} text={'映\n像'} />
      <BigLabel x={P.x + P.w - 6} y={bottom + h / 2 - 34} w={O.x - P.x - P.w + 12} text="結果" />
      <BigLabel x={I.x + 260} y={1012} w={1300} color="#c45a1c" text="画面を見て、踏む・照らす" />
    </AbsoluteFill>
  );
};
