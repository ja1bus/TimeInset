using System.Globalization;
using System.Text.RegularExpressions;
using TimeInset.Core.Localization;
namespace TimeInset.Tests;
// TimeInset is Japanese only (2026-11-03). The neutral resources hold the Japanese text.
public class LocalizationTests
{
    [Fact] public void ResourcesAreNonEmptyJapaneseWithValidFormatArguments()
    {
        var set = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        var all = set.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        Assert.NotEmpty(all);
        foreach (var (key, value) in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            foreach (Match m in Regex.Matches(value, @"\{(\d+)\}")) Assert.InRange(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0, 1);
        }
        Assert.Contains(all["Settings"], c => c >= 0x3000 && c <= 0x9FFF);
        Assert.Contains("{0}", Strings.HotkeyInUseFormat); Assert.Contains("{1}", Strings.HotkeyErrorFormat);
    }
    [Fact] public void EnglishUiCultureStillGetsJapanese()
    {
        var before = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US"); Assert.Equal("設定", Strings.Settings); }
        finally { CultureInfo.CurrentUICulture = before; }
    }
}
