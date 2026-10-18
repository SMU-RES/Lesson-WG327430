namespace AutomotiveGpu;

public enum DetailLevel { Minimal, Compatibility }

/// <summary>
/// 三维显示负载策略。阅读入口：OpenGl.GlView.OnHandleCreated → Select → ReportWindow。
/// 静态车身固定，只根据实际渲染器和连续低帧率调整几何细度及像素上限。
/// 软件OpenGL最多20 FPS；显卡渲染最多30 FPS。低帧时先降车轮分段，再缩小渲染像素。
/// 每连续三个低帧率统计窗口才降一次，避免窗口拖动造成瞬时抖动；最小化/后台时间不计入降级。
/// </summary>
public sealed class QualityPolicy
{
    public const int TargetFps=30;
    int slowWindows;
    public DetailLevel Level { get; private set; }=DetailLevel.Compatibility;
    public bool ReducedResolution { get; private set; }
    public bool Software { get; private set; }
    public int AutomaticChanges { get; private set; }
    public string DetailName => Level==DetailLevel.Compatibility?"兼容":"极简";
    public int FrameRateLimit => Software?20:TargetFps;
    public void Select(bool software)
    {
        Software=software;slowWindows=0;ReducedResolution=false;AutomaticChanges=0;
        // 软件渲染从六边形车轮起步，硬件渲染从八边形起步；车身和活动机构坐标相同。
        Level=software?DetailLevel.Minimal:DetailLevel.Compatibility;
    }
    public bool ReportWindow(double fps,bool foreground)
    {
        if(!foreground || !double.IsFinite(fps) || fps<0) {slowWindows=0;return false;}
        if(fps>=FrameRateLimit*.8) {slowWindows=0;return false;}
        if(++slowWindows<3)return false;
        slowWindows=0;
        if(Level>DetailLevel.Minimal)Level--;
        else if(!ReducedResolution)ReducedResolution=true;
        else return false;
        AutomaticChanges++;return true;
    }
    public (int Width,int Height) PixelLimit => ReducedResolution?(480,320):Level==DetailLevel.Minimal?(640,420):(720,460);
}
