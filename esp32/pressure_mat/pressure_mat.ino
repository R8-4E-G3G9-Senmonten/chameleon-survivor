#include <WiFi.h>
#include <WiFiUdp.h>

// ========================================
// Wi-Fi設定
// ========================================
const char* ssid = "WiFiのSSID";
const char* password = "WiFiのパスワード";

// ========================================
// Unityを動かしているPC
// (PointerServerのQR画面などに表示されるこのPCのIPアドレスに合わせること。
//  Unity側は PressureMatServer.cs が同じ 5005 番ポートで待ち受ける)
// ========================================
const char* unityIP = "192.168.1.100";
const int unityPort = 5005;

WiFiUDP udp;

// ========================================
// 圧力マットのGPIO
//
// 送信順番
// [前, 後, 左, 右]
// ========================================
const int FRONT_PIN = 25;
const int BACK_PIN  = 26;
const int LEFT_PIN  = 27;
const int RIGHT_PIN = 32;

// ========================================
// UDP送信間隔
// ========================================
const unsigned long SEND_INTERVAL = 50; // 50ms

unsigned long lastSendTime = 0;


// ========================================
// Wi-Fi接続
// ========================================
void connectWiFi()
{
  Serial.println("WiFi接続開始");

  WiFi.begin(ssid, password);

  while (WiFi.status() != WL_CONNECTED)
  {
    delay(500);
    Serial.print(".");
  }

  Serial.println();
  Serial.println("WiFi接続完了");

  Serial.print("ESP32 IP: ");
  Serial.println(WiFi.localIP());
}


// ========================================
// 初期設定
// ========================================
void setup()
{
  Serial.begin(115200);

  // 圧力マット
  // マットを踏むとGNDにつながるため
  // INPUT_PULLUPを使用
  pinMode(FRONT_PIN, INPUT_PULLUP);
  pinMode(BACK_PIN, INPUT_PULLUP);
  pinMode(LEFT_PIN, INPUT_PULLUP);
  pinMode(RIGHT_PIN, INPUT_PULLUP);

  // Wi-Fi接続
  connectWiFi();
}


// ========================================
// メインループ
// ========================================
void loop()
{
  // ----------------------------------------
  // Wi-Fi切断チェック
  // ----------------------------------------
  if (WiFi.status() != WL_CONNECTED)
  {
    Serial.println("WiFiが切断されました");

    WiFi.disconnect();
    WiFi.begin(ssid, password);

    Serial.println("WiFi再接続中...");

    // 再接続できなければ今回は送信しない
    unsigned long startTime = millis();

    while (WiFi.status() != WL_CONNECTED)
    {
      delay(100);

      // 5秒経過したら一旦loopを抜ける
      if (millis() - startTime >= 5000)
      {
        Serial.println("WiFi再接続失敗");
        return;
      }
    }

    Serial.println("WiFi再接続成功");

    Serial.print("ESP32 IP: ");
    Serial.println(WiFi.localIP());
  }


  // ----------------------------------------
  // 50msごとに送信
  // ----------------------------------------
  if (millis() - lastSendTime >= SEND_INTERVAL)
  {
    lastSendTime = millis();


    // ----------------------------------------
    // 圧力マット読み取り
    //
    // INPUT_PULLUPなので
    // LOW  = 踏まれている
    // HIGH = 踏まれていない
    // ----------------------------------------
    int front = (digitalRead(FRONT_PIN) == LOW) ? 1 : 0;
    int back  = (digitalRead(BACK_PIN)  == LOW) ? 1 : 0;
    int left  = (digitalRead(LEFT_PIN)  == LOW) ? 1 : 0;
    int right = (digitalRead(RIGHT_PIN) == LOW) ? 1 : 0;


    // ----------------------------------------
    // 送信用データ作成
    //
    // [前, 後, 左, 右]
    //
    // 例：
    // 前だけ     → 1,0,0,0
    // 前＋右     → 1,0,0,1
    // 左＋後     → 0,1,1,0
    // 何もなし   → 0,0,0,0
    // ----------------------------------------
    String message =
      String(front) + "," +
      String(back)  + "," +
      String(left)  + "," +
      String(right);


    // ----------------------------------------
    // UDP送信
    // ----------------------------------------
    udp.beginPacket(unityIP, unityPort);

    udp.print(message);

    udp.endPacket();


    // シリアルモニタにも表示
    Serial.println(message);
  }
}
