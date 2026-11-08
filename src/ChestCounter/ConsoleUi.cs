using System.Globalization;
using System.Text;

namespace ChestCounter;

/// <summary>
/// Small interactive console helpers: screens that replace each other, arrow-key menus and a progress bar.
/// Everything falls back to plain text when the console is redirected (no keyboard / no cursor).
/// </summary>
public static class ConsoleUi
{
    /// <summary>Where keys come from: the console, or the queue fed by the keyboard thread once it runs.</summary>
    public static Func<ConsoleKeyInfo> ReadKey { get; set; } = () => Console.ReadKey(intercept: true);

    /// <summary>True when keys can be read and the cursor moved.</summary>
    public static bool Interactive { get; } = !Console.IsInputRedirected && !Console.IsOutputRedirected;

    internal static int Width
    {
        get
        {
            try { return Console.WindowWidth > 0 ? Console.WindowWidth : 80; } catch { return 80; }
        }
    }

    /// <summary>Clears the window and draws the title bar.</summary>
    public static void Screen(string title, string? step = null)
    {
        if (Interactive)
        {
            try { Console.Clear(); } catch { /* not a real console */ }
        }
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        var stepText = step == null ? "" : ConsoleColumns.Truncate($"   {step}", Math.Max(0, Width - 3));
        Console.Write(ConsoleColumns.Truncate($"  {title}", Math.Max(0, Width - 1 - ConsoleColumns.Width(stepText))));
        Console.ResetColor();
        if (step != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(stepText);
            Console.ResetColor();
        }
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('─', Math.Clamp(Width - 4, 0, 72)));
        Console.ResetColor();
        Console.WriteLine();
    }

    /// <summary>Window title (ignored when there is no console window, e.g. output redirected).</summary>
    public static void SetTitle(string title)
    {
        try { Console.Title = title; } catch { }
    }

    public static void Text(string text, ConsoleColor? color = null)
    {
        if (color is { } c) Console.ForegroundColor = c;
        foreach (var line in text.Split('\n'))
        {
            Console.WriteLine("  " + line);
        }
        if (color != null) Console.ResetColor();
    }

    public static void Hint(string text) => Text(text, ConsoleColor.DarkGray);

    internal static string FormatMenuLine(string option, bool selected, int width)
    {
        var room = Math.Max(0, width - 1);
        var line = ConsoleColumns.Truncate($"  {(selected ? "›" : " ")} {option}", room);
        return line + new string(' ', room - ConsoleColumns.Width(line));
    }

    /// <summary>
    /// Arrow-key menu (↑ ↓ then Enter; digits also work). Returns the chosen index.
    /// Without a keyboard, returns <paramref name="selected"/>.
    /// </summary>
    public static int Menu(IReadOnlyList<string> options, int selected = 0, string? help = null)
    {
        if (!Interactive)
        {
            return selected;
        }

        Console.WriteLine();
        var top = Console.CursorTop;
        var cursorVisible = true;
        try { cursorVisible = Console.CursorVisible; Console.CursorVisible = false; } catch { }
        try
        {
            while (true)
            {
                Console.SetCursorPosition(0, top);
                for (var i = 0; i < options.Count; i++)
                {
                    var line = FormatMenuLine(options[i], i == selected, Width);
                    if (i == selected)
                    {
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.BackgroundColor = ConsoleColor.Yellow;
                    }
                    Console.Write(line);
                    Console.ResetColor();
                    Console.WriteLine();
                }
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(ConsoleColumns.Truncate("  " + (help ?? L.T("↑ ↓ pour choisir, Entrée pour valider", "↑ ↓ to choose, Enter to confirm", "↑ ↓ para elegir, Intro para confirmar", "↑ ↓で選択、Enterで決定")), Math.Max(0, Width - 1)));
                Console.ResetColor();

                var key = ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow: selected = (selected + options.Count - 1) % options.Count; break;
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.Tab: selected = (selected + 1) % options.Count; break;
                    case ConsoleKey.Home: selected = 0; break;
                    case ConsoleKey.End: selected = options.Count - 1; break;
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        Console.WriteLine();
                        return selected;
                    default:
                        if (key.KeyChar is >= '1' and <= '9' && key.KeyChar - '1' < options.Count)
                        {
                            selected = key.KeyChar - '1';
                        }
                        break;
                }
            }
        }
        finally
        {
            try { Console.CursorVisible = cursorVisible; } catch { }
        }
    }

    /// <summary>Free text answer (Enter alone keeps <paramref name="defaultValue"/>).</summary>
    public static string Input(string prompt, string defaultValue = "")
    {
        if (!Interactive)
        {
            return defaultValue;
        }
        Console.Write($"  {prompt}");
        var answer = (Console.ReadLine() ?? "").Trim().Trim('"');
        return answer.Length == 0 ? defaultValue : answer;
    }

    public static void WaitEnter(string text)
    {
        if (!Interactive)
        {
            return;
        }
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"  {text}");
        Console.ResetColor();
        while (ReadKey().Key != ConsoleKey.Enter) { }
        Console.WriteLine();
    }
}

