using System.Diagnostics;
namespace TimeInset.Tests;
// 本物の AppHost を別プロセスで動かす統合試験（試験表 T-07・T-38 の自動化部分、レビュー R1〜R5 の回帰確認）。
// Application は1プロセスに1つで終了後に作り直せないため、場面ごとに ExitHarness を起動する。
// 確認ダイアログ・表示・メニューが一瞬画面に出る。対話セッションで実行する。
[Collection("ExitHarness")]
public class ExitHarnessTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "TimeInset-harness-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Theory]
    [InlineData("approve-exit", false)][InlineData("approve-exit", true)]
    [InlineData("session-idle", false)][InlineData("session-idle", true)]
    [InlineData("session-exit-confirm", false)][InlineData("session-exit-confirm", true)]
    [InlineData("session-reset-confirm", false)][InlineData("session-reset-confirm", true)]
    [InlineData("modal-input", false)]
    [InlineData("modal-zorder", false)]
    [InlineData("anchor", false)]
    [InlineData("monitors", false)] // 複数モニターなら各モニターと往復。1台なら1台分
    public async Task Scenario(string scenario, bool settingsOpen)
    {
        Directory.CreateDirectory(folder);
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "TimeInset.ExitHarness.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(scenario); info.ArgumentList.Add(folder);
        if (settingsOpen) info.ArgumentList.Add("--settings");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(); Assert.Fail("時間切れ\n" + await output); }
        string stdout = await output, stderr = await error;
        Assert.True(process.ExitCode == 0, $"終了コード {process.ExitCode}\n{stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
