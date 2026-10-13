using System.Globalization;

namespace RadioPanel.Settings;

public static class IniStore
{
    public static string DefaultPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = AppContext.BaseDirectory;
            return Path.Combine(localAppData, "RadioPanel", "settings.ini");
        }
    }

    public static PanelSettings Load(string path, PanelSettings fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return fallback;

        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] is ';' or '#' or '[')
                    continue;

                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = trimmed[..separator].Trim();
                var value = trimmed[(separator + 1)..].Trim();
                values[key] = value;
            }

            var x = values.TryGetValue("X", out var xText)
                && int.TryParse(xText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedX)
                ? parsedX
                : fallback.X;
            var y = values.TryGetValue("Y", out var yText)
                && int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedY)
                ? parsedY
                : fallback.Y;
            var alwaysOnTop = values.TryGetValue("AlwaysOnTop", out var topText)
                ? ParseBoolean(topText, fallback.AlwaysOnTop)
                : fallback.AlwaysOnTop;

            return new PanelSettings(x, y, alwaysOnTop);
        }
        catch (IOException)
        {
            return fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    public static void Save(string path, PanelSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        var temporaryPath = path + ".tmp";

        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                temporaryPath,
                $"[Window]{Environment.NewLine}X={settings.X.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}Y={settings.Y.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}AlwaysOnTop={(settings.AlwaysOnTop ? 1 : 0)}{Environment.NewLine}",
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, true);
        }
        catch (IOException)
        {
            TryDelete(temporaryPath);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool ParseBoolean(string value, bool fallback)
    {
        if (value == "1")
            return true;
        if (value == "0")
            return false;
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
