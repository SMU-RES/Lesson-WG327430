namespace 汽车综合实践;

/// <summary>
/// 程序入口：先保证单实例，再建立数据库、登录和主窗口生命周期。
/// 单实例互斥量防止两个进程同时写同一台 PLC 的 DB100 命令区。
/// 登录只确定本机记录身份；远程会话、运行许可和轴就绪仍由 PLC 独立确认。
///
/// 阅读顺序：界面/操作窗口 → 通信/通信服务 → 命令镜像/接口协议/状态快照。
/// 数据/本地数据库负责持久化；界面/集成视图把同一反馈送给二维、三维和数值栏。
/// UI 不决定物理终点，也不根据画面计时截断电机；动作完成以 PLC 状态为准。
/// </summary>
internal static class 程序
{
    [STAThread]
    static int Main(string[] args)
    {
        string? Arg(string name) => args.FirstOrDefault(s => s.StartsWith(name + "="))?[(name.Length + 1)..];
        using var mutex = new Mutex(true, "Local\\汽车综合实践_唯一上位机", out bool first);
        if (!first) { MessageBox.Show("上位机已在运行。", "汽车控制实验台"); return 1; }
        ApplicationConfiguration.Initialize();
        try
        {
            // 默认数据位于当前用户目录，改变工程或可执行文件位置不影响历史记录。
            using var db = new 本地数据库(Arg("--data-dir"));
            using (var login = new 登录窗口(db))
            {
                if (login.ShowDialog() != DialogResult.OK) return 0;
            }
            using var form = new 操作窗口(db);
            Application.Run(form);
            return 0;
        }
        catch (Exception error)
        { MessageBox.Show(error.ToString(), "启动失败"); return 1; }
    }
}