/// <summary>One line progress bar, redrawn in place: "  Analyse  [██████░░░░]  12 / 31  file".</summary>
public sealed class ProgressBar
{
    private readonly string _label;
    private readonly int _total;
    private int _lastLength;

    public ProgressBar(string label, int total)
    {
        _label = label;
        _total = Math.Max(1, total);
    }

    public void Update(int done, string? detail = null)
    {
        if (!ConsoleUi.Interactive)
        {
            return; // no redraw possible: the summary line at the end is enough
        }

        var width = Math.Max(0, ConsoleUi.Width - 1);
        var (prefix, bar, suffix) = FormatParts(_label, done, _total, detail, width);
        Console.Write("\r");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(prefix);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(bar);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(suffix);
        Console.ResetColor();
        var columns = ConsoleColumns.Width(prefix + bar + suffix);
        if (columns < _lastLength)
        {
            Console.Write(new string(' ', Math.Min(width, _lastLength) - columns));
        }
        _lastLength = columns;
    }

    internal static (string Prefix, string Bar, string Suffix) FormatParts(string label, int done, int total, string? detail, int width)
    {
        const int barWidth = 28;
        var filled = (int)Math.Round(barWidth * Math.Clamp(done, 0, total) / (double)total);
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        var count = $"{done} / {total}";
        var prefix = ConsoleColumns.Truncate($"  {label}  ", Math.Max(0, width - barWidth - count.Length - 2));
        var text = prefix + bar + $"  {count}";
        if (!string.IsNullOrEmpty(detail))
        {
            var room = width - ConsoleColumns.Width(text) - 2;
            if (room > 8)
            {
                text += "  " + ConsoleColumns.Truncate(detail, room, true);
            }
        }
        text = ConsoleColumns.Truncate(text, width);
        // Extremely narrow windows have no room for the full bar; the clipped line still stays on one row.
        if (ConsoleColumns.Width(prefix + bar + $"  {count}") > width)
        {
            prefix = "";
            bar = "";
        }
        return (prefix, bar, text[(prefix.Length + bar.Length)..]);
    }

    /// <summary>Erases the bar so the summary can be written on the same line.</summary>
    public void Clear()
    {
        if (!ConsoleUi.Interactive || _lastLength == 0)
        {
            return;
        }
        Console.Write("\r" + new string(' ', Math.Min(_lastLength, Math.Max(0, ConsoleUi.Width - 1))) + "\r");
        _lastLength = 0;
    }
}

/// <summary>Terminal columns: wide CJK and emoji use two cells; combining sequences stay intact.</summary>
internal static class ConsoleColumns
{
    public static int Width(string text) => Elements(text).Sum(element => ElementWidth(element));

    public static string Truncate(string text, int columns, bool tail = false)
    {
        if (columns <= 0) return "";
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        var elements = Elements(text).ToList();
        if (elements.Sum(ElementWidth) <= columns) return text;
        var selected = new List<string>();
        var used = 1; // ellipsis
        foreach (var element in tail ? elements.AsEnumerable().Reverse() : elements)
        {
            var width = ElementWidth(element);
            if (used + width > columns) break;
            selected.Add(element);
            used += width;
        }
        if (tail) selected.Reverse();
        return tail ? "…" + string.Concat(selected) : string.Concat(selected) + "…";
    }

    private static IEnumerable<string> Elements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }

    private static int ElementWidth(string element)
    {
        var width = 0;
        var emojiPresentation = false;
        foreach (var rune in element.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is 0xFE0F or 0x20E3) emojiPresentation = true;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.Control) continue;
            var wide = value is >= 0x1100 and <= 0x115F or 0x2329 or 0x232A
                or >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3
                or >= 0xF900 and <= 0xFAFF or >= 0xFE10 and <= 0xFE19
                or >= 0xFE30 and <= 0xFE6F or >= 0xFF01 and <= 0xFF60
                or >= 0xFFE0 and <= 0xFFE6 or >= 0x1B000 and <= 0x1B2FF
                or >= 0x1F1E6 and <= 0x1FAFF or >= 0x20000 and <= 0x3FFFD;
            width = Math.Max(width, wide ? 2 : 1);
        }
        return emojiPresentation && width > 0 ? 2 : width;
    }
}
