using System.Text.Json;
using System.Threading.Channels;
namespace 汽车综合实践;
/// <summary>
/// 将最近一次状态异步写为JSON，便于诊断或外部只读观察；本模块没有PLC写入能力。
/// 容量为1的队列只保留最新状态，磁盘慢时丢旧快照而不积压控制UI。
/// 先写临时文件，再替换目标文件，避免读取者看见写到一半的JSON。
/// wiperEstimated仍保留内部来源信息；操作界面不额外显示该标记。
/// </summary>
internal sealed class 快照发布 : IAsyncDisposable
{
    readonly Channel<object> queue = Channel.CreateBounded<object>(new BoundedChannelOptions(1) { FullMode=BoundedChannelFullMode.DropOldest, SingleReader=true });
    readonly Task worker;
    public string? 错误 { get; private set; }
    public 快照发布(string directory)
    {
        worker=Task.Run(async()=>
        {
            await foreach(var snapshot in queue.Reader.ReadAllAsync())
            {
                try
                {
                    var path=Path.Combine(directory,"状态快照.json");
                    await File.WriteAllTextAsync(path+".tmp",JsonSerializer.Serialize(snapshot));
                    File.Move(path+".tmp",path,true); 错误=null;
                }
                catch(Exception e) { 错误=e.Message; }
            }
        });
    }
    public void 发布(状态快照? s, string alarm) => queue.Writer.TryWrite(new {
        timestampUtc=DateTimeOffset.UtcNow.ToString("O"), source="plc", connected=s!=null,
        wiperValid=s?.雨刷显示有效==true, windowValid=s?.车窗位置有效==true,
        wiperEstimated=s?.雨刷角度为估算==true,
        wiperAngleDegrees=s?.雨刷显示有效==true?(double?)s.雨刷显示角度:null,
        windowAngleDegrees=s?.车窗位置有效==true?(double?)s.车窗实际角度:null,
        windowPercent=s?.车窗开度有效==true?(double?)s.字[16]:null,
        mode=s==null?"无有效状态":new[]{"关","自动","小","中","大"}[s.字[7]], alarm
    });
    public async ValueTask DisposeAsync() { queue.Writer.TryComplete(); await worker.ConfigureAwait(false); }
}




