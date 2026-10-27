// カメレオンサバイバー 紹介動画(1分・16:9)。BGM の拍(172BPM)に合わせて場面と演出を切り替える。
// 場面の長さは拍数で書く(b(拍) でフレームになる)。録画素材は public/footage.mp4(ゲームのデモ撮影モードで撮ったもの)
import React from 'react';
import {AbsoluteFill, Audio, interpolate, Sequence, staticFile, useCurrentFrame} from 'remotion';
import {
  b,
  BEAT,
  BeatBorder,
  Bolt,
  BounceChars,
  Burst,
  Confetti,
  Enter,
  EnterKind,
  Flash,
  Footage,
  Glitch,
  HEAVY,
  LIGHT_COLORS,
  Letterbox,
  POP,
  Punch,
  Shake,
  Shockwave,
  Sparkles,
  SpeedLines,
  Sticker,
  WarningTape,
} from './fx';
import {MatMock, PhoneMock, PlayerDots} from './mocks';

export const PROMO_FRAMES = b(172); // 約60秒

// ---- 部品 ----
const Section: React.FC<{from: number; to: number; enter?: EnterKind; children: React.ReactNode}> = ({
  from,
  to,
  enter = 'zoom',
  children,
}) => (
  <Sequence from={b(from)} durationInFrames={b(to) - b(from)}>
    <Enter kind={enter}>{children}</Enter>
  </Sequence>
);

const Sfx: React.FC<{at: number; src: string; volume?: number}> = ({at, src, volume = 0.9}) => (
  <Sequence from={Math.max(0, at)} durationInFrames={90}>
    <Audio src={staticFile(`audio/${src}.mp3`)} volume={volume} />
  </Sequence>
);

const Bg: React.FC<{a: string; c: string}> = ({a, c}) => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill
      style={{
        background: `radial-gradient(circle at ${50 + Math.sin(f / 20) * 20}% ${50 + Math.cos(f / 25) * 20}%, ${a}, ${c} 70%)`,
      }}
    />
  );
};

// 暗くした録画の上に文字を乗せるときの、下からのグラデーション
const Shade: React.FC<{amount?: number}> = ({amount = 0.55}) => (
  <AbsoluteFill style={{background: `linear-gradient(180deg, rgba(0,0,0,${amount * 0.6}) 0%, rgba(0,0,0,0) 35%, rgba(0,0,0,0) 60%, rgba(0,0,0,${amount}) 100%)`}} />
);

const KO: React.FC<{at: number; x: number; y: number; color?: string}> = ({at, x, y, color = '#ff6b6b'}) => (
  <>
    <Burst at={at} x={x} y={y} size={260} color={color} points={10} />
    <Punch at={at} text={'撃破!'} size={80} x={x} y={y} rotate={-10} color="#fff" until={at + 16} />
  </>
);

// 進み具合のバー(下)
const Progress: React.FC = () => {
  const f = useCurrentFrame();
  return (
    <div style={{position: 'absolute', left: 0, bottom: 0, height: 12, width: `${(f / PROMO_FRAMES) * 100}%`, background: `linear-gradient(90deg, ${LIGHT_COLORS.join(',')})`}} />
  );
};

