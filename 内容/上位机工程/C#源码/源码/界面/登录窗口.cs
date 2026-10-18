using System.Drawing;
using System.Windows.Forms;
namespace 汽车综合实践;

/// <summary>
/// Main 创建数据库后显示本窗口；登录成功才进入主窗口，PLC 控制许可仍须另行申请。
/// 用户名预填课程身份，密码留空并使用系统密码遮蔽。
/// Enter由AcceptButton触发登录，Escape由CancelButton退出；成功才允许Main创建操作窗口。
/// 具体账号判断在本地数据库.登录，失败不建立会话。此窗口不连接PLC或发送电机命令。
/// </summary>
internal sealed class 登录窗口 : Form
{
    private readonly 本地数据库 数据;
    internal readonly TextBox 用户输入=new() {Name="用户名",Text="shmtu",Location=new Point(105,28),Width=260};
    internal readonly TextBox 密码输入=new() {Name="密码",UseSystemPasswordChar=true,Location=new Point(105,75),Width=260};
    internal readonly Label 登录提示=new() {Location=new Point(105,112),Size=new Size(260,22),ForeColor=Color.Firebrick};
    internal readonly 现代按钮 登录按钮=new() {Name="登录",Text="登录",Location=new Point(105,150),Size=new Size(124,38)};
    public 登录窗口(本地数据库 数据)
    {
        this.数据=数据;
        Font=new Font("Segoe UI",9F);Text="汽车控制系统 - 登录";ClientSize=new Size(395,212);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowIcon=false;StartPosition=FormStartPosition.CenterScreen;
        var 取消=new 现代按钮 {Text="取消",Location=new Point(241,150),Size=new Size(124,38),DialogResult=DialogResult.Cancel};
        Controls.AddRange(new Control[]{new Label {Text="用户名",Location=new Point(25,31),Size=new Size(74,24)},用户输入,new Label {Text="密码",Location=new Point(25,78),Size=new Size(74,24)},密码输入,登录提示,登录按钮,取消});
        登录按钮.Click+=(_,_)=>尝试登录();AcceptButton=登录按钮;CancelButton=取消;ActiveControl=密码输入;
        主题.应用(this);登录提示.ForeColor=Color.Firebrick;
    }
    internal bool 尝试登录()
    {
        if(!数据.登录(用户输入.Text,密码输入.Text)) {登录提示.Text="用户名或密码错误";密码输入.SelectAll();密码输入.Focus();return false;}
        登录提示.Text="";DialogResult=DialogResult.OK;return true;
    }
}
