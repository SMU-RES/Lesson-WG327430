using System.Collections.Concurrent;

namespace 汽车综合实践;

/// <summary>
/// 阅读起点为 Main.cs。本窗体负责布局、控件事件和反馈显示，不直接驱动电机。
/// partial 类的另一部分在「集成视图.cs」「管理页面.cs」；F12 可继续跟踪同一类型。
/// 控件点击只调用通信服务的方法。真实允许运动与最终挡位来自 PLC 反馈；
/// 所以“申请远程控制”按钮与“控制已启用”反馈是不同概念，不能互相伪造。
/// </summary>
internal sealed partial class 操作窗口 : Form
{
    private readonly 通信服务 通信;
    private readonly 快照发布 发布器;
    private readonly System.Windows.Forms.Timer 界面计时器 = new() { Interval = 100 };
    private readonly 实际反馈画布 画布 = new();
    private readonly 稳定状态标签 状态文字 = new() { Dock=DockStyle.Fill, Padding=new Padding(9,0,7,0), TextAlign=ContentAlignment.MiddleLeft,Text="PLC未连接" };
    private readonly 控制申请输入 控制会话 = new();
    private readonly Button 防夹按钮 = new 现代按钮() { Name="防夹触发按钮",Text="触发防夹",Dock=DockStyle.Fill,Margin=new Padding(3,1,3,1) };
    private readonly Label 雨量文字 = new() { AutoSize=false,Text="雨量：0 / 1000",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(3,0,0,0) };
    private readonly TrackBar 雨量条 = new() { Name="雨量输入",Minimum=0,Maximum=1000,TickFrequency=100,TickStyle=TickStyle.None,AutoSize=false,Dock=DockStyle.Fill,Margin=Padding.Empty };
    private readonly 本地数据库 数据;
    private readonly string 用户;
    private long 上次记录时刻,上次有效反馈时刻;
    private readonly ConcurrentQueue<Action> 记录队列 = new();
    private readonly CancellationTokenSource 记录取消 = new();
    private readonly List<Button> 模式按钮集 = new(),升降按钮集 = new();
    private Button? 一次按钮;
    private readonly Label 记录告警 = new() { Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleRight,ForeColor=Color.Firebrick };
    private Task? 记录任务;
    private 状态快照? 当前状态;
    private static readonly string[] 模式名 = ["关","自动","小","中","大"];