// ---- 本編 ----
export const Promo: React.FC = () => {
  const hook: {src: number; text: string; color: string; enter: EnterKind; sfx: string}[] = [
    {src: 905, text: '夜の森に…', color: '#9dffb0', enter: 'zoom', sfx: 'whoosh'},
    {src: 1487, text: 'ヤツらが来る!!', color: '#ff6b6b', enter: 'glitch', sfx: 'scratch'},
    {src: 415, text: '光で', color: '#ffe066', enter: 'whip', sfx: 'whoosh'},
    {src: 1196, text: 'ぶっ倒せ!!', color: '#6bdfff', enter: 'spin', sfx: 'hit3'},
  ];
  const ctaShots = [418, 910, 1495, 624, 1300, 1612, 980, 1200];

  return (
    <AbsoluteFill style={{background: '#000', fontFamily: POP}}>
      {/* 1. つかみ: 2拍ごとに山場を4連発 */}
      {hook.map((h, i) => (
        <Section key={i} from={i * 2} to={i * 2 + 2} enter={h.enter}>
          <Shake hits={[0]} power={22}>
            <Footage from={h.src} dur={b(2)} zoom={[1.5, 1.8]} />
          </Shake>
          <Shade />
          <Punch text={h.text} size={170} color={h.color} y={260} from={i % 2 ? 'right' : 'left'} rotate={i % 2 ? 4 : -4} />
          <Flash at={0} len={5} peak={0.7} />
        </Section>
      ))}

      {/* 2. タイトル */}
      <Section from={8} to={16} enter="iris">
        <Shake hits={[b(1), b(3)]} power={30}>
          <Bg a="#2b7a4b" c="#03110a" />
          <SpeedLines color="#9dffb0" opacity={0.35} />
          <Burst at={b(0.5)} size={1300} color="#14301f" points={18} />
          <Punch at={0} text="🦎" size={260} y={-250} from="spin" rotate={0} stroke="transparent" />
          <BounceChars text="カメレオン" at={b(1)} step={2} size={190} y={-20} />
          <BounceChars text="サバイバー" at={b(3)} step={2} size={190} y={200} />
          <Sticker at={b(5)} text="スマホが ライトになる!" x={520} y={-330} bg="#6bdfff" rotate={10} size={46} />
        </Shake>
        <Flash at={b(1)} len={6} />
        <Flash at={b(3)} len={6} />
      </Section>

      {/* 3. 主人公: 足踏みで移動 */}
      <Section from={16} to={24} enter="whip">
        <Footage from={326} dur={b(8)} rate={1.4} zoom={[1.3, 1.5]} focus={[50, 40]} />
        <Shade amount={0.7} />
        <MatMock x={-560} y={60} at={b(2)} />
        <Punch at={0} text={'キミは\nカメレオン!!'} size={130} color="#9dffb0" x={330} y={-200} from="right" rotate={-5} />
        <Punch at={b(3)} text="足ぶみで移動!" size={110} color="#ffe066" x={330} y={260} from="pop" rotate={3} />
        <Sticker at={b(5)} text="圧力マット" x={-560} y={-260} bg="#b388ff" rotate={-10} />
      </Section>

      {/* 4. 灯り役: スマホで参加 */}
      <Section from={24} to={36} enter="up">
        <Bg a="#3a2a6b" c="#0b0716" />
        <SpeedLines color="#b388ff" opacity={0.2} />
        <PhoneMock x={-520} y={40} at={0} pressAt={b(3)} />
        <Punch at={b(0.5)} text={'友だちは\nスマホで参戦!!'} size={120} color="#fff" x={300} y={-250} from="drop" rotate={-3} />
        <Sticker at={b(4)} text="QRを読むだけ" x={150} y={30} bg="#ffe066" rotate={-6} />
        <Sticker at={b(5)} text="アプリ不要!" x={520} y={60} bg="#6bff95" rotate={8} />
        <Punch at={b(7)} text="最大7人!" size={120} color="#ffe066" x={330} y={200} from="pop" rotate={-4} until={b(12)} />
        <Sequence from={b(8)}>
          <div style={{position: 'absolute', left: 520, right: 0, top: 0, bottom: 0}}>
            <PlayerDots at={0} every={Math.round(BEAT / 2)} y={330} />
          </div>
        </Sequence>
      </Section>

      {/* 5. 光で敵を倒す */}
      <Section from={36} to={48} enter="shutter">
        <Shake hits={[b(4), b(6), b(8), b(10)]} power={20}>
          <Footage from={405} dur={b(12)} zoom={[1.5, 1.9]} focus={[50, 30]} />
        </Shake>
        <Shade />
        <Punch at={0} text="スマホのライトで…" size={100} color="#fff" y={-330} from="left" rotate={-2} until={b(4)} />
        <Punch at={b(4)} text="照らして撃破!!" size={150} color="#ffe066" y={-320} from="pop" rotate={-4} />
        <KO at={b(4)} x={-420} y={60} />
        <KO at={b(6)} x={430} y={-40} color="#6bdfff" />
        <KO at={b(8)} x={-300} y={250} color="#b388ff" />
        <KO at={b(10)} x={380} y={230} color="#6bff95" />
        <Sticker at={b(6)} text="当てている間 スマホも鳴る!" x={0} y={390} bg="#ff6bd6" size={44} rotate={2} />
      </Section>

      {/* 6. 擬態 → 見つかる */}
      <Section from={48} to={54} enter="diag">
        <Footage from={600} dur={b(6)} rate={1.4} zoom={[1.6, 1.9]} focus={[50, 32]} filter={`hue-rotate(0deg)`} />
        <HueSweep />
        <Shade />
        <Punch at={0} text="止まると…" size={110} color="#fff" y={-320} from="left" rotate={-3} />
        <Punch at={b(2)} text="擬態!!" size={230} rainbow y={40} from="spin" rotate={-6} />
        <QuestionMarks at={b(3)} />
      </Section>
      <Section from={54} to={60} enter="glitch">
        <Shake hits={[0, b(2), b(4)]} power={28}>
          <Footage from={700} dur={b(6)} rate={1.2} zoom={[1.6, 1.7]} focus={[50, 32]} filter="sepia(0.3) hue-rotate(-30deg) saturate(2)" />
        </Shake>
        <AbsoluteFill style={{background: 'rgba(255,0,40,0.18)'}} />
        <Punch at={0} text="！" size={420} color="#ff3b3b" y={-60} from="pop" rotate={8} />
        <Punch at={b(1)} text="見つかった!!" size={150} color="#fff" y={300} from="pop" rotate={-3} />
        <Flash at={0} color="#ff0033" len={8} peak={0.6} />
      </Section>

      {/* 7. ランタン */}
      <Section from={60} to={72} enter="zoom">
        <Shake hits={[50]} power={45}>
          <Footage from={860} dur={b(12)} zoom={[1.3, 1.6]} focus={[50, 45]} />
        </Shake>
        <Shade />
        <Punch at={0} text="ランタンを照らすと…" size={100} color="#fff" y={-330} from="right" rotate={2} until={50} />
        <Shockwave at={50} color="#ffe066" y={-60} />
        <Shockwave at={56} color="#fff" y={-60} />
        <Flash at={50} len={10} />
        <Burst at={52} size={760} color="#ffe066" y={40} />
        <Punch at={52} text={'まとめて\n一掃!!'} size={170} color="#ff6b6b" y={40} from="pop" rotate={-6} />
      </Section>

      {/* 8. 導きの花 → 蛍 */}
      <Section from={72} to={78} enter="iris">
        <Footage from={1060} dur={b(6)} rate={2} zoom={[1.3, 1.5]} />
        <Shade />
        <Punch at={0} text={'花が 道を\n教えてくれる🌸'} size={120} color="#ffb3e6" y={250} from="drop" rotate={-3} />
      </Section>
      <Section from={78} to={84} enter="whip">
        <Footage from={1250} dur={b(6)} rate={1.3} zoom={[1.5, 1.8]} focus={[50, 40]} />
        <Sparkles />
        <Shade />
        <Punch at={0} text={'石を照らして\n蛍ゲット✨'} size={130} color="#d7ff6b" y={-250} from="pop" rotate={4} />
      </Section>

      {/* 9. 敵が増える */}
      <Section from={84} to={92} enter="glitch">
        <Shake hits={[0, b(2), b(4), b(6)]} power={24}>
          <Footage from={1330} dur={b(8)} rate={1.3} zoom={[1.4, 1.7]} />
        </Shake>
        <RedPulse />
        <WarningTape text="WARNING 敵が増える" />
        <Punch at={b(1)} text={'どんどん\n増える!!'} size={170} color="#ff6b6b" from="pop" rotate={-5} />
      </Section>

      {/* 10. 雷: 落ちる瞬間からスロー */}
      <Section from={92} to={95} enter="shutter">
        <Footage from={1461} dur={b(3)} zoom={[1.4, 1.6]} />
        <Shade />
        <Punch at={0} text="ゴロゴロ…" size={110} color="#9be7ff" y={-320} from="left" rotate={-3} />
      </Section>
      <Section from={95} to={104} enter="none">
        <Shake hits={[0]} power={55}>
          <Footage from={1491} dur={b(9)} rate={0.5} zoom={[1.5, 1.9]} focus={[50, 40]} />
        </Shake>
        <Bolt at={0} x={-120} />
        <Flash at={0} len={12} />
        <Letterbox at={2} />
        <Punch at={2} text={'雷\nドカーン!!'} size={200} color="#fff" from="pop" rotate={-6} stroke="#1a3a8f" />
        <Sticker at={b(3)} text="SLOW" x={700} y={-380} bg="#fff" size={48} rotate={6} />
      </Section>

      {/* 11. 脱出 */}
      <Section from={104} to={112} enter="spin">
        <Footage from={1600} dur={b(8)} rate={2.4} zoom={[1.4, 1.2]} focus={[50, 55]} />
        <SpeedLines opacity={0.4} />
        <Shade />
        <Punch at={b(1)} text={'門をくぐって\n脱出!!'} size={160} color="#9dffb0" from="right" rotate={-4} />
      </Section>

      {/* 12. ランキング */}
      <Section from={112} to={124} enter="up">
        <Footage from={1875} dur={b(12)} rate={0.5} zoom={[1.2, 1.6]} focus={[50, 50]} filter="brightness(0.8)" />
        <AbsoluteFill style={{background: 'rgba(0,0,0,0.35)'}} />
        <Burst at={b(1)} size={700} color="#ffe066" y={-60} />
        <Punch at={b(1)} text="👑" size={240} y={-90} rotate={0} stroke="transparent" from="drop" />
        <Punch at={b(2)} text="ランキング1位を狙え!" size={120} color="#ffe066" y={200} from="pop" rotate={-3} />
        <Sticker at={b(5)} text="マップごとに TOP10" x={-520} y={-330} bg="#6bdfff" rotate={-8} />
        <Sticker at={b(6)} text="クリアタイム+撃破数" x={520} y={-330} bg="#ff9f6b" rotate={8} />
        <Confetti at={b(2)} count={60} />
      </Section>

      {/* 13. 特徴を連打 */}
      {[
        {t: '📱 アプリ不要', s: 'QRを読むだけで参加', bg: ['#1b5e9e', '#06111f'], e: 'whip' as EnterKind, src: 0},
        {t: '👥 最大7人', s: 'みんなで同時にプレイ', bg: ['#8a2b6b', '#160611'], e: 'zoom' as EnterKind, src: 1612},
        {t: '🗺️ 3つのマップ', s: 'やさしい・ふつう・むずかしい', bg: ['#2b7a4b', '#03110a'], e: 'spin' as EnterKind, src: 120},
        {t: '🦎 足ぶみ×スマホ', s: 'リアルに動く協力プレイ', bg: ['#9e6b1b', '#1a0f02'], e: 'diag' as EnterKind, src: 418},
      ].map((c, i) => (
        <Section key={i} from={124 + i * 4} to={128 + i * 4} enter={c.e}>
          <Footage from={c.src} dur={b(4)} zoom={[1.1, 1.25]} filter="blur(6px) brightness(0.6)" />
          <AbsoluteFill style={{background: `radial-gradient(circle, ${c.bg[0]}cc, ${c.bg[1]}ee 70%)`}} />
          <SpeedLines opacity={0.18} />
          <Punch at={0} text={c.t} size={150} color="#fff" y={-60} from={i % 2 ? 'left' : 'pop'} rotate={i % 2 ? 3 : -3} />
          <Punch at={b(1)} text={c.s} size={70} color={LIGHT_COLORS[(i * 2) % 7]} y={120} font={POP} rotate={0} from="drop" />
        </Section>
      ))}

      {/* 14. 呼びかけ: 2拍ごとに切り替わる録画の上に */}
      <Sequence from={b(140)} durationInFrames={b(16)}>
        {ctaShots.map((src, i) => (
          <Sequence key={i} from={b(i * 2)} durationInFrames={b(i * 2 + 2) - b(i * 2)}>
            <Enter kind={(['zoom', 'whip', 'iris', 'shutter'] as EnterKind[])[i % 4]}>
              <Footage from={src} dur={b(2)} zoom={[1.4, 1.7]} filter="brightness(0.75)" />
            </Enter>
          </Sequence>
        ))}
        <Shade amount={0.7} />
        <Punch at={b(1)} text={'みんなで\n生き残れ!!'} size={190} rainbow from="pop" rotate={-4} y={-60} />
        <Sticker at={b(8)} text="高専祭 専門展で遊べる!" x={0} y={330} bg="#ffe066" size={64} rotate={-3} />
        <Glitch at={b(8)} />
      </Sequence>

      {/* 15. エンドカード */}
      <Section from={156} to={172} enter="iris">
        <Bg a="#2b7a4b" c="#020a06" />
        <SpeedLines color="#9dffb0" opacity={0.25} />
        <Confetti count={110} />
        <Punch at={0} text="🦎" size={220} y={-300} rotate={0} stroke="transparent" from="drop" />
        <BounceChars text="カメレオンサバイバー" at={b(0.5)} step={1} size={150} y={-40} />
        <Punch at={b(4)} text="スマホがライトになる協力サバイバル" size={58} color="#e6f5ec" font={POP} y={140} rotate={0} from="drop" />
        <Sticker at={b(7)} text="遊びに来てね!" x={0} y={300} bg="#ff6bd6" size={70} rotate={-4} />
        <EndFade />
      </Section>

      {/* 全体にかける演出 */}
      <Sequence from={b(8)} durationInFrames={b(148)}>
        <BeatBorder every={2} />
      </Sequence>
      <Progress />

      {/* ---- 音 ---- */}
      <Bgm />
      {hook.map((h, i) => (
        <Sfx key={i} at={b(i * 2)} src={h.sfx} volume={0.8} />
      ))}
      <Sfx at={b(9)} src="dodon" />
      <Sfx at={b(11)} src="impact" volume={0.8} />
      <Sfx at={b(16)} src="whoosh" />
      <Sfx at={b(18)} src="select3" />
      <Sfx at={b(24)} src="pafu" />
      <Sfx at={b(27)} src="kettei1" />
      <Sfx at={b(28)} src="pafu" />
      <Sfx at={b(29)} src="pafu" />
      <Sfx at={b(31)} src="seikai1" volume={0.6} />
      {[40, 42, 44, 46].map((n) => (
        <Sfx key={n} at={b(n)} src="kyupiin1" />
      ))}
      <Sfx at={b(50)} src="gliss" />
      <Sfx at={b(54)} src="impact" />
      <Sfx at={b(60) + 50} src="don" />
      <Sfx at={b(60) + 52} src="impact" volume={0.7} />
      <Sfx at={b(72)} src="gliss" volume={0.6} />
      <Sfx at={b(78)} src="kyupiin2" />
      <Sfx at={b(84)} src="scratch" />
      <Sfx at={b(85)} src="impact" volume={0.7} />
      <Sfx at={b(95)} src="don" />
      <Sfx at={b(95)} src="impact" />
      <Sfx at={b(104)} src="whoosh" />
      <Sfx at={b(113)} src="seikai1" volume={0.7} />
      {[124, 128, 132, 136].map((n) => (
        <Sfx key={n} at={b(n)} src={n % 8 === 4 ? 'select3' : 'kettei1'} />
      ))}
      <Sfx at={b(141)} src="dodon" />
      <Sfx at={b(148)} src="scratch" />
      <Sfx at={b(156)} src="jajan" />
      <Sfx at={b(156) + 15} src="kyupiin1" />
    </AbsoluteFill>
  );
};

