namespace AutomotiveGpu;

/// <summary>
/// 上位机向三维控件发送的只读状态快照。TimestampUtc 是接收状态的时间，Valid 是独立有效性标志。
/// 角度、开度和报警来自同一批状态，三维显示不会读取 PLC 的另一份数据后自行拼接。
/// </summary>
public sealed record Snapshot(DateTimeOffset TimestampUtc, string Source, bool Connected,
    bool WiperValid, bool WindowValid, double? WiperAngleDegrees, double? WindowAngleDegrees,
    double? WindowPercent, string Mode, string Alarm)
{
    public bool HasWiper => WiperValid && WiperAngleDegrees is >= -120 and <= 120;
    public bool HasWindow => WindowValid && WindowAngleDegrees is >= 0 and <= 180 && WindowPercent is >= 0 and <= 100;
}

/// <summary>
/// 三维只读状态邮箱。阅读路线：上位机通信服务 → 操作窗口推送 Snapshot → Receive → VisualPose → GlView.OnPaint。
/// 只在已收到的两帧状态间做插值，不推测下一帧；600 ms 未更新则有效性失效，防止失联后继续演动作。
/// 显示插值不影响 PLC 控制周期，也不更新 PLC 心跳。本类只接收程序内快照，不再建立另一条文件或网络数据来源。
/// </summary>
public sealed class SnapshotReader
{
    readonly List<Snapshot> history = new();
    readonly Func<DateTimeOffset> clock;
    readonly Func<long> monotonicClock;
    long? receivedTick;
    public Snapshot? Latest { get; private set; }
    public string Error { get; private set; } = "请先启动上位机并连接 PLC";
    public double AgeMs => Latest == null ? double.PositiveInfinity : receivedTick is long tick ? monotonicClock()-tick : (clock() - Latest.TimestampUtc).TotalMilliseconds;
    public bool Fresh => Latest is { Connected: true } && AgeMs is >= -100 and <= 600;
    public bool WiperValid => Fresh && Latest!.HasWiper;
    public bool WindowValid => Fresh && HasWindow(Latest!);
    public SnapshotReader(Func<DateTimeOffset>? clock = null, Func<long>? monotonicClock = null) { this.clock = clock ?? (() => DateTimeOffset.UtcNow); this.monotonicClock = monotonicClock ?? (() => Environment.TickCount64); }
    // 内嵌二维、三维用同一车窗角度换算位置；独立开度值保留自己的有效性，不能替换失效的角度。
    bool HasWindow(Snapshot sample) => sample.WindowValid && sample.WindowAngleDegrees is >=0 and <=180;
    double WindowPosition(Snapshot sample) => (sample.WindowAngleDegrees ?? 0)/1.8;

    // receivedAt 必须保留通信服务最初的接收时刻。UI 队列延迟到现在才显示，并不等于刚收到新状态。
    public void Receive(Snapshot next, long? receivedAt = null)
    {
        if (next.Source is not ("plc" or "simulation")) throw new InvalidDataException("不支持的状态来源");
        if (Latest != null && (next.Source != Latest.Source || next.TimestampUtc < Latest.TimestampUtc)) history.Clear();
        if (Latest == null || next != Latest)
        {
            if (!next.Connected || !next.HasWiper || !HasWindow(next)) history.Clear();
            if (history.Count == 0 || next.TimestampUtc > history[^1].TimestampUtc) history.Add(next);
            else if (next.TimestampUtc == history[^1].TimestampUtc) history[^1] = next;
            while (history.Count > 16) history.RemoveAt(0);
        }
        Latest = next; receivedTick = receivedAt; Error = "";
    }

    public void Clear(string error = "反馈已失效 · 等待有效快照")
    {
        Latest = null; receivedTick = null; history.Clear(); Error = error;
    }

    public (double Wiper, double Window) VisualPose()
    {
        if (Latest == null) return (0, 0);
        double w = Latest.WiperAngleDegrees ?? 0, p = WindowPosition(Latest);
        if (history.Count == 0) return (w, p);
        DateTimeOffset target = clock().AddMilliseconds(-100);
        Snapshot a = history[0], b = history[^1];
        if (target <= a.TimestampUtc) b = a;
        else if (target >= b.TimestampUtc) a = b;
        else for (int i = 1; i < history.Count; i++)
            if (history[i].TimestampUtc >= target) { a = history[i - 1]; b = history[i]; break; }
        double duration = (b.TimestampUtc - a.TimestampUtc).TotalMilliseconds;
        double t = duration <= 0 ? 1 : Math.Clamp((target - a.TimestampUtc).TotalMilliseconds / duration, 0, 1);
        // 两帧相隔超过 600 ms 或来源改变时不跨越插值。数字控件仍使用 Latest，动画延迟 100 ms 平滑显示。
        if (duration > 600 || a.Source != b.Source || !a.Connected || !b.Connected) return (w, p);
        if (a.HasWiper && b.HasWiper) w = a.WiperAngleDegrees!.Value + (b.WiperAngleDegrees!.Value - a.WiperAngleDegrees.Value) * t;
        if (HasWindow(a) && HasWindow(b)) p = WindowPosition(a) + (WindowPosition(b)-WindowPosition(a))*t;
        return (w, p);
    }
}
