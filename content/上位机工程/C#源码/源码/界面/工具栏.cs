namespace 汽车综合实践;

internal sealed partial class 操作窗口
{
    private Button 连接目标 = null!, 停止目标 = null!, 复位目标 = null!;
    internal ToolStrip 快捷工具栏 { get; } = new()
    {
        Name = "主工具栏", Dock = DockStyle.Top, AutoSize = false, Height = 25,
        GripStyle = ToolStripGripStyle.Hidden, Renderer = new ToolStripSystemRenderer(),
        Padding = new Padding(4, 0, 0, 0)
    };

    /// <summary>
    /// 工具栏只是原按钮的快捷入口，不建立第二条控制路径。
    /// Tag 保存对应 Button，便于控件树检查确认映射；EnabledChanged 保持相同启用条件。
    /// 工具栏与原按钮同在汽车控制页，PerformClick 不会转发给隐藏页面中的按钮。
    /// </summary>
    private ToolStrip 建快捷工具栏()
    {
        void 添加(string name, string text, Button target)
        {
            var item = new ToolStripButton(text)
            {
                Name = name, DisplayStyle = ToolStripItemDisplayStyle.Text,
                Tag = target, Enabled = target.Enabled,
                ToolTipText = "与“" + target.Text + "”按钮执行相同操作"
            };
            target.EnabledChanged += (_, _) => item.Enabled = target.Enabled;
            item.Click += (_, _) => { if (target.Enabled) target.PerformClick(); };
            快捷工具栏.Items.Add(item);
        }
        添加("工具栏连接PLC", "连接PLC", 连接目标);
        添加("工具栏软件停止", "软件停止", 停止目标);
        添加("工具栏复位", "复位", 复位目标);
        return 快捷工具栏;
    }
}
