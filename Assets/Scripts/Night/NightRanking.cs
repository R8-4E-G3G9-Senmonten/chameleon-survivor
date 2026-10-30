using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// マップごとのランキング。スコア = 早さと撃破数を合わせた値。早くクリアするほど、たくさん倒すほど高い。
//   クリア時 : 5000 + 撃破数×100 + max(0, 600−クリア秒数)×10
//   力尽きた時: 撃破数×100 だけ(長く粘っても点は増えない。クリアより必ず低くなる)
//
// 記録は exe と同じフォルダの ranking.csv に保存する(Excel でそのまま開いて、名前の修正・行の削除・
// 並べ替えができる。Excel で保存し直した内容も、次にタイトル画面へ戻った時に反映される)。
// 灯り役(スマホ)は名前の入力なしで参加でき、名前を付けるのはメインのプレイヤーだけ(ゲーム開始前にPCで入力)。
public static class NightRanking
{
    public const int MaxEntries = 10;       // 画面に出す順位の数(ファイルにはすべての記録を残す)
    public const int MaxNameLength = 8;
    public const int ClearBonus = 5000, KillPoints = 100, TimeBase = 600, TimePoints = 10;
    public const string DefaultName = "ゲスト";
    const string Header = "マップ,名前,スコア,秒数,撃破数,結果,日時";
    const string ClearedText = "クリア", FailedText = "途中終了";

    public class Entry
    {
        public string map, name;
        public int score, seconds, kills;
        public bool cleared;
        public DateTime time;
    }

    static List<Entry> entries;
    static DateTime loadedStamp = DateTime.MinValue;

    // 実行ファイルと同じフォルダ(エディタではプロジェクトのフォルダ)。自動撮影テストは別ファイルを使い、本物を汚さない。
    public static string FilePath
    {
        get
        {
            bool test = Array.IndexOf(Environment.GetCommandLineArgs(), "-nightAutoShot") >= 0;
            string dir = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(dir, test ? "ranking_test.csv" : "ranking.csv");
        }
    }

    public static int ScoreOf(bool cleared, int seconds, int kills)
    {
        if (cleared) return ClearBonus + kills * KillPoints + Mathf.Max(0, TimeBase - seconds) * TimePoints;
        return kills * KillPoints;
    }

    // ファイルが変わっていたら(Excel で編集された等)読み直す
    static void Load()
    {
        string path = FilePath;
        DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (entries != null && stamp == loadedStamp) return;
        entries = new List<Entry>();
        loadedStamp = stamp;
        if (stamp == DateTime.MinValue) return;
        try
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false), true)) text = sr.ReadToEnd();
            foreach (string line in SplitLines(text))
            {
                var c = SplitCsv(line);
                if (c.Count < 6) continue;
                if (!int.TryParse(c[2].Trim(), out int score)) continue; // ヘッダー行や不正な行は読み飛ばす
                int.TryParse(c[3].Trim(), out int sec);
                int.TryParse(c[4].Trim(), out int kills);
                DateTime.TryParse(c.Count > 6 ? c[6].Trim() : "", out DateTime time);
                string map = c[0].Trim().ToUpperInvariant();
                if (Array.IndexOf(NightMap.Ids, map) < 0) continue;
                entries.Add(new Entry { map = map, name = c[1].Trim().Length == 0 ? DefaultName : c[1].Trim(), score = score, seconds = sec, kills = kills, cleared = c[5].Trim() == ClearedText, time = time });
            }
        }
        catch (Exception e) { Debug.LogWarning("NightRanking: 読み込みに失敗しました: " + e.Message); }
    }

    static void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            foreach (var m in NightMap.Ids)
                foreach (var e in entries.FindAll(x => x.map == m).OrderByScore())
                    sb.Append(e.map).Append(',').Append(Quote(e.name)).Append(',').Append(e.score).Append(',').Append(e.seconds).Append(',')
                      .Append(e.kills).Append(',').Append(e.cleared ? ClearedText : FailedText).Append(',')
                      .Append(e.time.ToString("yyyy-MM-dd HH:mm:ss")).Append("\r\n");
            // BOM 付き UTF-8 にすると、Excel で開いても日本語が文字化けしない
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            loadedStamp = File.GetLastWriteTimeUtc(FilePath);
        }
        catch (Exception e) { Debug.LogWarning("NightRanking: 保存に失敗しました(Excelで開いたままの可能性があります): " + e.Message); }
    }

    static IEnumerable<Entry> OrderByScore(this List<Entry> list)
    {
        var copy = new List<Entry>(list);
        copy.Sort(Compare);
        return copy;
    }

    // スコアが高い順。同点はクリアタイムが短い方、さらに同じなら先に登録した方が上。
    static int Compare(Entry a, Entry b)
    {
        int c = b.score.CompareTo(a.score);
        if (c != 0) return c;
        c = a.seconds.CompareTo(b.seconds);
        return c != 0 ? c : a.time.CompareTo(b.time);
    }

    public static List<Entry> Top(string map)
    {
        Load();
        var list = entries.FindAll(e => e.map == map);
        list.Sort(Compare);
        if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        return list;
    }

    // 登録する前に「このスコアなら何位か」を返す(10位以内に入れなければ 0)。結果画面で名前を入れてもらう前に見せる
    public static int RankIfAdded(string map, int score, int seconds)
    {
        var probe = new Entry { map = map, score = score, seconds = seconds, time = DateTime.Now };
        int ahead = Top(map).FindAll(e => Compare(e, probe) < 0).Count;
        return ahead < MaxEntries ? ahead + 1 : 0;
    }

    // 登録して、そのマップでの順位(1始まり。10位以内に入れなければ 0)を返す。
    public static int Add(string map, string name, int score, int seconds, int kills, bool cleared, out Entry entry)
    {
        Load();
        entry = new Entry { map = map, name = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim(), score = score, seconds = seconds, kills = kills, cleared = cleared, time = DateTime.Now };
        entries.Add(entry);
        Save();
        int idx = Top(map).IndexOf(entry);
        return idx >= 0 ? idx + 1 : 0;
    }

    // ---- CSV の読み書き(引用符・改行つきのセルにも対応。Excel が保存する形式を読める) ----
    static string Quote(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (char ch in text)
        {
            if (ch == '"') inQuote = !inQuote;
            if ((ch == '\n' || ch == '\r') && !inQuote)
            {
                if (sb.Length > 0) { lines.Add(sb.ToString()); sb.Length = 0; }
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) lines.Add(sb.ToString());
        return lines;
    }

    static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuote)
            {
                if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else inQuote = false; }
                else sb.Append(ch);
            }
            else if (ch == '"') inQuote = true;
            else if (ch == ',') { cells.Add(sb.ToString()); sb.Length = 0; }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells;
    }
}
