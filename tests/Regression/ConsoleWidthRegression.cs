using System.Reflection;
using ChestCounter;

public static class ConsoleWidthRegression
{
    public static void Run()
    {
        var columns = typeof(ConsoleUi).Assembly.GetType("ChestCounter.ConsoleColumns")
            ?? throw new Exception("Console display-column helpers are missing");
        object Invoke(string name, params object[] args) => columns.GetMethod(name)!.Invoke(null, args)!;
        int Width(string text) => (int)Invoke("Width", text);
        string Truncate(string text, int limit, bool tail = false) => (string)Invoke("Truncate", text, limit, tail);
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

        Assert(Width("abc") == 3, "ASCII width changed");
        Assert(Width("日本語かなカナＡ") == 16, "Japanese and fullwidth characters need two columns");
        Assert(Width("e\u0301") == 1 && Width("\u0301") == 0, "Combining marks must not add columns");
        Assert(Width("😀") == 2 && Width("👩‍💻") == 2 && Width("🇯🇵") == 2,
            "Emoji, surrogate pairs and joined emoji must occupy one two-column cluster");
        Assert(Width("✈️") == 2 && Width("1️⃣") == 2, "Emoji presentation and keycaps need two columns");
        Assert(Truncate("あいう", 5) == "あい…", "Japanese prefix truncation splits columns");
        Assert(Truncate("あいう", 5, true) == "…いう", "Japanese filename suffix truncation failed");
        Assert(Truncate("😀xyz", 2) == "…", "Truncation must not split a surrogate pair");
        Assert(Truncate("e\u0301xyz", 2) == "e\u0301…", "Truncation must retain a combining sequence");
        Assert(Truncate("abc", 0) == "", "Zero available columns must produce no text");

        var menuLine = typeof(ConsoleUi).GetMethod("FormatMenuLine", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("Menu rendering must use column-aware formatting");
        foreach (var option in new[] { "日本語の長いメニュー項目", "短い", "👩‍💻 設定 e\u0301", "ASCII option" })
        foreach (var windowWidth in new[] { 12, 40, 80 })
        {
            var line = (string)menuLine.Invoke(null, new object[] { option, true, windowWidth })!;
            Assert(Width(line) == windowWidth - 1, "Menu padding must leave the final terminal column unused");
        }

        var progressParts = typeof(ProgressBar).GetMethod("FormatParts", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("Progress rendering must use column-aware formatting");
        foreach (var available in new[] { 0, 12, 39, 79, 119 })
        {
            var (prefix, bar, suffix) = ((string, string, string))progressParts.Invoke(null,
                new object[] { "リプレイを解析しています", 12, 31, "長い日本語のファイル名👩‍💻e\u0301.replay", available })!;
            Assert(Width(prefix + bar + suffix) <= available, "Progress bar or filename exceeds its terminal row");
            Assert(!(prefix + bar + suffix).Contains('\uFFFD'), "Progress clipping damaged a Unicode sequence");
        }
    }
}
