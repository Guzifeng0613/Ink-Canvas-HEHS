using System.Text.Json;

var outDir = "parity/vectors";
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--out")
    {
        outDir = args[i + 1];
    }
}

Directory.CreateDirectory(outDir);
InkColorVectors.Write(outDir);
StrokeAttributesVectors.Write(outDir);
TaperVectors.Write(outDir);
GeometryVectors.Write(outDir);
TransformVectors.Write(outDir);
SettingsVectors.Write(outDir);
Console.WriteLine($"vectors written to {outDir}");
