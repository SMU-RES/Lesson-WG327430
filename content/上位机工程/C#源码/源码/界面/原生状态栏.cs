namespace 汽车综合实践;

/// <summary>
/// 四项状态栏采用真正的 GroupBox + Label，由 Windows 系统绘制边框和文字。
/// 数值来自同一个 DB101 状态快照，绝不从三维插值姿态反算后写入测量记录。
/// 雨刷仍在车辆画面中显示动作；底部只保留车窗角度、开度、计数和当前挡位。
/// </summary>
internal sealed class 原生反馈栏 : UserControl
{
    private readonly Label[] 数值 = new Label[4];
    internal IReadOnlyList<Label> 数值控件 => 数值;
    internal 原生反馈栏()
    {
        Name="原生四项状态栏"; Dock=DockStyle.Bottom; Height=88;
        BackColor=SystemColors.Control;
        var table=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Padding=new Padding(5,0,5,5),Margin=Padding.Empty };
        table.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        string[] titles={"车窗电机角度","车窗开度","往返次数","当前挡位"};
        for(int i=0;i<4;i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            var group=new GroupBox { Name="状态_"+titles[i],Text=titles[i],Dock=DockStyle.Fill,FlatStyle=FlatStyle.Standard,Margin=new Padding(4),Padding=new Padding(6,4,6,6),BackColor=SystemColors.Control };
            数值[i]=new Label { Text="—",Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",16,FontStyle.Regular),ForeColor=SystemColors.ControlText,BackColor=SystemColors.Control,Margin=Padding.Empty };
            group.Controls.Add(数值[i]);table.Controls.Add(group,i,0);
        }
        Controls.Add(table);
    }
    internal void 更新(状态快照? state)
    {
        string[] modes={"关","自动","小","中","大"};
        string[] values={state?.车窗位置有效==true?$"{state.车窗实际角度:F1}°":"—",
            state?.车窗开度有效==true?$"{state.字[16]}%":"—",state==null?"—":state.字[32].ToString(),
            state==null?"—":modes[Math.Clamp((int)state.字[7],0,4)]+" / "+state.字[8]+"档"};
        for(int i=0;i<4;i++)if(数值[i].Text!=values[i])数值[i].Text=values[i];
    }
}
