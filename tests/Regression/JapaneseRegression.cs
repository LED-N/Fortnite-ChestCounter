using ChestCounter;

public static class JapaneseRegression
{
    public static void Run()
    {
        var previous = L.Code;
        try
        {
            VerifyStoredDefaultPrizeTranslations();
            foreach (var alias in new[] { "ja", "jpn", "japanese", "日本語", "ja-JP" })
                Equal("ja", L.Normalize(alias), "Japanese language alias");
            L.Set("ja");
            Equal("ja", L.Code, "Japanese language persists");
            Equal("ja-JP", L.Culture.Name, "Japanese culture");
            Equal("日本語", L.NativeName("ja"), "Japanese menu name");
            Equal("日本語", L.T("fr", "en", "es", "日本語"), "Japanese translation");
            Equal("en", L.T("fr", "en", "es"), "Missing translation falls back to English");
            Equal("3 宝箱", L.Count(3, "coffre", "chest", "cofre", "宝箱"), "Japanese count");
            Equal("3 試合", L.Count(3, "partie", "parties", "match", "matches", "partida", "partidas", "試合"), "Japanese irregular count");
            Equal("12位", L.Ordinal(12), "Japanese placement");
            Equal("11月8日 09:05", L.DateTimeShort(new DateTime(2026, 11, 8, 9, 5, 0)), "Japanese short date");
            Equal("宝箱", L.Name("Coffre"), "Persisted French chest name is translated");
            Equal("Coffre", ChestKinds.Normal, "Persisted chest kind is unchanged");
            Equal("custom name", L.Name("custom name"), "Custom names remain intact");
            Equal("金色の花火", L.Name("Feu d'artifice doré"), "Persisted treasure name is translated");
            Equal("お宝アイテム（種類不明）", TreasureNames.Friendly("Unknown:JapaneseRegression"), "Unknown treasure is translated");
            TreasureNames.PrizeNames["JapaneseRegression:custom"] = "My custom prize";
            Equal("My custom prize", TreasureNames.Friendly("JapaneseRegression:custom"), "Custom prize name remains intact");
            TreasureNames.PrizeNames.Remove("JapaneseRegression:custom");
            var view = OverlayModel.Build(new TrackerData(), null, false);
            Equal("前の試合", view.CurrentCaption, "Japanese overlay caption");
            if (!ReportWriter.Overlay(view).Contains("合計"))
                throw new Exception("Japanese overlay total caption missing");
            if (!ReportWriter.Csv(new TrackerData()).StartsWith("日時;モード;プレイヤー;"))
                throw new Exception("Japanese CSV headings missing");
            var html = ReportWriter.Report(new Config(), new TrackerData(), Array.Empty<Profile>());
            if (!html.Contains("lang=\"ja\"") || !html.Contains("宝箱カウンター"))
                throw new Exception("Japanese report language and title missing");
            var folder = Path.Combine(Path.GetTempPath(), "JapaneseRegression-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Store(folder);
                store.SaveConfig(new Config { Language = L.Code });
                Equal("ja", store.LoadConfig()?.Language, "Japanese config round trip");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            L.Set("fr");
            Equal("2 coffres", L.Count(2, "coffre", "chest", "cofre"), "Existing French plural");
            L.Set("en");
            Equal("21st", L.Ordinal(21), "Existing English ordinal");
            L.Set("es");
            Equal("12.º", L.Ordinal(12), "Existing Spanish ordinal");
        }
        finally { L.Set(previous); }
    }

    private static void Equal(string? expected, string? actual, string scenario)
    {
        if (expected != actual) throw new Exception($"{scenario}: expected {expected}, got {actual}");
    }

    private static void VerifyStoredDefaultPrizeTranslations()
    {
        var previous = L.Code;
        var prizes = TreasureNames.PrizeNames.ToDictionary(kv => kv.Key, kv => kv.Value);
        try
        {
            foreach (var storedLanguage in L.Codes)
            {
                L.Set(storedLanguage);
                var storedDefaults = Store.DefaultPrizeNames;
                foreach (var displayLanguage in L.Codes)
                {
                    L.Set(displayLanguage);
                    var expectedDefaults = Store.DefaultPrizeNames;
                    foreach (var (id, storedName) in storedDefaults)
                    {
                        TreasureNames.PrizeNames[id] = storedName;
                        Equal(expectedDefaults[id], TreasureNames.Friendly(id), $"Stored {storedLanguage} default displayed in {displayLanguage}");
                        Equal(storedName, TreasureNames.PrizeNames[id], "Display resolution leaves stored names intact");
                        TreasureNames.PrizeNames[id] = "My custom prize";
                        Equal("My custom prize", TreasureNames.Friendly(id), "Edited known prize stays intact");
                    }
                }
            }
        }
        finally
        {
            TreasureNames.PrizeNames.Clear();
            foreach (var (id, name) in prizes) TreasureNames.PrizeNames[id] = name;
            L.Set(previous);
        }
    }
}
