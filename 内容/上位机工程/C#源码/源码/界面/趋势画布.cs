using System.Drawing.Drawing2D;
namespace 汽车综合实践;
internal sealed class 趋势画布 : Control
{
    private readonly Queue<(long 时刻, float? 雨刷, float? 车窗)> 采样 = new();
    public 趋势画布() { DoubleBuffered = true; BackColor = 主题.背景; }
    public void 断点() { 采样.Enqueue((Environment.TickCount64,null,null)); Invalidate(); }
    public void 加入(状态快照 状态)
    {
        long 现在 = Environment.TickCount64;
        采样.Enqueue((现在, 状态.雨刷显示有效 ? 状态.雨刷显示角度 : null, 状态.车窗位置有效 ? 状态.车窗实际角度 : null));
        while (采样.Count > 600 || (采样.Count > 0 && 现在 - 采样.Peek().时刻 > 60000)) 采样.Dequeue();
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(主题.背景);
        using var title=new Font("Microsoft YaHei UI",16,FontStyle.Bold);using var font=new Font("Microsoft YaHei UI",9);using var sub=new SolidBrush(主题.次要);using var ink=new SolidBrush(主题.文字);
        g.DrawString("角度趋势",title,ink,24,21);g.DrawString("最近 60 秒  /  无效反馈自动断点",font,sub,24,57);
        using var wp=new Pen(主题.青,2.5f);using var vp=new Pen(主题.绿,2.5f);using var gridPen=new Pen(主题.边框,1);
        float legend=Math.Max(280,Width-280);g.DrawLine(wp,legend,40,legend+24,40);g.DrawString("雨刷",font,ink,legend+32,31);g.DrawLine(vp,legend+140,40,legend+164,40);g.DrawString("车窗",font,ink,legend+172,31);
        var card=new Rectangle(18,91,Math.Max(40,Width-36),Math.Max(60,Height-115));using var cardPath=主题.圆角(card,14);using var fill=new SolidBrush(主题.卡片);g.FillPath(fill,cardPath);
        var plot=new Rectangle(card.Left+58,card.Top+28,Math.Max(30,card.Width-88),Math.Max(30,card.Height-75));
        foreach(int degree in new[]{-120,-60,0,60,120,180})
        {
            float y=plot.Bottom-(degree+130)/320f*plot.Height;g.DrawLine(gridPen,plot.Left,y,plot.Right,y);g.DrawString(degree+"°",font,sub,card.Left+10,y-8);
        }
        for(int sec=0;sec<=60;sec+=10){float x=plot.Left+sec/60f*plot.Width;g.DrawLine(gridPen,x,plot.Top,x,plot.Bottom);g.DrawString(sec==60?"现在":$"−{60-sec}s",font,sub,x-13,plot.Bottom+14);}
        var saved=g.Save();g.SetClip(plot);long now=Environment.TickCount64,last=0;PointF? previousW=null,previousV=null;
        foreach(var sample in 采样)
        {
            float x=plot.Right-(now-sample.时刻)/60000f*plot.Width;if(now-sample.时刻>60000)continue;
            if(last!=0&&sample.时刻-last>600){previousW=previousV=null;}
            PointF? w=sample.雨刷.HasValue?new PointF(x,plot.Bottom-(sample.雨刷.Value+130)/320*plot.Height):null;
            PointF? v=sample.车窗.HasValue?new PointF(x,plot.Bottom-(sample.车窗.Value+130)/320*plot.Height):null;
            if(w.HasValue&&previousW.HasValue)g.DrawLine(wp,previousW.Value,w.Value);if(v.HasValue&&previousV.HasValue)g.DrawLine(vp,previousV.Value,v.Value);
            previousW=w;previousV=v;last=sample.时刻;
        }
        g.Restore(saved);
        if(采样.Count==0)g.DrawString("等待有效反馈",font,sub,plot.Left+plot.Width/2-45,plot.Top+plot.Height/2);
    }
}
