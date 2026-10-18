using System.Data;
namespace 汽车综合实践;
/// <summary>
/// 操作窗口的记录和诊断页，保持与主控制页共用同一个通信服务。
/// “测量记录/操作记录”用于查询已有数据；“实验备注”用于记录观察，独立表不回写测量值。
/// 查询和保存通过后台任务执行；进入对话框或改变页面前先释放车窗保持命令。
///
/// 诊断文本来自DB101，不根据按钮外观猜测PLC状态。F001等说明段只是码表，
/// 实际当前故障必须看“雨刷当前故障码/车窗当前故障码”及顶部提示。
/// 诊断页隐藏时不刷新；显示时最多每秒改写一次，便于用户稳定地选择并复制现场诊断。
/// 下一步可转到本地数据库.cs查看参数化SQL和记录分表，或状态快照.cs查看每项质量条件。
/// </summary>
internal sealed partial class 操作窗口
{
    private TabPage? 诊断页;
    private TextBox? 诊断显示;
    private long 上次诊断刷新;
    private int 诊断写入次数;
    static DataGridView 表格() => new() { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,BackgroundColor=Color.White };
    void 建记录页(TabControl tabs)
    {
        var page=new TabPage("实验记录"); tabs.TabPages.Add(page);
        var grid=表格();grid.Name="实验记录表格"; var bar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=112,MinimumSize=new Size(0,112),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(8),AutoScroll=true };
        var kind=new 现代下拉框 { Name="记录类型",DropDownStyle=ComboBoxStyle.DropDownList,Width=110 }; kind.Items.AddRange(["测量记录","操作记录","实验备注"]); kind.SelectedIndex=0;
        var from=new 现代日期输入 { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm:ss",Width=240,Value=DateTime.Now.Date };
        var to=new 现代日期输入 { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm:ss",Width=240,Value=DateTime.Now.Date.AddDays(1).AddSeconds(-1) };
        var keyword=new TextBox { Width=170,PlaceholderText="来源 / 用户 / 内容关键词" };
        var status=new Label { AutoSize=true,MaximumSize=new Size(720,0),Text="" };
        var query=new 现代按钮 { Text="查询",AutoSize=true }; var export=new 现代按钮 { Text="导出筛选结果",AutoSize=true };
        bool 可写备注=数据.可写实验备注(用户);
        var editor=new TableLayoutPanel { Name="实验备注编辑",Dock=DockStyle.Top,Height=105,Padding=new Padding(8,4,8,4),ColumnCount=3,RowCount=2,Visible=false };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,182));editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,254));editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute,54));editor.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        var noteDate=new 现代日期输入 { Name="备注日期",Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Width=130,Value=DateTime.Today,Enabled=可写备注 };
        var title=new TextBox { Name="备注标题",Width=196,MaxLength=80,ReadOnly=!可写备注 };
        var note=new TextBox { Name="备注内容",Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,MaxLength=2000,ReadOnly=!可写备注 };
        var add=new 现代按钮 {Name="新增实验备注",Text="新增备注",AutoSize=true,Enabled=可写备注};var edit=new 现代按钮 {Name="修改实验备注",Text="修改所选",AutoSize=true,Enabled=可写备注};var delete=new 现代按钮 {Name="删除实验备注",Text="删除所选",AutoSize=true,Enabled=可写备注};
        var noteActions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};noteActions.Controls.AddRange([add,edit,delete]);
        var noteInput=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};noteInput.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50));noteInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));noteInput.Controls.Add(new Label {Text="备注",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);noteInput.Controls.Add(note,1,0);
        editor.Controls.Add(Group("日期",noteDate,176),0,0);editor.Controls.Add(Group("标题",title,248),1,0);editor.Controls.Add(noteInput,2,0);editor.Controls.Add(noteActions,0,1);editor.SetColumnSpan(noteActions,3);
        void Busy(bool busy)
        {
            bool writable=可写备注&&!busy;
            add.Enabled=writable;edit.Enabled=writable;delete.Enabled=writable;
            noteDate.Enabled=writable;title.ReadOnly=!writable;note.ReadOnly=!writable;
            query.Enabled=!busy;export.Enabled=!busy;kind.Enabled=!busy;
        }
        async Task Load(bool save,string message="")
        {
            通信.释放(); Busy(true);
            try
            {
                if(from.Value>to.Value) throw new ArgumentException("开始时间不能晚于结束时间");
                int selected=kind.SelectedIndex; string text=keyword.Text; DateTime a=from.Value,b=to.Value;
                string? path=null;
                if(save) { using var dialog=new SaveFileDialog { Filter="CSV文件|*.csv",FileName=kind.Text+".csv" }; if(dialog.ShowDialog(this)!=DialogResult.OK)return; path=dialog.FileName; }
                var result=await Task.Run(()=>selected==2?数据.查实验备注(text,a,b,!save):数据.查记录(selected==1,text,a,b,!save));
                if(save) { await Task.Run(()=>本地数据库.导出表(result,path!)); 记("导出记录 "+result.Rows.Count+" 行"); status.Text="已导出 "+result.Rows.Count+" 行："+path; }
                else { grid.DataSource=result; status.Text=message+$"查询返回 {result.Rows.Count} 行（最多5000行）。"; }
            }
            catch(Exception e) { status.Text="操作失败："+e.Message; }
            finally { if(!IsDisposed)Busy(false); }
        }
        async Task ChangeNote(int operation)
        {
            通信.释放();
            try
            {
                if(!可写备注)throw new UnauthorizedAccessException("未建立有效的操作身份");
                long id=operation==0?0:grid.CurrentRow==null?throw new InvalidOperationException("请先选择一条实验备注"):Convert.ToInt64(grid.CurrentRow.Cells["编号"].Value);
                DateTime date=noteDate.Value.Date;string caption=title.Text,body=note.Text;
                if(operation==2 && MessageBox.Show(this,"删除所选实验备注？","确认删除",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                Busy(true);
                await Task.Run(()=>{if(operation==0)数据.新增实验备注(用户,date,caption,body);else if(operation==1)数据.修改实验备注(用户,id,date,caption,body);else 数据.删除实验备注(用户,id);});
                await Load(false,operation==2?"已删除备注。 ":"已保存备注。 ");
            }
            catch(Exception e){status.Text="操作失败："+e.Message;}
            finally{if(!IsDisposed)Busy(false);}
        }
        add.Click+=async(_,_)=>await ChangeNote(0);edit.Click+=async(_,_)=>await ChangeNote(1);delete.Click+=async(_,_)=>await ChangeNote(2);
        kind.SelectedIndexChanged+=async(_,_)=>{grid.DataSource=null;editor.Visible=kind.SelectedIndex==2;from.CustomFormat=to.CustomFormat=kind.SelectedIndex==2?"yyyy-MM-dd":"yyyy-MM-dd HH:mm:ss";keyword.PlaceholderText=kind.SelectedIndex==2?"标题 / 备注 / 记录人":"来源 / 用户 / 内容关键词";await Load(false);};
        grid.SelectionChanged+=(_,_)=>
        {
            if(kind.SelectedIndex!=2 || grid.CurrentRow==null || !grid.Columns.Contains("日期"))return;
            var row=grid.CurrentRow;noteDate.Value=DateTime.ParseExact(Convert.ToString(row.Cells["日期"].Value)!,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);title.Text=Convert.ToString(row.Cells["标题"].Value);note.Text=Convert.ToString(row.Cells["备注"].Value);
        };
        query.Click+=async(_,_)=>await Load(false); export.Click+=async(_,_)=>await Load(true);
        FlowLayoutPanel Group(string label,Control control,int width)
        {
            var group=new FlowLayoutPanel {Width=width,Height=36,WrapContents=false,Padding=Padding.Empty,Margin=new Padding(3)};
            group.Controls.Add(new Label {Text=label,AutoSize=true,Padding=new Padding(0,7,0,0)});group.Controls.Add(control);return group;
        }
        bar.Controls.AddRange([kind,Group("从",from,268),Group("至",to,268),Group("关键词",keyword,228),query,export,status]); page.Controls.Add(grid);page.Controls.Add(editor);page.Controls.Add(bar);
    }
    internal static string 诊断文本(状态快照 s)
    {
        // 字13为提示位，轴故障在字35/36。只拼接当前置位项，不把码表当作多个同时故障。
        var items=new List<string>(); string[] flags=["雨量无效","模式冲突","升降冲突","左屏超时","右PLC超时","存在未就绪轴（按轴检查）","PC超时","等待升降按钮释放"];
        for(int i=0;i<8;i++) if((s.字[13]&(1<<i))!=0) items.Add(flags[i]);
        if(s.标志(15))items.Add("软件停止锁定"); if(s.标志(9))items.Add("防夹全开锁定");
        if(s.字[35]!=0)items.Add($"雨刷故障0x{s.字[35]:X4}"); if(s.字[36]!=0)items.Add($"车窗故障0x{s.字[36]:X4}");
        return items.Count==0?"无当前报警":string.Join("；",items);
    }
    void 建诊断页(TabControl tabs)
    {
        var page=诊断页=new TabPage("连接诊断"); tabs.TabPages.Add(page);
        var text=诊断显示=new TextBox { Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),ScrollBars=ScrollBars.Vertical,BackColor=Color.White };
        var bar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=80,Padding=new Padding(10),AutoScroll=true };
        bar.Controls.Add(new Label { AutoSize=true,Text="软件停止后先复位，再申请控制；两轴分别显示当前故障码。" });
        界面计时器.Tick+=(_,_)=>更新诊断();
        tabs.SelectedIndexChanged+=(_,_)=>{if(tabs.SelectedTab==page)更新诊断(true);};
        page.Controls.Add(text);page.Controls.Add(bar);
    }
    private bool 更新诊断(bool 立即=false,long? 现在=null)
    {
        if(诊断页==null || 诊断显示==null || !诊断页.Visible)return false;
        long now=现在??Environment.TickCount64;
        if(!立即 && now-上次诊断刷新<1000)return false;
        上次诊断刷新=now;
        var s=当前状态;
        string value=s==null?"未连接或反馈已过期。\r\n"+(发布器.错误??""):
                $"运行模式：PLC在线\r\n当前提示：{诊断文本(s)}\r\n\r\n"+
                $"左PLC心跳：{s.字[3]}　右PLC心跳：{s.字[20]}　命令确认序号：{s.字[4]}\r\n"+
                $"PC心跳年龄：{s.字[22]*10} ms　右PLC心跳年龄：{s.字[23]*10} ms　左HMI年龄：{s.字[24]*10} ms\r\n"+
                $"一次确认：{s.字[5]}　模式确认：{s.字[27]}　复位确认：{s.字[6]}\r\n一次拒绝：{s.字[18]}　复位拒绝：{s.字[40]}　往返次数：{s.字[32]}\r\n\r\n"+
                $"逻辑钥匙：{s.标志(0)}　PC会话：{s.标志(4)}　运行许可：{s.标志(14)}　右PLC在线：{s.标志(3)}\r\n"+
                $"雨刷角度有效：{s.雨刷显示有效}　车窗角度有效：{s.车窗位置有效}\r\n质量位：0x{s.字[31]:X4}　握手位：0x{s.字[39]:X4}\r\n"+
                $"雨刷状态：{s.字[33]}　车窗状态：{s.字[34]}\r\n编码器状态：{s.字[37]} / {s.字[38]}（2=有效）\r\n双机读写返回码：{unchecked((short)s.字[26])} / {unchecked((short)s.字[25])}\r\n"+
                $"雨刷当前故障码：0x{s.字[35]:X4}　车窗当前故障码：0x{s.字[36]:X4}（0000=无）\r\n"+
                $"手操输入：0x{s.字[10]:X4}　手操输出：0x{s.字[15]:X4}\r\n\r\n"+
                $"以下为代码说明，不表示正在发生：\r\nF001工艺对象故障；F002反馈/相对基准丢失；F003运动超时；F004停止超时；\r\nF005非法参数；F006定位意外中断；F007控制面板接管；F008相对指令跨度异常。其他非零值按MC指令ErrorID核对。\r\n\r\n反馈文件：{(发布器.错误==null?"正常":发布器.错误)}";
        // 心跳变化会改变文本，但相同内容不重复赋值，避免光标和选区不停闪动。
        if(诊断显示.Text==value)return false;
        int start=诊断显示.SelectionStart,length=诊断显示.SelectionLength;
        诊断显示.Text=value;诊断写入次数++;
        诊断显示.Select(Math.Min(start,value.Length),Math.Min(length,Math.Max(0,value.Length-start)));
        return true;
    }
}
