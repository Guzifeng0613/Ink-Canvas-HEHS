namespace InkCanvas.Core.Versioning;

/// <summary>Dotted application version, tolerant of the tag prefixes used in release names.</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, int Build) : IComparable<AppVersion>
{
    /// <summary>
    /// Accepts <c>3.0.0</c>, <c>v3.1.2</c>, <c>V26.10.7</c>, <c>1.2.3.4</c> and surrounding
    /// whitespace. Requires at least two numeric components so a bare year or a word like
    /// "latest" is not mistaken for a version.
    /// </summary>
    public static bool TryParse(string text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span.Substring(1);

        var parts = span.Split('.');
        if (parts.Length is < 2 or > 4) return false;

        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int n) || n < 0) return false;
            numbers[i] = n;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        return c != 0 ? c : Build.CompareTo(other.Build);
    }

    public override string ToString() => Build == 0
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}.{Build}";

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;
}