// ========================================
// カメレオンナイト ESP32コントローラー(USBシリアル版、Wi-Fi不要)
//
// 前・後・左・右の圧力マット(またはスイッチ)の状態を、USBケーブルのシリアル通信でPCへ送る。
// Unity側は PressureMatServer.cs がCOMポートを自動で探して受け取る。
//
// 送り方(オーキャンのLED制御と同じ考え方):
//   - 4方向の状態を1バイトにまとめて送る
//   - 状態が変わった瞬間にすぐ送る(反応を速く、通信量は少なく)
//   - 変化がなくても HEARTBEAT_MS ごとに同じ値を送り直す
//     (Unityは0.5秒届かないと「入力なし」、2秒で「未接続」と判断するため)
//
// 1バイトの中身:
//   上位4ビット = 1010 (0xA0、ESP32からのデータだという目印)
//   bit0 = 前, bit1 = 後, bit2 = 左, bit3 = 右
//   例: 前だけ → 0xA1、前+右 → 0xA9、何もなし → 0xA0
//
// 動作確認したいときは、下の OUTPUT_TEXT を true にして書き込むと、
// シリアルモニタで読める "前,後,左,右" の文字形式(例: 1,0,0,1)で送る。Unityはどちらも受け付ける。
//
// 書き込み:
//   ツール → ボード → esp32 → 「ESP32 Dev Module」
//   (ボード一覧にesp32が無い場合は、ボードマネージャで「esp32 by Espressif Systems」を入れる)
//
// 書き込み後はシリアルモニタを閉じること。開いたままだとUnityがCOMポートを使えない。
// ========================================

// true にすると、シリアルモニタで読める文字形式で送る(動作確認用)
const bool OUTPUT_TEXT = false;

// ========================================
// 圧力マットのGPIO [前, 後, 左, 右]
//
// マットの片側をこのピン、もう片側をGNDにつなぐ。
// 踏むとGNDにつながるので INPUT_PULLUP を使う(LOW = 踏まれている)。
// ========================================
const int PINS[4] = {
  26, // 前
  25, // 後
  32, // 左
  27, // 右
};

// ========================================
// 送信設定(Unity側の baudRate と合わせる)
// ========================================
const unsigned long BAUD_RATE = 115200;
const unsigned long HEARTBEAT_MS = 200; // 変化がなくても送り直す間隔
const unsigned long DEBOUNCE_MS = 15;   // 踏み始め/離した瞬間のON/OFFの揺れを無視する時間
const uint8_t MARKER = 0xA0;

// ========================================
// チャタリング除去
//
// 読み取った値が DEBOUNCE_MS の間ずっと同じだった時だけ、状態として確定する。
// ========================================
bool rawState[4];
bool stableState[4];
unsigned long rawChangedAt[4];

uint8_t lastSentBits = 0xFF; // 起動直後は必ず1回送るよう、ありえない値にしておく
unsigned long lastSendTime = 0;


bool readPressed(int i)
{
  return digitalRead(PINS[i]) == LOW;
}

uint8_t currentBits()
{
  uint8_t bits = 0;
  for (int i = 0; i < 4; i++)
  {
    if (stableState[i]) bits |= (1 << i);
  }
  return bits;
}

void sendBits(uint8_t bits)
{
  if (OUTPUT_TEXT)
  {
    for (int i = 0; i < 4; i++)
    {
      if (i > 0) Serial.print(',');
      Serial.print((bits >> i) & 1);
    }
    Serial.println();
  }
  else
  {
    Serial.write(MARKER | bits);
  }
  lastSentBits = bits;
  lastSendTime = millis();
}


// ========================================
// 初期設定
// ========================================
void setup()
{
  Serial.begin(BAUD_RATE);

  for (int i = 0; i < 4; i++)
  {
    pinMode(PINS[i], INPUT_PULLUP);
    rawState[i] = readPressed(i);
    stableState[i] = rawState[i];
    rawChangedAt[i] = millis();
  }
}


// ========================================
// メインループ
// ========================================
void loop()
{
  unsigned long now = millis();

  for (int i = 0; i < 4; i++)
  {
    bool pressed = readPressed(i);
    if (pressed != rawState[i])
    {
      rawState[i] = pressed;
      rawChangedAt[i] = now;
    }
    else if (pressed != stableState[i] && now - rawChangedAt[i] >= DEBOUNCE_MS)
    {
      stableState[i] = pressed;
    }
  }

  uint8_t bits = currentBits();
  if (bits != lastSentBits || now - lastSendTime >= HEARTBEAT_MS)
  {
    sendBits(bits);
  }
}
