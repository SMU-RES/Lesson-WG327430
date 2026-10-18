using AutomotiveGpu;

namespace 汽车综合实践;

/// <summary>
/// 同一操作窗口的图形部分。阅读顺序：操作窗口订阅通信事件 → 接收视图反馈 → 两种画面。
/// 二维直接绘制已收到的状态；三维在已收样本间插值以减小视觉跳变，不由插值结果控制PLC。
///
/// 每个样本带通信线程的接收时刻。到UI时已超过600 ms的样本丢弃；运行中超过期限也清图。
/// 切换二维/三维、切标签页先释放车窗按住命令，避免切页后“松手事件找不到旧按钮”。
/// 三维控件只创建一次，切换可见性和渲染开关；既减少GPU资源重建，也保持用户选择视角。
/// 四项原生状态栏位于两种画面的共同父容器，因此切图不会换一套数值来源。
/// </summary>
internal sealed partial class 操作窗口
{
    // 一个通信样本同时供二维和三维使用；此处不启动第二个网络连接。
    private readonly SnapshotReader 三维状态 = new();
    private GlView 三维画布 = null!;
    private Panel 视图区域 = null!, 三维区域 = null!;
    private readonly 现代按钮 切换视图按钮 = new() { Name="切换视图",Text="切换至三维", Dock=DockStyle.Fill,Margin=new Padding(3,1,3,1) };
    private readonly 现代按钮 还原视角按钮 = new() { Name="还原视角",Text="还原视角", Dock=DockStyle.Fill,Margin=new Padding(3,1,3,1),Enabled=false };
    private readonly 稳定状态标签 三维数值 = new() { Dock=DockStyle.Top, Height=28, Padding=new Padding(12,3,8,0), AutoEllipsis=true };
    private readonly Label 三维提示 = new() { Dock=DockStyle.Bottom, Height=25, Padding=new Padding(12,0,8,0), Text="拖动旋转 · 滚轮缩放", AutoEllipsis=true };
    private readonly 原生反馈栏 原生数据卡 = new();
    private bool 三维模式;
    private bool 控制页可见=true;

    private Control 建集成视图()
    {
        var 容器 = new Panel { Name="车辆视图容器",Dock=DockStyle.Fill, BackColor=主题.背景,Margin=Padding.Empty };
        切换视图按钮.Click += (_,_) => 设置三维模式(!三维模式);
        视图区域 = new Panel { Dock=DockStyle.Fill };
        三维区域 = new Panel { Dock=DockStyle.Fill, Visible=false, BackColor=主题.背景 };
        三维画布 = new GlView(三维状态) { Dock=DockStyle.Fill };
        三维画布.SetRenderingEnabled(false);
        var 底栏 = new Panel {Name="三维数据区",Dock=DockStyle.Bottom,Height=250,BackColor=主题.背景};
        底栏.Controls.Add(三维数值);底栏.Controls.Add(三维提示);
        底栏.SizeChanged+=(_,_)=>{int wanted=53;if(底栏.Height!=wanted)底栏.Height=wanted;};
        底栏.Height=53;
        三维区域.Controls.Add(三维画布); 三维区域.Controls.Add(底栏);
        画布.Dock=DockStyle.Fill;
        视图区域.Controls.Add(三维区域); 视图区域.Controls.Add(画布);
        容器.Controls.Add(视图区域);
        容器.Controls.Add(原生数据卡);
        还原视角按钮.Click += (_,_) => 三维画布.ResetView();
        // 页面隐藏后关闭三维定时渲染，保留网格与句柄供下次显示复用。
        视图区域.VisibleChanged += (_,_) => 三维画布.SetRenderingEnabled(三维模式 && 控制页可见 && 视图区域.Visible);
        更新三维数值();
        return 容器;
    }

    private Control 建视图控制()
    {
        var row=建网格(1,2);row.Controls.Add(切换视图按钮,0,0);row.Controls.Add(还原视角按钮,1,0);
        return 建控制分组("车辆视图",row,主题.浮层);
    }

    private void 设置三维模式(bool 使用三维)
    {
        通信.释放();
        foreach(var 按钮 in 升降按钮集) 按钮.Capture=false;
        检查反馈期限(Environment.TickCount64);
        三维模式=使用三维;
        三维画布.SetRenderingEnabled(false);
        画布.Visible=!使用三维;
        三维区域.Visible=使用三维;
        if(使用三维) 三维区域.BringToFront(); else 画布.BringToFront();
        三维画布.SetRenderingEnabled(使用三维 && 控制页可见 && 视图区域.Visible);
        切换视图按钮.Text=使用三维?"切换至二维":"切换至三维";
        还原视角按钮.Enabled=使用三维;
        更新三维数值();
    }

    private void 设置控制页可见(bool 可见)
    {
        控制页可见=可见;
        三维画布.SetRenderingEnabled(三维模式 && 可见 && 视图区域.Visible);
    }

    private bool 接收视图反馈(状态快照? 状态, long 接收时刻, DateTimeOffset 接收时间, string 文本)
    {
        long 现在=Environment.TickCount64;
        if(状态!=null && 现在-接收时刻>=600) { 检查反馈期限(现在); return false; }
        当前状态=状态;
        画布.反馈=状态;
        原生数据卡.更新(状态);
        if(状态==null) 三维状态.Clear(文本);
        else
        {
            上次有效反馈时刻=接收时刻;
            三维状态.Receive(new Snapshot(接收时间,"plc",true,
                状态.雨刷显示有效,状态.车窗位置有效,
                状态.雨刷显示有效?状态.雨刷显示角度:null,
                状态.车窗位置有效?状态.车窗实际角度:null,
                状态.车窗开度有效?状态.字[16]:null,
                模式名[状态.字[7]],文本),接收时刻);
        }
        if(!三维模式) 画布.Invalidate();
        更新状态概要(状态,文本);
        更新三维数值();
        return true;
    }

    private bool 检查反馈期限(long 现在)
    {
        if(当前状态==null || 现在-上次有效反馈时刻<600) return false;
        接收视图反馈(null,现在,DateTimeOffset.UtcNow,"反馈过期600ms");
        发布器.发布(null,"反馈过期600ms");
        控制会话.Checked=false; 通信.释放(); 更新操作许可();
        更新状态概要(null,"反馈超过600毫秒未更新，控制已停止");
        return true;
    }

    private void 更新状态概要(状态快照? 状态,string 文本)
    {
        string 来源=状态==null?"PLC未连接":"PLC在线";
        string 概要=状态==null?$"{来源}  |  {文本.Replace('\r',' ').Replace('\n',' ')}":
            $"{来源}  |  {(状态.标志(4)&&状态.标志(14)?"控制已启用":"控制未启用")}  |  {诊断文本(状态)}";
        状态文字.更新(概要);
        状态文字.ForeColor=状态==null || 诊断文本(状态)!="无当前报警"?主题.红:主题.次要;
    }

    private void 更新三维数值()
    {
        if(三维画布==null) return;
        var s=当前状态;
        三维数值.更新(s==null || !三维状态.Fresh?"等待反馈":
            $"模拟雨量 {s.字[11]} / 1000    模拟手 {(s.标志(10)?"在位":"移开")}    防夹 {(s.标志(9)?"触发":"未触发")}");
        三维数值.ForeColor=s!=null && 三维状态.WiperValid && 三维状态.WindowValid?主题.文字:主题.红;
    }
}
