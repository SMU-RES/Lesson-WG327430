// 阅读入口见 Main.cs。本文件把界面意图转换为可靠的周期通信；不计算物理运动终点。
// 输入来自操作窗口，输出经接口协议编码后只写 PLC 左 DB100；DB101 始终由 PLC 发布。
using System;
using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;


namespace 汽车综合实践;

/// <summary>
/// 【通信状态机】
/// 1. 启动校验 IP，并只创建一个后台工作任务。
/// 2. 连接最长等待 3 s；之后读取正确版本的 DB101，等待 PLC 心跳真正变化。
/// 3. 对齐一次/复位/模式确认票，撤回旧的控制申请和车窗保持，避免重连自动重放。
/// 4. 每轮写 DB100、读 DB101、复读头序号，确认本帧一致后发布。
/// 5. PLC 心跳超过 600 ms 不变化则判通信失效；异常时清保持和会话，1 s 后重连。
///
/// 界面存活检测与 PLC 心跳检测是两条独立保护：前者检查本机操作窗口是否仍能处理消息，
/// 后者检查远端用户程序是否仍扫描。TCP 尚连接不等于 PLC 用户程序仍在运行。
/// 与该逻辑对应的可见提示位于管理页面.cs，动作是否最终执行由 PLC 反馈决定。
/// </summary>
internal sealed class 通信服务 : IDisposable
{
    private readonly object 锁 = new();
    private readonly CancellationTokenSource 取消 = new();
    private Task? 工作任务;
    private I传输? 当前客户端;
    private readonly int 端口;
    private readonly Func<string, int, I传输> 传输工厂;
    private readonly Func<long> 单调时钟;
    // 毫秒单位。时钟采用系统单调计数，不会被 Windows 时间校准或日期修改影响。
    public const int 防夹脉冲毫秒 = 300, 防夹冷却毫秒 = 3000;
    private long 防夹释放时刻, 防夹允许时刻;

