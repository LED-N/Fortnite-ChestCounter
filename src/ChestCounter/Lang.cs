using System.Globalization;

namespace ChestCounter;

/// <summary>
/// User interface language (French, English, Spanish or Japanese), chosen on first launch and saved in config.json.
/// Texts are written inline as <c>L.T("français", "English", "español", "日本語")</c>. Names stored in data.json (chest kinds,
/// treasure items) are kept in French for compatibility and translated when displayed (<see cref="Name"/>).
/// </summary>
public static class L
{
    /// <summary>Languages offered, in menu order.</summary>
    public static readonly string[] Codes = { "fr", "en", "es", "ja" };

    /// <summary>"fr", "en", "es" or "ja".</summary>
    public static string Code { get; private set; } = "fr";

    public static bool En => Code == "en";
    public static bool Es => Code == "es";
    public static bool Ja => Code == "ja";

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("fr-FR");

    public static void Set(string? code)
    {
        Code = Normalize(code) ?? "fr";
        Culture = CultureInfo.GetCultureInfo(T("fr-FR", "en-GB", "es-ES", "ja-JP"));
    }

    /// <summary>"fr", "en", "es", "ja" or null when the answer is not recognized.</summary>
    public static string? Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "fr" or "fra" or "fre" or "french" or "français" or "francais" or "francés" or "frances" => "fr",
        "en" or "eng" or "english" or "anglais" or "inglés" or "ingles" => "en",
        "es" or "spa" or "spanish" or "español" or "espanol" or "espagnol" => "es",
        "ja" or "jpn" or "japanese" or "日本語" or "ja-jp" => "ja",
        _ => null,
    };

    /// <summary>Name of a language in that language (menus).</summary>
    public static string NativeName(string code) => code switch { "en" => "English", "es" => "Español", "ja" => "日本語", _ => "Français" };

    /// <summary>Language of Windows when it is one of ours, English otherwise.</summary>
    public static string SystemDefault => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? "en";

    public static string T(string fr, string en, string es, string? ja = null) => Code switch { "en" => en, "es" => es, "ja" => ja ?? en, _ => fr };

    public static string N(int n) => n.ToString("N0", Culture);

    /// <summary>"3 coffres" / "3 chests" / "3 cofres" (regular plurals: + "s").</summary>
    public static string Count(int n, string fr, string en, string es, string? ja = null) => Count(n, fr, fr + "s", en, en + "s", es, es + "s", ja);

    /// <summary>French: singular for 0 and 1. English and Spanish: singular for 1 only.</summary>
    public static string Count(int n, string frOne, string frMany, string enOne, string enMany, string esOne, string esMany, string? ja = null) => Code switch
    {
        "en" => $"{N(n)} {(n == 1 ? enOne : enMany)}",
        "es" => $"{N(n)} {(n == 1 ? esOne : esMany)}",
        "ja" => $"{N(n)} {ja ?? (n == 1 ? enOne : enMany)}",
        _ => $"{N(n)} {(n > 1 ? frMany : frOne)}",
    };

    /// <summary>"12e" / "12th" / "12.º".</summary>
    public static string Ordinal(int n)
    {
        if (Ja) return $"{n}位";
        if (Es)
        {
            return $"{n}.º";
        }
        if (!En)
        {
            return n == 1 ? "1er" : $"{n}e";
        }
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }

    /// <summary>Short date + time of a match ("05/11 22:54" / "5 Nov 22:54").</summary>
    public static string DateTimeShort(DateTime d) => d.ToString(Ja ? "M月d日 HH:mm" : En ? "d MMM HH:mm" : "dd/MM HH:mm", Culture);

    // French names stored in data.json -> English / Spanish / Japanese display names.
    private static readonly Dictionary<string, (string En, string Es, string Ja)> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Coffre"] = ("Chest", "Cofre", "宝箱"),
        ["Coffre rare"] = ("Rare chest", "Cofre raro", "レア宝箱"),
        ["Coffre enterré"] = ("Buried chest", "Cofre enterrado", "埋められた宝箱"),
        ["Coffre d'extraction / reliques"] = ("Extraction / relic chest", "Cofre de extracción / reliquias", "採掘・レリックの宝箱"),
        ["Coffre des affiches d'esprit"] = ("Sprite poster chest", "Cofre de los carteles de espíritu", "精霊ポスターの宝箱"),
        ["Autre coffre"] = ("Other chest", "Otro cofre", "その他の宝箱"),
        ["Canon à butin"] = ("Loot cannon", "Cañón de botín", "戦利品キャノン"),
        ["Caisse de munitions"] = ("Ammo box", "Caja de munición", "弾薬箱"),
        ["Caisse de nourriture"] = ("Food box", "Caja de comida", "食料箱"),
        ["Autre conteneur"] = ("Other container", "Otro contenedor", "その他のコンテナ"),
        ["Trésor"] = ("Treasure", "Tesoro", "お宝"),
        ["Trésor (type inconnu)"] = ("Treasure gizmo", "Artilugio de tesoro", "お宝アイテム（種類不明）"),
        ["Gizmo de trésor"] = ("Treasure gizmo", "Artilugio de tesoro", "お宝アイテム（種類不明）"),
        ["Feu d'artifice doré"] = ("Golden firework", "Fuego artificial dorado", "金色の花火"),
        ["Extracteur portable"] = ("Portable extractor", "Extractor portátil", "携帯型エクストラクター"),
        ["Localisateur d'esprit"] = ("Sprite locator", "Localizador de espíritus", "精霊ロケーター"),
        ["Localisateur chanceux"] = ("Lucky locator", "Localizador de la suerte", "ラッキーロケーター"),
        ["Accélérateur d'extraction"] = ("Extraction accelerator", "Acelerador de extracción", "採掘アクセラレーター"),
        ["inconnu"] = ("unknown", "desconocido", "不明"),
    };

    /// <summary>Display name of a chest kind or treasure item stored in French.</summary>
    public static string Name(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || Code == "fr" || !Names.TryGetValue(stored, out var t))
        {
            return stored ?? "";
        }
        return Ja ? t.Ja : En ? t.En : t.Es;
    }
}
