using System.Runtime.InteropServices;
using System.Text;

namespace TimeInset.Services;
public static class DataPaths
{
    public static string Folder { get; } = ResolveFolder();
    public static string Settings => Path.Combine(Folder, "settings.json");
    private static string ResolveFolder()
    {
        // Select the package's real cache explicitly so the UI shows the physical path.
        uint length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) == 122)
        {
            var name = new StringBuilder((int)length);
            if (GetCurrentPackageFamilyName(ref length, name) == 0)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", name.ToString(), "LocalCache", "Local", "TimeInset");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeInset");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? name);
}
public static class ErrorLog
{
    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataPaths.Folder);
            var path = Path.Combine(DataPaths.Folder, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {ex}\n");
        }
        catch (Exception) { /* Logging must never cause another failure. */ }
    }
}