    /// <summary>
    /// 生产默认工厂只建立 S7传输；可选工厂/时钟供隔离验证构建注入，发行界面不公开。
    /// 注意：所有 UI 线程写入字段均用同一把锁，通信线程取到的是一个完整命令镜像。
    /// </summary>
    public 通信服务(int 端口 = 102, bool 允许操作 = true,
        Func<string, int, I传输>? 传输工厂 = null, Func<long>? 单调时钟 = null)
    {
        this.端口 = 端口;
        this.传输工厂 = 传输工厂 ?? ((地址, port) => new S7传输(地址, port));
        this.单调时钟 = 单调时钟 ?? (() => Environment.TickCount64);
        界面心跳 = this.单调时钟();
        雨量有效 = 允许操作;
    }
    private bool 已释放;
    private ushort 模式, 雨量, 一次票, 复位票, 模式票;
    private bool 向上, 向下, 手在位, 软件停止, 远程会话, 雨量有效 = true;
    private long 界面心跳;
    public event Action<状态快照?, string>? 状态更新;
    public void 启动(string 地址)
    {
        if (工作任务 != null) throw new InvalidOperationException("通信线程已经启动");
        if (!System.Net.IPAddress.TryParse(地址, out _)) throw new ArgumentException("请输入合法IP地址");
        工作任务 = Task.Run(() => 循环(地址));
    }
    public void 界面存活() { lock (锁) 界面心跳 = 单调时钟(); }
    // 票号允许在 ushort 边界回绕；PLC 比较“是否变化”，不以大小判断新旧。
    public void 选模式(int 值) { if (值 is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(值)); lock (锁) { 模式 = checked((ushort)值); 模式票 = unchecked((ushort)(模式票 + 1)); } }
    public void 改雨量(int 值) { if (值 is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(值)); lock (锁) 雨量 = checked((ushort)值); }
    public void 一次() { lock (锁) 一次票 = unchecked((ushort)(一次票 + 1)); }
    // 复位先撤回申请；这样PLC不会在同一命令中看到“运行申请仍为真”的复位冲突。
    // 手在位输入单独保持其真实语义：脉冲输入到期后自然释放。
    public void 复位() { lock (锁) { 向上 = 向下 = 远程会话 = false; 软件停止 = false; 复位票 = unchecked((ushort)(复位票 + 1)); } }
    public void 停止() { lock (锁) { 向上 = 向下 = false; 软件停止 = true; 远程会话 = false; } }
    public void 控制会话(bool 开) { lock (锁) { 向上 = 向下 = false; 远程会话 = 开; } }
    /// <summary>
    /// 防夹按钮调用此方法。一次点击产生 300 ms 的传感器输入脉冲，
    /// 从接受点击的时刻起计 3 s 冷却。冷却内重复点击返回 false，不延长脉冲。
    /// 释放在取命令内进行，故 UI Timer 停止或主窗口暂时卡顿不会把手在位永久保持。
    /// </summary>
    public bool 触发防夹()
    {
        lock (锁)
        {
            long now = 单调时钟();
            if (now < 防夹允许时刻) return false;
            手在位 = true;
            防夹释放时刻 = now + 防夹脉冲毫秒;
            防夹允许时刻 = now + 防夹冷却毫秒;
            return true;
        }
    }
    public int 防夹剩余冷却
    { get { lock (锁) return (int)Math.Clamp(防夹允许时刻 - 单调时钟(), 0, 防夹冷却毫秒); } }
    public void 按钮(bool 上, bool 下) { lock (锁) { 向上 = 上; 向下 = 下; } }
    public void 释放() => 按钮(false, false);
    private 命令镜像 取命令()
    {
        lock (锁)
        {
            long now = 单调时钟();
            if (防夹释放时刻 != 0 && now >= 防夹释放时刻)
            { 手在位 = false; 防夹释放时刻 = 0; }
            bool 界面正常 = now - 界面心跳 < 400;
            if (!界面正常) { 向上 = 向下 = false; 远程会话 = false; }
            // 复制的是意图，不是驱动使能状态；PLC左仍将它与HMI、停止锁定和运行条件仲裁。
            ushort 标志 = (ushort)((向上 ? 1 : 0) | (向下 ? 2 : 0) | (手在位 ? 4 : 0)
                | (软件停止 ? 8 : 0) | (远程会话 ? 16 : 0) | (雨量有效 && 界面正常 ? 32 : 0));
            return new 命令镜像(模式, 雨量, 标志, 一次票, 复位票, 模式票);
        }
    }
    private async Task 循环(string 地址)
    {
        ushort 序号 = 0, 心跳 = 0;
        while (!取消.IsCancellationRequested)
        {
            using I传输 客户端 = 传输工厂(地址, 端口);
            try
            {
                lock (锁) 当前客户端 = 客户端;
                using (var 连接期限 = CancellationTokenSource.CreateLinkedTokenSource(取消.Token))
                {
                    连接期限.CancelAfter(3000);
                    using var 中止连接 = 连接期限.Token.Register(() => { try { 客户端.关闭(); } catch { } });
                    await 客户端.打开(连接期限.Token);
                }
                ushort 初始心跳 = 0;
                using (var 握手期限 = CancellationTokenSource.CreateLinkedTokenSource(取消.Token))
                {
                    握手期限.CancelAfter(1000);
                    using var 中止握手 = 握手期限.Token.Register(() => { try { 客户端.关闭(); } catch { } });
                    byte[] 初帧 = await 客户端.读(101, 0, 128, 握手期限.Token);
                    byte[] 初头 = await 客户端.读(101, 4, 2, 握手期限.Token);
                    var 初始状态 = 接口协议.解码(初帧, 初头); // 错误版本禁止开始写入。
                    初始心跳 = 初始状态.字[3];
                    // 先见到一次新的PLC发布：首读缓存的确认票可能落后于PLC内部已消费票。
                    // 单写者前提下，等待下一发布周期后才对齐票号；冻结PLC期间完全不写DB100。
                    do
                    {
                        await Task.Delay(30, 握手期限.Token);
                        初帧 = await 客户端.读(101, 0, 128, 握手期限.Token);
                        初头 = await 客户端.读(101, 4, 2, 握手期限.Token);
                        初始状态 = 接口协议.解码(初帧, 初头);
                    } while (初始状态.字[3] == 初始心跳);
                    初始心跳 = 初始状态.字[3];
                    lock (锁) { 一次票 = 初始状态.字[5]; 复位票 = 初始状态.字[6]; 模式票 = 初始状态.字[27]; 模式 = 初始状态.字[7]; 向上 = 向下 = 远程会话 = false; }
                }
                ushort 上次状态心跳 = 初始心跳; long 状态变化时刻 = Environment.TickCount64;
                bool 收过状态 = true;
                while (!取消.IsCancellationRequested)
                {
                    using var 交换期限 = CancellationTokenSource.CreateLinkedTokenSource(取消.Token);
                    // 一个交换的读写预算是500 ms；取消回调关闭套接字，不能只取消UI等待。
                    交换期限.CancelAfter(500);
                    using var 中止交换 = 交换期限.Token.Register(() => { try { 客户端.关闭(); } catch { } });
                    var 令牌 = 交换期限.Token;
                    序号 = 序号 >= 65534 ? (ushort)2 : (ushort)(序号 + 2);
                    心跳 = unchecked((ushort)(心跳 + 1));
                    byte[] 帧 = 接口协议.编码(取命令(), 序号, 心跳);
                    // 独占写DB100：先写奇数使帧无效，再写主体，最后单独提交偶数序号。
                    byte[] 奇数 = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(奇数, (ushort)(序号 | 1));
                    await 客户端.写(100, 4, 奇数, 令牌);
                    var 主体 = (byte[])帧.Clone(); 接口协议.写字(主体, 2, (ushort)(序号 | 1));
                    await 客户端.写(100, 0, 主体, 令牌);
                    await 客户端.写(100, 4, 帧.AsSpan(4, 2).ToArray(), 令牌);
                    状态快照? 状态 = null;
                    // 顺序锁冲突只重读，不把旧数据伪装成新反馈。
                    for (int 重读 = 0; 重读 < 3 && 状态 == null; 重读++)
                    {
                        byte[] 数据 = await 客户端.读(101, 0, 128, 令牌);
                        byte[] 头 = await 客户端.读(101, 4, 2, 令牌);
                        try { 状态 = 接口协议.解码(数据, 头); }
                        catch (InvalidDataException) when (重读 < 2) { await Task.Delay(15, 令牌); }
                    }
                    if (状态 == null) throw new InvalidDataException("无法获得一致状态帧");
                    bool 新状态 = !收过状态 || 状态.字[3] != 上次状态心跳;
                    if (新状态)
                    { 状态变化时刻 = Environment.TickCount64; 上次状态心跳 = 状态.字[3]; 收过状态 = true; }
                    if (Environment.TickCount64 - 状态变化时刻 > 600) throw new IOException("PLC用户程序心跳停止");
                    // 新鲜心跳才发布新反馈；重复帧不延长界面有效期。
                    if (新状态)
                        状态更新?.Invoke(状态, "PLC在线：" + 地址);
                    交换期限.CancelAfter(Timeout.Infinite);
                    await Task.Delay(100, 取消.Token);
                }
            }
            catch (OperationCanceledException) when (取消.IsCancellationRequested) { break; }
            catch (Exception 异常)
            {
                lock (锁) { 向上 = 向下 = false; 远程会话 = false; }
                // null是明确的“当前无有效反馈”，而不是拿最后一次角度冒充仍然在线。
                状态更新?.Invoke(null, "通信无效：" + 异常.Message + "；重连后需重新申请控制");
                try { await Task.Delay(1000, 取消.Token); } catch (OperationCanceledException) { break; }
            }
            finally { 客户端.关闭(); lock (锁) 当前客户端 = null; }
        }
    }
    public void Dispose()
    {
        if (已释放) return; 已释放 = true;
        释放(); 控制会话(false); 取消.Cancel();
        lock (锁) { try { 当前客户端?.关闭(); } catch { } }
        // 不阻塞UI等待网络线程；任务正常结束后释放取消源。
        if (工作任务 is { } 任务) _ = 任务.ContinueWith(_ => 取消.Dispose(), TaskScheduler.Default);
        else 取消.Dispose();
    }
}






