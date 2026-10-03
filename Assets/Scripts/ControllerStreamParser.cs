using System;
using System.Text;

// ESP32コントローラーから届くデータを、前・後・左・右のビットに解読する(UnityEngineに依存しない)。
// 2つの形式が混ざって届いてもよい:
//   - 1バイト形式(esp32/controller/controller.ino): 0xA0 | ビット。上位4ビット 1010 が目印、
//       bit0 = 前, bit1 = 後, bit2 = 左, bit3 = 右。例: 0xA9 = 前+右
//   - 文字形式(Wi-Fi版 esp32/pressure_mat など): "前,後,左,右" の 0/1 を改行区切り。例: "1,0,0,1\n"
// 文字形式のバイト('0' '1' ',' 改行)は上位4ビットが 1010 にならないので、両者を取り違えない。
public sealed class ControllerStreamParser
{
    public const byte Marker = 0xA0;
    public const int Front = 1, Back = 2, Left = 4, Right = 8;

    const int MaxLineLength = 32;
    readonly StringBuilder line = new StringBuilder();

    // 受信したバイト列を流し込み、読み取れた入力ごとに onFrame(ビット) を呼ぶ。
    // 途中で切れた文字行は内部に持ち越すので、細切れに届いても正しく解読できる。
    public void Feed(byte[] data, int count, Action<int> onFrame)
    {
        for (int i = 0; i < count; i++)
        {
            byte b = data[i];
            if ((b & 0xF0) == Marker)
            {
                line.Length = 0;
                onFrame(b & 0x0F);
            }
            else if (b == (byte)'\n')
            {
                if (TryParseLine(line.ToString(), out int bits)) onFrame(bits);
                line.Length = 0;
            }
            else if (b != (byte)'\r')
            {
                if (line.Length < MaxLineLength) line.Append((char)b);
                else line.Length = 0; // 改行の来ない長い行(起動時のゴミなど)は捨てる
            }
        }
    }

    // "前,後,左,右" の1行(例 "1,0,0,1")をビットにする。形式が違えば false。
    public static bool TryParseLine(string text, out int bits)
    {
        bits = 0;
        string[] parts = text.Trim().Split(',');
        if (parts.Length != 4) return false;
        for (int i = 0; i < 4; i++)
        {
            string p = parts[i].Trim();
            if (p == "1") bits |= 1 << i;
            else if (p != "0") return false;
        }
        return true;
    }
}
