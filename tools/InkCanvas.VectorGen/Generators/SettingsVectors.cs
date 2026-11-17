using System.Text.Json;
using System.Text.Json.Nodes;
using InkCanvas.Core.Settings;

internal static class SettingsVectors
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public static void Write(string dir)
    {
        var inputs = new[]
        {
            "{\"advanced\":{\"isSpecialScreen\":true,\"touchMultiplier\":0.3},\"appearance\":{\"theme\":2},\"automation\":{\"autoSavedStrokesLocation\":\"D:\\\\Ink Canvas\",\"autoDelSavedFilesDaysThreshold\":30},\"behavior\":{\"isSupportWPS\":false},\"canvas\":{\"inkWidth\":7.5,\"inkStyle\":1,\"hyperbolaAsymptoteOption\":0},\"gesture\":{\"AutoSwitchTwoFingerGesture\":false,\"matrixTransformCenterPoint\":2},\"inkToShape\":{\"isInkToShapeEnabled\":false},\"startup\":{\"isAutoUpdateWithSilence\":true,\"isAutoUpdateWithSilenceStartTime\":\"22:30\"},\"randSettings\":{\"peopleCount\":42},\"futureFlag\":true}",
            "{\"canvas\":{\"INKWIDTH\":7.5},\"appearance\":{\"THEME\":1}}",
            "{\"automation\":{\"autoSavedStrokesLocation\":\"C:\\\\Users\\\\Teacher\\\\Desktop\\\\notes\"}}",
            "{}"
        };

        var cases = new List<object>();
        for (var i = 0; i < inputs.Length; i++)
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(inputs[i], CaseInsensitive)
                ?? throw new InvalidOperationException("settings deserialize failed");
            var serialized = JsonSerializer.Serialize(settings);
            var expected = JsonNode.Parse(serialized)
                ?? throw new InvalidOperationException("settings serialize failed");
            cases.Add(new
            {
                id = $"settings-{i}",
                fn = "settingsRoundtrip",
                input = new object[] { inputs[i] },
                expected,
                tolerance = 0
            });
        }

        JsonWriter.Write(dir, "settings-roundtrip.json", new
        {
            id = "settings-roundtrip",
            entry = "harmony/core/src/main/ets/persistence/SettingsCodec.ts",
            cases
        });
    }
}