// ---- 場面専用の小さな演出 ----
const Bgm: React.FC = () => (
  <Audio
    src={staticFile('audio/bgm_dnb1.mp3')}
    volume={(f) => interpolate(f, [0, 6, PROMO_FRAMES - 45, PROMO_FRAMES], [0, 0.5, 0.5, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'})}
  />
);

// 擬態: 画面の色がぐるぐる変わる
const HueSweep: React.FC = () => {
  const f = useCurrentFrame();
  return <AbsoluteFill style={{backdropFilter: `hue-rotate(${f * 12}deg) saturate(1.6)`}} />;
};

const QuestionMarks: React.FC<{at: number}> = ({at}) => {
  const f = useCurrentFrame();
  if (f < at) return null;
  return (
    <AbsoluteFill>
      {[
        [-620, -150],
        [560, -220],
        [-480, 260],
        [640, 200],
        [0, -420],
      ].map(([x, y], i) => (
        <div
          key={i}
          style={{
            position: 'absolute',
            left: 960 + x,
            top: 540 + y + Math.sin((f + i * 9) / 5) * 20,
            fontFamily: HEAVY,
            fontSize: 140,
            color: '#ffe066',
            WebkitTextStroke: '10px #111',
            paintOrder: 'stroke fill',
            transform: `rotate(${Math.sin((f + i * 4) / 6) * 20}deg) scale(${Math.min(1, (f - at - i * 2) / 5)})`,
          }}
        >
          ？
        </div>
      ))}
    </AbsoluteFill>
  );
};

const RedPulse: React.FC = () => {
  const f = useCurrentFrame();
  const ph = (f % BEAT) / 4;
  return <AbsoluteFill style={{background: `rgba(255,0,40,${0.08 + 0.3 * Math.exp(-ph)})`}} />;
};

const EndFade: React.FC = () => {
  const f = useCurrentFrame();
  const end = b(16);
  return <AbsoluteFill style={{background: '#000', opacity: interpolate(f, [end - 20, end], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'})}} />;
};
