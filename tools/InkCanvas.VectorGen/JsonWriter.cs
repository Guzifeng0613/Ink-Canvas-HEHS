using System.Text.Encodings.Web;
using System.Text.Json;

internal static class JsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Write(string dir, string file, object document)
    {
        var json = JsonSerializer.Serialize(document, Options).Replace("\r\n", "\n");
        File.WriteAllText(Path.Combine(dir, file), json + "\n");
    }
}
