namespace TimeInset.Core;

/// <summary>
/// 確認ダイアログを同時に1つだけにし、終了処理が始まった後の要求を断る。
/// 1.0.1 では確認が入れ子で重なり、終了後に残った呼び出しが例外になった（仕様書§16）。
/// </summary>
public sealed class ConfirmGate
{
    public bool IsOpen { get; private set; }
    public bool Exiting { get; private set; }

    /// <summary>開いてよければ true。開いている間は前面に出す処理を呼んで false、終了開始後は何もせず false。</summary>
    public bool TryOpen(Action bringToFront)
    {
        if (Exiting) return false;
        if (IsOpen) { bringToFront(); return false; }
        IsOpen = true;
        return true;
    }

    public void Close() => IsOpen = false;

    /// <summary>終了処理を始める。初回だけ true。</summary>
    public bool BeginExit()
    {
        if (Exiting) return false;
        Exiting = true;
        return true;
    }
}
