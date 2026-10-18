using System.Drawing.Drawing2D;
namespace 汽车综合实践;

// 本文件只负责显示。按钮、滑块、分组框等交给 Windows Visual Styles 绘制，
// 在启用 Aero 的 Win7 电脑上采用系统主题；系统主题改变时不假造另一套控件外观。
internal static class 主题
{
    public static readonly Color 背景=SystemColors.Control,卡片=SystemColors.Window,浮层=SystemColors.Control,文字=SystemColors.ControlText,次要=SystemColors.GrayText,青=SystemColors.Highlight,绿=Color.FromArgb(30,120,50),红=Color.Firebrick,边框=SystemColors.ControlDark;
    public const TextFormatFlags 自绘文字标志=TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
    public static void 清理控件背景(Control control,Graphics graphics)
    {
        Control? parent=control.Parent;while(parent!=null&&parent.BackColor.A!=255)parent=parent.Parent;
        using var background=new SolidBrush(parent?.BackColor??卡片);graphics.FillRectangle(background,control.ClientRectangle);
    }
    public static GraphicsPath 圆角(RectangleF r,float radius=4)
    {
        var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    public static void 应用(Control root)
    {
        if(root is 实际反馈画布)return;
        root.ForeColor=文字;
        switch(root)
        {
            case Form:root.BackColor=背景;break;
            case TabPage:root.BackColor=卡片;break;
            case Panel p:root.BackColor=SystemColors.Control;break;
            case GroupBox g:g.BackColor=SystemColors.Control;g.FlatStyle=FlatStyle.Standard;break;
            case TrackBar t:t.BackColor=SystemColors.Control;break;
            case TextBox t:t.BackColor=SystemColors.Window;t.ForeColor=SystemColors.WindowText;t.BorderStyle=BorderStyle.Fixed3D;break;
            case ComboBox c:c.BackColor=SystemColors.Window;c.ForeColor=SystemColors.WindowText;c.FlatStyle=FlatStyle.Standard;break;
            case NumericUpDown n:n.BackColor=SystemColors.Window;n.ForeColor=SystemColors.WindowText;n.BorderStyle=BorderStyle.Fixed3D;break;
            case Button b:b.FlatStyle=FlatStyle.System;b.UseVisualStyleBackColor=true;break;
            case CheckBox c:c.FlatStyle=FlatStyle.System;c.UseVisualStyleBackColor=true;break;
            case DataGridView g:
                g.BackgroundColor=SystemColors.Window;g.BorderStyle=BorderStyle.Fixed3D;g.GridColor=主题.边框;g.EnableHeadersVisualStyles=true;g.RowHeadersVisible=false;g.ColumnHeadersHeight=30;g.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                g.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Raised;g.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
                g.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {Font=root.Font,Padding=new Padding(5,0,0,0)};
                g.DefaultCellStyle=new DataGridViewCellStyle {BackColor=SystemColors.Window,ForeColor=SystemColors.WindowText,SelectionBackColor=SystemColors.Highlight,SelectionForeColor=SystemColors.HighlightText,Padding=new Padding(5,3,3,3)};
                g.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(245,249,253);g.RowTemplate.Height=30;break;
            case MenuStrip m:m.BackColor=SystemColors.MenuBar;m.Renderer=new ToolStripSystemRenderer();break;
            case Label l:l.BackColor=Color.Transparent;break;
        }
        foreach(Control child in root.Controls)应用(child);
    }
}

/// <summary>
/// 远程控制申请控件。Checked 只是“本机请求”，PLC 是否接受请看状态反馈。
/// 内部使用真正的 Windows Button。鼠标点击、Space/Enter 激活沿用系统按钮行为。
/// Checked 这个接口名为兼容现有停止/复位调用保留，内部既没有滑块也没有复选框。
/// 点击只改变申请，CheckedChanged 交给操作窗口发送命令；顶栏再显示 PLC 是否授予控制。
/// </summary>
internal sealed class 控制申请输入 : UserControl
{
    private bool 本机请求;
    private readonly Button 按钮;
    internal Button 操作按钮 => 按钮;
    public bool 使用按钮 => true;
    public event EventHandler? CheckedChanged;
    public bool Checked
    {
        get => 本机请求;
        set
        {
            if (本机请求 == value) return;
            本机请求 = value;
            按钮.Text = value ? "释放远程控制" : "申请远程控制";
            按钮.AccessibleDescription = value ? "本机已申请，是否获权以PLC反馈为准；点击撤回申请" : "点击申请，是否获权以PLC反馈为准";
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public 控制申请输入()
    {
        Name = "远程控制输入";
        Dock = DockStyle.Fill;
        Margin = new Padding(3, 1, 3, 1);
        按钮 = new Button { Name="远程控制按钮", Text="申请远程控制", Dock=DockStyle.Fill, FlatStyle=FlatStyle.System, UseVisualStyleBackColor=true, TabStop=true,
            AccessibleName="远程控制申请按钮", AccessibleDescription="点击申请，是否获权以PLC反馈为准" };
        按钮.Click += (_, _) => Checked = !Checked;
        Controls.Add(按钮);
    }
}
internal sealed class 现代按钮:Button
{
    bool selected;
    Font? normalFont,selectedFont;
    public bool 已选中
    {
        get=>selected;
        set
        {
            if(selected==value)return;
            selected=value;normalFont??=Font;selectedFont??=new Font(normalFont,FontStyle.Bold);
            Font=value?selectedFont:normalFont;
            AccessibleDescription=value?"当前雨刷挡位":"";
        }
    }
    public 现代按钮(){FlatStyle=FlatStyle.System;UseVisualStyleBackColor=true;Padding=new Padding(6,2,6,2);Margin=new Padding(3);Height=30;}
    protected override void Dispose(bool disposing){if(disposing)selectedFont?.Dispose();base.Dispose(disposing);}
}
internal sealed class 侧栏标签页:TabControl
{
    public 侧栏标签页(){Alignment=TabAlignment.Top;SizeMode=TabSizeMode.Normal;Padding=new Point(18,7);}
}
internal sealed class 现代下拉框:ComboBox
{
    public 现代下拉框(){DrawMode=DrawMode.Normal;FlatStyle=FlatStyle.Standard;BackColor=SystemColors.Window;ForeColor=SystemColors.WindowText;}
}
internal sealed class 现代日期输入:DateTimePicker
{
    public 现代日期输入(){Format=DateTimePickerFormat.Custom;CustomFormat="yyyy-MM-dd HH:mm:ss";}
}
internal sealed class 稳定状态标签:Label
{
    internal int 文本更新次数 {get;private set;}
    public 稳定状态标签(){AutoSize=false;AutoEllipsis=true;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.Opaque,true);}
    protected override void OnPaint(PaintEventArgs e)
    {
        using var fill=new SolidBrush(BackColor.A==255?BackColor:Parent?.BackColor??主题.背景);
        e.Graphics.FillRectangle(fill,ClientRectangle);base.OnPaint(e);
    }
    internal bool 更新(string text)
    {
        if(Text==text)return false;
        Text=text;文本更新次数++;return true;
    }
}
