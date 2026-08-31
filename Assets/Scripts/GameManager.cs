using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Playing, Success, Fail }
    public GameState State { get; private set; } = GameState.Playing;

    GUIStyle bigStyle;
    GUIStyle smallStyle;

    Texture2D qrTexture;
    string qrTextureUrl;

    void Awake()
    {
        Instance = this;
    }

    public void Fail()
    {
        if (State != GameState.Playing) return;
        State = GameState.Fail;
    }

    public void Success()
    {
        if (State != GameState.Playing) return;
        State = GameState.Success;
    }

    void Update()
    {
        if (State != GameState.Playing && Input.GetKeyDown(KeyCode.R))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }

    void EnsureStyles()
    {
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label);
            bigStyle.fontSize = 36;
            bigStyle.alignment = TextAnchor.MiddleCenter;
            bigStyle.normal.textColor = Color.white;
        }
        if (smallStyle == null)
        {
            smallStyle = new GUIStyle(GUI.skin.label);
            smallStyle.fontSize = 16;
            smallStyle.normal.textColor = Color.white;
        }
    }

    void OnGUI()
    {
        EnsureStyles();

        string httpUrl = PointerServer.Instance != null ? PointerServer.Instance.ServerUrl : "起動中...";
        string httpsUrl = PointerServer.Instance != null ? PointerServer.Instance.HttpsServerUrl : "起動中...";

        GUI.color = new Color(0, 0, 0, 0.75f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, 82), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 6, Screen.width - 200, 24), "傾き操作(推奨・QRはこちら): " + httpsUrl, smallStyle);
        GUI.Label(new Rect(10, 28, Screen.width - 200, 24), "タッチのみで確実に繋ぎたい場合: " + httpUrl, smallStyle);
        GUI.Label(new Rect(10, 54, Screen.width - 200, 24), "WASD/矢印キー: 移動　止まると擬態します", smallStyle);

        bool httpsReady = httpsUrl.StartsWith("https://");
        if (httpsReady)
        {
            try
            {
                EnsureQrTexture(httpsUrl);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                qrTexture = null;
            }
            if (qrTexture != null)
            {
                const int qrDisplaySize = 150;
                Rect qrRect = new Rect(Screen.width - qrDisplaySize - 10, 10, qrDisplaySize, qrDisplaySize);
                GUI.DrawTexture(new Rect(qrRect.x - 4, qrRect.y - 4, qrRect.width + 8, qrRect.height + 8), Texture2D.whiteTexture);
                GUI.DrawTexture(qrRect, qrTexture);
            }
        }

        if (State == GameState.Fail)
        {
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, Screen.height / 2f - 60, Screen.width, 60), "捕食者に見つかった…", bigStyle);
            GUI.Label(new Rect(0, Screen.height / 2f, Screen.width, 30), "R キーでやり直す", smallStyle2());
        }
        else if (State == GameState.Success)
        {
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, Screen.height / 2f - 60, Screen.width, 60), "ジャングル脱出成功！", bigStyle);
            GUI.Label(new Rect(0, Screen.height / 2f, Screen.width, 30), "R キーでやり直す", smallStyle2());
        }
    }

    void EnsureQrTexture(string url)
    {
        if (qrTexture != null && qrTextureUrl == url) return;
        qrTextureUrl = url;

        bool[,] modules = QRCodeGenerator.Encode(url, out int size);
        const int moduleScale = 4;
        const int border = 4;
        int texSize = (size + border * 2) * moduleScale;

        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color32[texSize * texSize];
        Color32 white = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = white;

        Color32 black = new Color32(0, 0, 0, 255);
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (!modules[r, c]) continue;
                int px0 = (c + border) * moduleScale;
                // Texture rows are bottom-to-top; QR row 0 is the top row.
                int py0 = (size - 1 - r + border) * moduleScale;
                for (int dy = 0; dy < moduleScale; dy++)
                {
                    int rowStart = (py0 + dy) * texSize + px0;
                    for (int dx = 0; dx < moduleScale; dx++)
                        pixels[rowStart + dx] = black;
                }
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        if (qrTexture != null) Destroy(qrTexture);
        qrTexture = tex;
    }

    GUIStyle smallStyle2()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize = 18;
        s.alignment = TextAnchor.MiddleCenter;
        s.normal.textColor = Color.white;
        return s;
    }
}