    /// <summary>
    /// 窗口只建立一个通信服务；连接须由操作者明确点击，构造窗口不连接设备。
    /// 窗口按可用高度分配控制区，不采用控件固定总高加滚动条的方式。
    /// </summary>
    public 操作窗口(本地数据库 数据)
    {
        if(!数据.已建立操作会话)throw new InvalidOperationException("未建立有效的操作身份");
        this.数据=数据;用户=数据.当前操作者;通信=new 通信服务();发布器=new 快照发布(数据.目录);
        // 百分比布局配合 Windows DPI 字体，避免二次缩放固定坐标。
        AutoScaleMode=AutoScaleMode.None;
        Text="汽车控制实验台";Size=new Size(1280,720);MinimumSize=new Size(900,600);
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",9f);DoubleBuffered=true;
        启动记录线程();
        var 菜单=new MenuStrip();var 文件项=new ToolStripMenuItem("文件");var 导出=new ToolStripMenuItem("导出测量记录");
        导出.Click+=(_,_)=>
        {
            通信.释放();using var 框=new SaveFileDialog {Filter="测量记录|*.csv",FileName="汽车测量记录.csv"};
            if(框.ShowDialog()==DialogResult.OK)安全执行(()=>本地数据库.导出表(数据.查记录(false,"",DateTime.Now.AddYears(-20),DateTime.Now.AddDays(1),false),框.FileName));
        };
        文件项.DropDownItems.Add(导出);菜单.Items.Add(文件项);MainMenuStrip=菜单;
        // 单一 PLC 运行方式，不设置离线入口，也不在连接异常时改变数据来源。
        var 标签页=new 侧栏标签页 {Dock=DockStyle.Fill,Name="主功能页"};
        var 操作页=new TabPage("汽车控制");标签页.TabPages.Add(操作页);
        标签页.SelectedIndexChanged+=(_,_)=>{通信.释放();设置控制页可见(标签页.SelectedTab==操作页);};
        var 运行组=建运行控制();var 雨刷组=建雨刷控制();var 车窗组=建车窗控制();var 输入组=建实验输入();
        var 集成视图=建集成视图();var 视图组=建视图控制();
        var 侧栏=new TableLayoutPanel {Name="控制侧栏",Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,AutoScroll=false,Margin=Padding.Empty,Padding=new Padding(4,3,4,3)};
        侧栏.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        // 运行、车窗、雨刷、实验输入和视图采用固定顺序，比例布局保持最小窗口可操作。
        Control[] groups=[运行组,车窗组,雨刷组,输入组,视图组];
        float[] heights=[27,16,21,24,12];
        for(int i=0;i<groups.Length;i++){侧栏.RowStyles.Add(new RowStyle(SizeType.Percent,heights[i]));侧栏.Controls.Add(groups[i],0,i);}
        var 主布局=new TableLayoutPanel {Name="控制页主布局",Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty,Padding=Padding.Empty};
        const float 操作宽度=292;
        主布局.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        主布局.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,操作宽度));
        主布局.RowStyles.Add(new RowStyle(SizeType.Absolute,30));主布局.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        主布局.Controls.Add(状态文字,0,0);主布局.SetColumnSpan(状态文字,2);
        主布局.Controls.Add(集成视图,0,1);主布局.Controls.Add(侧栏,1,1);
        操作页.Controls.Add(主布局);操作页.Controls.Add(建快捷工具栏());建记录页(标签页);建诊断页(标签页);
        var 顶栏=new TableLayoutPanel {Dock=DockStyle.Top,Height=36,Padding=new Padding(11,3,11,3),ColumnCount=3,RowCount=1};
        顶栏.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));顶栏.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));顶栏.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,10));
        顶栏.Controls.Add(new Label {Text="汽车控制实验台",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Font.FontFamily,12)},0,0);
        顶栏.Controls.Add(记录告警,1,0);顶栏.Controls.Add(new Label {Text=用户,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight},2,0);
        Controls.Add(标签页);Controls.Add(顶栏);Controls.Add(菜单);主题.应用(this);
        状态文字.BackColor=主题.卡片;状态文字.ForeColor=主题.次要;
        // 通信事件发生于后台线程。接收时间也在后台记录，防止 UI 堆积旧样本后延长有效期。
        通信.状态更新+=(状态,文本)=>
        {
            long 接收时刻=Environment.TickCount64;DateTimeOffset 接收时间=DateTimeOffset.UtcNow;
            if(IsDisposed||!IsHandleCreated)return;
            try {BeginInvoke((Action)(()=>
            {
                if(!接收视图反馈(状态,接收时刻,接收时间,文本))return;
                if(状态!=null)上次有效反馈时刻=接收时刻;
                更新操作许可();发布器.发布(状态,状态==null?文本:诊断文本(状态));
                if(状态==null){控制会话.Checked=false;记(文本);}
                else if(Environment.TickCount64-上次记录时刻>1000)
                {上次记录时刻=Environment.TickCount64;排队记录(()=>数据.记测量(状态));}
            }));}catch(InvalidOperationException){}
        };
        界面计时器.Tick+=(_,_)=>{通信.界面存活();检查反馈期限(Environment.TickCount64);更新防夹按钮();};界面计时器.Start();
        Deactivate+=(_,_)=>通信.释放();
        FormClosing+=(_,_)=>
        {
            界面计时器.Stop();界面计时器.Dispose();通信.Dispose();记录取消.Cancel();发布器.发布(null,"上位机已关闭");_=发布器.DisposeAsync();
            if(记录任务 is {} 任务)_=任务.ContinueWith(_=>记录取消.Dispose(),TaskScheduler.Default);
        };
    }

    /// <summary>连接、申请、停止、复位是不同命令；复位先撤回本机申请和升降保持。</summary>
    private Control 建运行控制()
    {
        var row=建网格(3,2);
        var 地址=new TextBox {Name="PLC地址",Text="192.168.0.11",Dock=DockStyle.Fill,Margin=new Padding(3,4,3,2)};
        var 连接=new 现代按钮 {Name="连接PLC",Text="连接PLC左",Dock=DockStyle.Fill,Margin=new Padding(3,1,3,1)};
        连接.Click+=(_,_)=>安全执行(()=>{通信.启动(地址.Text.Trim());连接.Enabled=false;地址.Enabled=false;记("连接PLC："+地址.Text);});
        row.Controls.Add(地址,0,0);row.Controls.Add(连接,1,0);row.Controls.Add(控制会话,0,1);row.SetColumnSpan(控制会话,2);
        控制会话.CheckedChanged+=(_,_)=>{通信.控制会话(控制会话.Checked&&数据.已建立操作会话);记(控制会话.Checked?"申请控制会话":"释放控制会话");};
        var 停止=new 现代按钮 {Name="软件停止",Text="软件停止",Dock=DockStyle.Fill,Margin=new Padding(3,1,3,1)};
        停止.Click+=(_,_)=>{通信.停止();控制会话.Checked=false;记("软件停止");};
        var 复位=new 现代按钮 {Name="复位",Text="复位",Dock=DockStyle.Fill,Margin=new Padding(3,1,3,1)};
        复位.Click+=(_,_)=>{if(当前状态==null)return;控制会话.Checked=false;通信.复位();记("申请复位：释放控制与升降；复位后重新申请控制");};
        row.Controls.Add(停止,0,2);row.Controls.Add(复位,1,2);
        连接目标=连接;停止目标=停止;复位目标=复位;
        return 建控制分组("运行与连接",row,Color.FromArgb(235,243,251));
    }

    /// <summary>上排关/自动/一次，下排小/中/大；挡位票改变后由 PLC 仲裁是否接受。</summary>
    private Control 建雨刷控制()
    {
        var row=建网格(2,3);
        foreach(var item in new[]{(档:0,列:0,行:0),(档:1,列:1,行:0),(档:2,列:0,行:1),(档:3,列:1,行:1),(档:4,列:2,行:1)})
        {
            int 档=item.档;
            var 按=new 现代按钮 {Name="雨刷_"+模式名[档],Text=模式名[档],Tag=档,Dock=DockStyle.Fill,Margin=new Padding(3,2,3,2),Enabled=false};
            按.Click+=(_,_)=>{通信.选模式(档);记("选择雨刷"+模式名[档]);};模式按钮集.Add(按);row.Controls.Add(按,item.列,item.行);
        }
        一次按钮=new 现代按钮 {Name="一次往返",Text="一次",Dock=DockStyle.Fill,Margin=new Padding(3,2,3,2),Enabled=false};
        一次按钮.Click+=(_,_)=>{通信.一次();记("申请一次雨刷");};row.Controls.Add(一次按钮,2,0);
        return 建控制分组("雨刷控制",row,Color.FromArgb(235,245,253));
    }

    /// <summary>保持按钮只在按住期间发命令；释放鼠标、捕获或键盘按键立即撤回。</summary>
    private Control 建车窗控制()
    {
        var row=建网格(1,2);
        foreach(bool 是上行 in new[]{true,false})
        {
            bool 上行=是上行;
            var 键=new 现代按钮 {Name=上行?"车窗向上":"车窗向下",Text=上行?"关窗（向上）":"开窗（向下）",AccessibleDescription="按住运行，松开停止",Dock=DockStyle.Fill,Margin=new Padding(3,2,3,2),Enabled=false};
            键.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){键.Capture=true;通信.按钮(上行,!上行);}};
            键.MouseUp+=(_,_)=>通信.释放();键.MouseCaptureChanged+=(_,_)=>{if(!键.Capture)通信.释放();};
            键.KeyDown+=(_,e)=>{if(e.KeyCode is Keys.Space or Keys.Enter){通信.按钮(上行,!上行);e.SuppressKeyPress=true;}};
            键.KeyUp+=(_,e)=>{if(e.KeyCode is Keys.Space or Keys.Enter)通信.释放();};键.LostFocus+=(_,_)=>通信.释放();
            升降按钮集.Add(键);row.Controls.Add(键,上行?0:1,0);
        }
        return 建控制分组("车窗控制 · 按住运行",row,Color.FromArgb(234,246,239));
    }

    private Control 建实验输入()
    {
        var row=建网格(3,1);row.RowStyles.Clear();
        row.RowStyles.Add(new RowStyle(SizeType.Percent,25));row.RowStyles.Add(new RowStyle(SizeType.Percent,35));row.RowStyles.Add(new RowStyle(SizeType.Percent,40));
        row.Controls.Add(雨量文字,0,0);row.Controls.Add(雨量条,0,1);
        雨量条.ValueChanged+=(_,_)=>{通信.改雨量(雨量条.Value);雨量文字.Text=$"雨量：{雨量条.Value} / 1000";};
        // 只触发既有的短脉冲与冷却逻辑；锁存、停车和反向过程由 PLC 负责。
        防夹按钮.Click+=(_,_)=>{if(当前状态!=null&&通信.触发防夹())记("触发防夹");更新防夹按钮();};row.Controls.Add(防夹按钮,0,2);
        return 建控制分组("实验输入",row,Color.FromArgb(251,247,235));
    }

    // 百分比布局使行高随可用面积分配。GroupBox 使用系统主题，不画仿制边框。
    private static TableLayoutPanel 建网格(int rows,int columns)
    {
        var table=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=rows,ColumnCount=columns,Margin=Padding.Empty,Padding=Padding.Empty,AutoScroll=false};
        for(int i=0;i<columns;i++)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));
        for(int i=0;i<rows;i++)table.RowStyles.Add(new RowStyle(SizeType.Percent,100f/rows));return table;
    }
    private GroupBox 建控制分组(string title,Control content,Color color)
    {
        var group=new GroupBox {Name=title+"分组",Text=title,Dock=DockStyle.Fill,Padding=new Padding(6,4,6,5),Margin=new Padding(2,2,2,2),FlatStyle=FlatStyle.Standard,Tag=color};
        group.Controls.Add(content);return group;
    }
    private void 安全执行(Action 动作)
    {通信.释放();try{动作();}catch(Exception error){MessageBox.Show(error.Message,"操作未完成");}}

    /// <summary>
    /// 位 4 为 PC 会话，位 14 为运行许可；就绪按轴判断，车窗故障不封锁雨刷选档。
    /// 界面禁用只是提示，PLC 仍检查停止、通信、新命令及机构保护条件。
    /// </summary>
    private void 更新操作许可()
    {
        bool 会话=数据.已建立操作会话&&当前状态?.标志(4)==true&&当前状态.标志(14);
        雨量条.Enabled=数据.已建立操作会话&&当前状态!=null;
        foreach(var 按 in 模式按钮集){按.Enabled=会话;if(按 is 现代按钮 modern)modern.已选中=当前状态!=null&&Convert.ToInt32(按.Tag)==当前状态.字[7];}
        foreach(var 按 in 升降按钮集)按.Enabled=会话&&(当前状态!.字[39]&8)!=0&&!当前状态.标志(9);
        if(一次按钮!=null)一次按钮.Enabled=会话&&(当前状态!.字[39]&7)==4&&当前状态.字[33]==10;
        更新防夹按钮();
    }
    private void 更新防夹按钮()
    {
        int remaining=通信.防夹剩余冷却;
        string text=remaining>0?$"防夹冷却 {remaining/1000.0:F1} 秒":"触发防夹";
        if(防夹按钮.Text!=text)防夹按钮.Text=text;
        防夹按钮.Enabled=数据.已建立操作会话&&当前状态!=null&&remaining==0;
    }
    // 记录线程只消费已复制的数据，不占用 S7 客户端；磁盘变慢不会拖长命令交换周期。
    private void 启动记录线程()
    {
        记录任务=Task.Run(async()=>
        {
            while(!记录取消.IsCancellationRequested||!记录队列.IsEmpty)
            {
                while(记录队列.TryDequeue(out var write)){try{write();}catch(Exception error){通知记录错误(error.Message);}}
                try{await Task.Delay(100,记录取消.Token);}catch(OperationCanceledException){}
            }
        });
    }
    private void 排队记录(Action 记录)
    {if(记录队列.Count>=1000){记录告警.Text="记录队列已满；本次记录丢弃";return;}记录队列.Enqueue(记录);}
    private void 通知记录错误(string text)
    {if(!IsHandleCreated||IsDisposed)return;try{BeginInvoke((Action)(()=>记录告警.Text="写盘异常："+text));}catch(InvalidOperationException){}}
    private void 记(string text)=>排队记录(()=>数据.记操作(用户,"[PLC] "+text));
}
