using S7.Net;
namespace 汽车综合实践;

// 唯一网络边界。发行程序只使用真实 S7 传输；读写地址限制与界面外观无关。
/// <summary>
/// 传输接口是通信逻辑与 S7 库的边界。打开、读、写都支持取消，以便界面关闭或超时时中断等待。
/// 工厂默认只创建下面的 S7传输；隔离测试编译可以注入验证目录的内存实现。
/// 使用者从通信服务.循环开始阅读，再沿这里到 S7netplus 的 ReadBytesAsync/WriteBytesAsync。
/// </summary>
internal interface I传输 : IDisposable
{
    Task 打开(CancellationToken token);
    Task<byte[]> 读(int db, int offset, int count, CancellationToken token);
    Task 写(int db, int offset, byte[] bytes, CancellationToken token);
    void 关闭();
}
internal sealed class S7传输(string ip, int port = 102) : I传输
{
    // S7-1200 使用机架0、槽1。连接目标始终为PLC左；右PLC与驱动之间通信由PLC工程完成。
    private readonly Plc client = new(CpuType.S71200, ip, port, 0, 1) { ReadTimeout = 500, WriteTimeout = 500 };
    public Task 打开(CancellationToken token) => client.OpenAsync(token);
    public Task<byte[]> 读(int db, int offset, int count, CancellationToken token)
    {
        // 第二种读法专用于顺序锁复读头序号；不开放任意数据库地址读取接口给UI。
        if (db != 101 || !((offset == 0 && count == 128) || (offset == 4 && count == 2))) throw new InvalidOperationException("仅允许读DB101协议状态");
        return client.ReadBytesAsync(DataType.DataBlock, db, offset, count, token);
    }
    public Task 写(int db, int offset, byte[] bytes, CancellationToken token)
    {
        // 整帧64字节或提交序号2字节以外的写请求全部拒绝，防止界面代码越过PLC仲裁层。
        if (db != 100 || !((offset == 0 && bytes.Length == 64) || (offset == 4 && bytes.Length == 2))) throw new InvalidOperationException("仅允许写DB100命令分区");
        return client.WriteBytesAsync(DataType.DataBlock, db, offset, bytes, token);
    }
    public void 关闭() => client.Close();
    public void Dispose() => ((IDisposable)client).Dispose();
}

