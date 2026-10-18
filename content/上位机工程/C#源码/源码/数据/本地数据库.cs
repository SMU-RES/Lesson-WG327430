// 源码导读见 Main.cs。数据文件默认放在当前用户 LocalApplicationData/汽车综合实践。
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
/// SQLite持久化边界。每次操作创建并关闭自己的连接，允许记录队列和查询任务并行工作。
/// WAL模式减少查询与写入之间的等待；控制线程不等待磁盘，调用方在后台队列执行记录。
///
/// 用户通过固定课程账号登录；旧用户表保留以兼容既有数据。
/// 操作记录：记录申请、复位、挡位等用户动作；它不是电机已执行的证明。
/// 测量记录：来自PLC反馈，雨刷实际角度只在实测质量有效时写入；时间姿态不能冒充实测。
/// 实验备注：日期、标题、观察内容、记录人，与测量表分离，允许新增、修改和删除。
///
/// 所有外部文本通过SQL参数传入，不把关键词拼接成SQL条件。CSV导出对引号和公式开头转义。
/// </summary>
internal sealed class 本地数据库 : IDisposable
{
    private readonly string 连接串;
    public string 目录 { get; }
    public 本地数据库(string? 目录参数 = null)
    {
        目录 = 目录参数 ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "汽车综合实践");
        Directory.CreateDirectory(目录);
        连接串 = new SqliteConnectionStringBuilder { DataSource = Path.Combine(目录, "汽车实验.db") }.ToString();
        执行("CREATE TABLE IF NOT EXISTS 用户(名称 TEXT PRIMARY KEY, 盐 BLOB NOT NULL, 密码摘要 BLOB NOT NULL, 角色 TEXT NOT NULL);");
        执行("CREATE TABLE IF NOT EXISTS 场景(编号 INTEGER PRIMARY KEY AUTOINCREMENT, 名称 TEXT NOT NULL UNIQUE, 模式 INTEGER NOT NULL CHECK(模式 BETWEEN 0 AND 4), 雨量 INTEGER NOT NULL CHECK(雨量 BETWEEN 0 AND 1000));");
        执行("CREATE TABLE IF NOT EXISTS 操作记录(编号 INTEGER PRIMARY KEY, 时间 TEXT NOT NULL, 用户 TEXT NOT NULL, 内容 TEXT NOT NULL);");
        执行("CREATE TABLE IF NOT EXISTS 测量记录(编号 INTEGER PRIMARY KEY, 时间 TEXT NOT NULL, 雨刷角度 REAL, 车窗角度 REAL, 开度 INTEGER, 质量 INTEGER);");
        执行("CREATE TABLE IF NOT EXISTS 实验备注(编号 INTEGER PRIMARY KEY AUTOINCREMENT, 日期 TEXT NOT NULL, 标题 TEXT NOT NULL CHECK(length(trim(标题)) BETWEEN 1 AND 80), 备注 TEXT NOT NULL CHECK(length(备注) <= 2000), 记录人 TEXT NOT NULL);");
        执行("CREATE INDEX IF NOT EXISTS 实验备注_日期 ON 实验备注(日期);");
        执行("PRAGMA journal_mode=WAL;");
        // 保留来源列以识别历史记录；新记录由唯一 PLC 通信链产生。旧数据库原地迁移。
        try { 执行("ALTER TABLE 测量记录 ADD COLUMN 来源 TEXT NOT NULL DEFAULT '未标注旧数据'"); } catch (SqliteException e) when (e.Message.Contains("duplicate column")) { }
    }
    private SqliteConnection 打开() { var 连接 = new SqliteConnection(连接串); 连接.Open(); return 连接; }
    private void 执行(string 语句, params (string 名, object 值)[] 参数)
    {
        using var 连接 = 打开(); using var 命令 = 连接.CreateCommand(); 命令.CommandText = 语句;
        foreach (var 参数项 in 参数) 命令.Parameters.AddWithValue(参数项.名, 参数项.值);
        命令.ExecuteNonQuery();
    }
    public bool 已登录 { get; private set; }
    public string 当前操作者 { get; private set; } = "";
    public bool 已建立操作会话 => 当前操作者.Length > 0;

    // 固定课程身份仅决定本机记录权限，不向 PLC 发出控制申请，也不改写历史用户表。
    public bool 登录(string 用户名,string 密码)
    {
        已登录=string.Equals(用户名?.Trim(),"shmtu",StringComparison.Ordinal) && string.Equals(密码,"shmtu",StringComparison.Ordinal);
        当前操作者=已登录?"shmtu":"";
        return 已登录;
    }
    // 备注和日志只接受当前登录身份，防止查询窗口伪造其他记录人进行写入。
    public bool 可写实验备注(string 操作者)=>已建立操作会话 && string.Equals(操作者,当前操作者,StringComparison.Ordinal);
    public DataTable 查场景(string 关键字 = "")
    {
        using var 连 = 打开(); using var 命 = 连.CreateCommand(); 命.CommandText = "SELECT * FROM 场景 WHERE 名称 LIKE $词 ORDER BY 编号";
        命.Parameters.AddWithValue("$词", "%" + 关键字 + "%"); using var 读 = 命.ExecuteReader(); var 表 = new DataTable(); 表.Load(读); return 表;
    }
    public void 新增场景(string 名, int 模式, int 雨量) { 校验场景(名, 模式, 雨量); 执行("INSERT INTO 场景(名称,模式,雨量) VALUES($名,$模,$雨)", ("$名", 名), ("$模", 模式), ("$雨", 雨量)); }
    public void 修改场景(long 编号, string 名, int 模式, int 雨量) { 校验场景(名, 模式, 雨量); 执行("UPDATE 场景 SET 名称=$名,模式=$模,雨量=$雨 WHERE 编号=$号", ("$名", 名), ("$模", 模式), ("$雨", 雨量), ("$号", 编号)); }
    private static void 校验场景(string 名, int 模式, int 雨量)
    {
        if (string.IsNullOrWhiteSpace(名) || 名.Length > 60 || 模式 is < 0 or > 4 || 雨量 is < 0 or > 1000)
            throw new ArgumentException("场景名称须1至60字，模式0至4，雨量0至1000。");
    }
    public void 删除场景(long 编号) => 执行("DELETE FROM 场景 WHERE 编号=$号", ("$号", 编号));
    public DataTable 查实验备注(string 关键词, DateTime 起, DateTime 止, bool 限制 = true)
    {
        if (起.Date > 止.Date) throw new ArgumentException("开始日期不能晚于结束日期");
        return 查询("SELECT 编号,日期,标题,备注,记录人 FROM 实验备注 WHERE 日期 >= $起 AND 日期 <= $止 " +
            "AND (instr(标题,$词) > 0 OR instr(备注,$词) > 0 OR instr(记录人,$词) > 0) ORDER BY 日期 DESC,编号 DESC" +
            (限制 ? " LIMIT 5000" : ""), ("$起", 起.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$止", 止.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$词", 关键词 ?? ""));
    }
    public long 新增实验备注(string 操作者, DateTime 日期, string 标题, string 备注)
    {
        校验实验备注(标题, 备注);
        return 写实验备注(操作者, true,
            "INSERT INTO 实验备注(日期,标题,备注,记录人) VALUES($日,$题,$文,$人)",
            ("$日", 日期.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$题", 标题.Trim()),
            ("$文", 备注), ("$人", 操作者));
    }
    public void 修改实验备注(string 操作者, long 编号, DateTime 日期, string 标题, string 备注)
    {
        校验实验备注(标题, 备注);
        写实验备注(操作者, false,
            "UPDATE 实验备注 SET 日期=$日,标题=$题,备注=$文,记录人=$人 WHERE 编号=$号",
            ("$日", 日期.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$题", 标题.Trim()),
            ("$文", 备注), ("$人", 操作者), ("$号", 编号));
    }
    public void 删除实验备注(string 操作者, long 编号) =>
        写实验备注(操作者, false, "DELETE FROM 实验备注 WHERE 编号=$号", ("$号", 编号));
    private static void 校验实验备注(string 标题, string 备注)
    {
        if (string.IsNullOrWhiteSpace(标题) || 标题.Trim().Length > 80 || 备注 == null || 备注.Length > 2000)
            throw new ArgumentException("标题须1至80字，备注最多2000字。");
    }
    private long 写实验备注(string 操作者, bool 新增, string sql, params (string 名, object 值)[] 参数)
    {
        using var 连 = 打开();
        using var 事务 = 连.BeginTransaction();
        if (!可写实验备注(操作者)) throw new UnauthorizedAccessException("未建立有效的操作身份");
        using var 命令 = 连.CreateCommand();
        命令.Transaction = 事务;
        命令.CommandText = sql;
        foreach (var 参数项 in 参数) 命令.Parameters.AddWithValue(参数项.名, 参数项.值);
        if (命令.ExecuteNonQuery() != 1) throw new InvalidOperationException("指定的实验备注不存在，请刷新列表");
        long 编号 = 0;
        if (新增)
        {
            命令.CommandText = "SELECT last_insert_rowid()";
            命令.Parameters.Clear();
            编号 = Convert.ToInt64(命令.ExecuteScalar());
        }
        事务.Commit();
        return 编号;
    }
    public void 记操作(string 用户, string 内容) => 执行("INSERT INTO 操作记录(时间,用户,内容) VALUES($时,$人,$文)", ("$时", DateTimeOffset.Now.ToString("O")), ("$人", 用户), ("$文", 内容));
    // DBNull代表“当前没有有效实测值”，和真实0度有根本区别；不要为了图表连续而用0补齐。
    public void 记测量(状态快照 状态) => 执行("INSERT INTO 测量记录(时间,雨刷角度,车窗角度,开度,质量,来源) VALUES($时,$刷,$窗,$度,$质,$源)",
        ("$时", DateTimeOffset.Now.ToString("O")), ("$刷", 状态.雨刷位置有效 ? (object)状态.雨刷实际角度 : DBNull.Value),
        ("$窗", 状态.车窗位置有效 ? (object)状态.车窗实际角度 : DBNull.Value), ("$度", !状态.车窗开度有效 ? DBNull.Value : (object)状态.字[16]), ("$质", 状态.字[31]), ("$源", "PLC"));
    public void 导出(string 路径)
    {
        using var 连 = 打开(); using var 命 = 连.CreateCommand(); 命.CommandText = "SELECT 时间,雨刷角度,车窗角度,开度,质量 FROM 测量记录 ORDER BY 编号";
        using var 读 = 命.ExecuteReader(); using var 写 = new StreamWriter(路径, false, new UTF8Encoding(true));
        写.WriteLine("时间,雨刷实际角度,车窗实际角度,车窗开度,质量标志");
        while (读.Read()) { var 格 = new string[5]; for (int 号 = 0; 号 < 5; 号++) 格[号] = Convert.ToString(读[号], CultureInfo.InvariantCulture) ?? ""; 写.WriteLine(string.Join(",", 格)); }
    }
    public void Dispose() { }
    DataTable 查询(string sql, params (string 名,object 值)[] 参数)
    {
        using var con=打开(); using var cmd=con.CreateCommand(); cmd.CommandText=sql;
        foreach(var p in 参数) cmd.Parameters.AddWithValue(p.名,p.值);
        using var reader=cmd.ExecuteReader(); var table=new DataTable(); table.Load(reader); return table;
    }
    public DataTable 查记录(bool 操作,string 关键词,DateTime 起,DateTime 止,bool 限制=true) => 查询(
        操作?"SELECT * FROM 操作记录 WHERE 时间 >= $起 AND 时间 <= $止 AND (用户 LIKE $词 OR 内容 LIKE $词) ORDER BY 编号 DESC"+(限制?" LIMIT 5000":""):
        "SELECT * FROM 测量记录 WHERE 时间 >= $起 AND 时间 <= $止 AND 来源 LIKE $词 ORDER BY 编号 DESC"+(限制?" LIMIT 5000":""),
        ("$起",new DateTimeOffset(起).ToString("O")),("$止",new DateTimeOffset(止).ToString("O")),("$词","%"+关键词+"%"));
    public static void 导出表(DataTable 表,string 路径)
    {
        static string Cell(object? value) { var s=Convert.ToString(value,CultureInfo.InvariantCulture)??""; if(s.Length>0 && "=+-@\t\r".Contains(s[0]) && value is string) s="'"+s; return "\""+s.Replace("\"","\"\"")+"\""; }
        using var w=new StreamWriter(路径,false,new UTF8Encoding(true));
        w.WriteLine(string.Join(",",表.Columns.Cast<DataColumn>().Select(c=>Cell(c.ColumnName))));
        foreach(DataRow row in 表.Rows) w.WriteLine(string.Join(",",row.ItemArray.Select(Cell)));
    }
}




