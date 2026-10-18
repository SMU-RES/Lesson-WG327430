using System.Drawing.Drawing2D;
using System.Drawing.Text;
namespace 汽车综合实践;

// 每次绘制只使用同一状态快照；不插值、不按时钟推进电机位置。
// 车头和车门使用固定车辆外观；四项数值由共用的原生状态栏显示。
// 几何复杂度只影响显示；数据质量、反馈过期和电机控制不在绘图方法中处理。
internal sealed class 实际反馈画布 : Control
{
    public 状态快照? 反馈;
    static readonly Color 背景 = Color.FromArgb(239,246,252), 卡片 = Color.White, 边线 = Color.FromArgb(177,198,218);
    static readonly Color 文字 = Color.FromArgb(27,49,72), 次要 = Color.FromArgb(80,103,124), 青 = Color.FromArgb(0,102,170), 绿 = Color.FromArgb(25,122,69);
    public 实际反馈画布() { DoubleBuffered=true; ResizeRedraw=true; BackColor=背景; }
    internal (RectangleF Front,RectangleF Door) 布局几何()
    {
        float width=Width,height=Height;
        float dataTop=Math.Max(145,height-10),frontWidth=(width-36)*.55f;
        return (new(12,56,frontWidth,dataTop-68),new(24+frontWidth,56,width-36-frontWidth,dataTop-68));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(Width<100||Height<180)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        // 数值文字使用独立像素字号，不随机构绘图的几何缩放一起缩小。
        float width=Width,height=Height;
        bool w=反馈?.雨刷显示有效==true,v=反馈?.车窗位置有效==true,p=反馈?.车窗开度有效==true;
        var layout=布局几何();
        DrawText(g,"车辆实时状态",21,文字,16,13,true);
        DrawStage(g,layout.Front,"前挡风玻璃 · 雨刷",w,()=>DrawFront(g,w),new(48,145,395,249));
        DrawStage(g,layout.Door,"车门 · 升降玻璃",v,()=>DrawDoor(g,v,p),new(490,150,280,242));
    }

    void DrawStage(Graphics g,RectangleF area,string title,bool valid,Action draw,RectangleF source)
    {
        Card(g,area,Color.FromArgb(250,252,255));
        TextIn(g,valid?title:title+" · 无反馈",14,valid?文字:Color.Firebrick,new(area.X+12,area.Y+11,area.Width-24,23),true);
        var image=new RectangleF(area.X+10,area.Y+42,area.Width-20,Math.Max(45,area.Height-54));
        float scale=Math.Min(image.Width/source.Width,image.Height/source.Height);
        var saved=g.Save();g.SetClip(image);
        g.TranslateTransform(image.X+(image.Width-source.Width*scale)/2,image.Y+(image.Height-source.Height*scale)/2);
        g.ScaleTransform(scale,scale);g.TranslateTransform(-source.X,-source.Y);draw();g.Restore(saved);
    }
    void DrawFront(Graphics g,bool valid)
    {
        // 车头正视图，风挡是倾斜梯形；双雨刷在玻璃范围内摆动。
        using(var shadow=new SolidBrush(Color.FromArgb(24,50,70,90)))g.FillEllipse(shadow,63,367,364,24);
        using var body=new GraphicsPath();
        body.StartFigure();body.AddBezier(90,358,64,350,63,309,77,286);body.AddLine(77,286,112,173);body.AddBezier(112,173,118,145,363,145,375,173);body.AddLine(375,173,414,286);body.AddBezier(414,286,430,318,422,353,398,359);body.AddBezier(398,359,298,372,188,372,90,358);body.CloseFigure();
        using(var fill=new LinearGradientBrush(new Rectangle(70,150,352,220),Color.FromArgb(202,218,232),Color.FromArgb(157,181,202),90))g.FillPath(fill,body);
        using(var outline=new Pen(Color.FromArgb(111,137,158),2))g.DrawPath(outline,body);
        using(var roof=new Pen(Color.FromArgb(174,198,216),4))g.DrawBezier(roof,128,167,180,154,310,154,363,167);
        using var glass=new GraphicsPath();
        glass.AddBezier(137,179,187,168,302,168,350,179);glass.AddLine(350,179,387,272);glass.AddBezier(387,272,290,284,190,284,100,272);glass.CloseFigure();
        using(var fill=new LinearGradientBrush(new Rectangle(100,174,287,107),Color.FromArgb(208,233,246),Color.FromArgb(162,199,223),90))g.FillPath(fill,glass);
        using(var pen=new Pen(Color.FromArgb(109,136,155),7))g.DrawPath(pen,glass);
        var save=g.Save();g.SetClip(glass);
        using(var reflect=new SolidBrush(Color.FromArgb(21,153,218,255)))g.FillPolygon(reflect,new PointF[] {new(173,159),new(212,159),new(140,299),new(101,299)});
        using(var reflect=new SolidBrush(Color.FromArgb(13,153,218,255)))g.FillPolygon(reflect,new PointF[] {new(312,159),new(327,159),new(257,299),new(242,299)});
        int rain=反馈==null?0:反馈.字[11];
        if(rain>0){using var drop=new Pen(Color.FromArgb(70,125,198,232),1.3f);for(int i=0;i<Math.Min(42,6+rain/25);i++){float x=112+(i*47)%266,y=184+(i*23)%80;g.DrawLine(drop,x,y,x-3,y+9);}}
        if(valid)
        {
            float angle=反馈!.雨刷显示角度/3f;
            DrawWiper(g,new(177,274),angle,84);
            DrawWiper(g,new(305,274),angle,84);
        }
        g.Restore(save);
        using(var trim=new Pen(Color.FromArgb(113,156,183),1.5f))g.DrawBezier(trim,103,287,204,300,295,300,389,287);
        using(var bonnet=new Pen(Color.FromArgb(144,171,192),2)){g.DrawLine(bonnet,137,295,122,332);g.DrawLine(bonnet,352,295,366,332);}
        Round(g,new(84,319,67,19),8,Color.FromArgb(188,222,236));Round(g,new(341,319,67,19),8,Color.FromArgb(188,222,236));
        using(var lamp=new Pen(Color.FromArgb(225,249,254),3)){g.DrawLine(lamp,91,325,144,328);g.DrawLine(lamp,348,328,400,325);}
        Round(g,new(176,330,139,21),8,Color.FromArgb(103,122,139));
        using(var grille=new Pen(Color.FromArgb(166,184,199),1)){for(int y=334;y<350;y+=4)g.DrawLine(grille,184,y,305,y);}
        using(var badge=new Pen(青,2))g.DrawEllipse(badge,236,312,18,12);
        Round(g,new(225,355,41,11),3,Color.FromArgb(104,131,149));
    }
    static void DrawWiper(Graphics g,PointF pivot,float degree,float length)
    {
        double angle=degree*Math.PI/180;
        var tip=new PointF(pivot.X+(float)Math.Sin(angle)*length,pivot.Y-(float)Math.Cos(angle)*length);
        using(var sweep=new Pen(Color.FromArgb(25,56,189,248),17)){sweep.StartCap=LineCap.Round;sweep.EndCap=LineCap.Round;g.DrawLine(sweep,pivot,tip);}
        using(var arm=new Pen(Color.FromArgb(47,57,67),5)){arm.StartCap=arm.EndCap=LineCap.Round;g.DrawLine(arm,pivot,tip);}
        using(var trim=new Pen(Color.FromArgb(105,148,166),1.2f))g.DrawLine(trim,new PointF(pivot.X-1,pivot.Y),new PointF(tip.X-1,tip.Y));
        var a=new PointF(tip.X+(float)Math.Sin(angle)*12,tip.Y-(float)Math.Cos(angle)*12);
        var b=new PointF(tip.X-(float)Math.Sin(angle)*25,tip.Y+(float)Math.Cos(angle)*25);
        using(var blade=new Pen(Color.FromArgb(38,48,59),8)){blade.StartCap=blade.EndCap=LineCap.Round;g.DrawLine(blade,a,b);}
        using(var line=new Pen(Color.FromArgb(101,163,181),1))g.DrawLine(line,a,b);
        using(var cap=new SolidBrush(Color.FromArgb(76,106,124)))g.FillEllipse(cap,pivot.X-4,pivot.Y-4,8,8);
    }
    void DrawDoor(Graphics g,bool valid,bool percentValid)
    {
        using(var shadow=new SolidBrush(Color.FromArgb(24,50,70,90)))g.FillEllipse(shadow,503,370,258,17);
        using var door=new GraphicsPath();
        door.AddBezier(514,259,529,217,550,180,569,166);door.AddBezier(569,166,600,156,703,155,733,163);door.AddLine(733,163,754,349);door.AddBezier(754,349,754,367,534,373,520,352);door.CloseFigure();
        using(var paint=new LinearGradientBrush(new Rectangle(510,158,245,215),Color.FromArgb(202,218,232),Color.FromArgb(168,190,210),90))g.FillPath(paint,door);
        using(var pen=new Pen(Color.FromArgb(119,145,165),2))g.DrawPath(pen,door);
        using var opening=new GraphicsPath();opening.AddLine(532,259,576,179);opening.AddBezier(576,179,613,173,689,173,720,178);opening.AddLine(720,178,731,259);opening.CloseFigure();
        using(var dark=new SolidBrush(Color.FromArgb(161,180,195)))g.FillPath(dark,opening);
        // 位置有效才画玻璃；失效时不以默认0%冒充全关。
        if(valid)
        {
            float ratio=Math.Clamp(反馈!.车窗实际角度/180f,0,1),top=176+83*ratio;
            var state=g.Save();g.SetClip(opening);
            using(var glass=new LinearGradientBrush(new Rectangle(533,174,200,88),Color.FromArgb(214,238,248),Color.FromArgb(166,205,225),90))g.FillRectangle(glass,529,top,205,264-top);
            g.SetClip(new RectangleF(529,top,205,264-top),CombineMode.Intersect);
            using(var r=new SolidBrush(Color.FromArgb(36,191,235,248)))g.FillPolygon(r,new PointF[] {new(614,173),new(635,173),new(582,276),new(563,276)});
            using(var edge=new Pen(Color.FromArgb(169,221,239),2.2f))g.DrawLine(edge,530,top,736,top);
            g.Restore(state);
        }
        using(var trim=new Pen(Color.FromArgb(114,151,176),2.5f))g.DrawPath(trim,opening);
        using(var trim=new Pen(Color.FromArgb(106,132,151),5))g.DrawLine(trim,525,268,738,268);
        Round(g,new(687,283,42,8),3,Color.FromArgb(124,159,182));
        using(var panel=new Pen(Color.FromArgb(134,160,181),1.5f))g.DrawBezier(panel,534,301,597,313,679,315,739,295);
        using(var lower=new Pen(Color.FromArgb(123,149,169),4))g.DrawLine(lower,544,345,733,345);
        using(var mirror=new GraphicsPath()){mirror.AddBezier(526,246,503,229,497,248,510,257);mirror.AddLine(510,257,532,257);mirror.CloseFigure();using var b=new SolidBrush(Color.FromArgb(165,191,211));g.FillPath(b,mirror);}
    }
    static void Card(Graphics g,RectangleF r,Color? fill=null)
    {
        Round(g,r,6,fill??卡片);using var p=Path(r,6);using var pen=new Pen(边线,1);g.DrawPath(pen,p);
    }
    static void TextIn(Graphics g,string text,float size,Color color,RectangleF bounds,bool bold=false,StringAlignment align=StringAlignment.Near)
    {using var font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);using var brush=new SolidBrush(color);using var format=new StringFormat {Alignment=align,LineAlignment=StringAlignment.Near,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};g.DrawString(text,font,brush,bounds,format);}
    static void Pill(Graphics g,string value,Color c,float x,float y,float width){Round(g,new(x,y,width,32),16,Color.FromArgb(25,c));using var b=new SolidBrush(c);g.FillEllipse(b,x+13,y+12,7,7);DrawText(g,value,12,c,x+27,y+6,true);}
    static void DrawText(Graphics g,string text,float size,Color c,float x,float y,bool bold=false)
    { using var f=new Font("Microsoft YaHei UI",size, bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);using var b=new SolidBrush(c);g.DrawString(text,f,b,x,y,StringFormat.GenericTypographic); }
    static void Round(Graphics g,RectangleF r,float radius,Color c){using var p=Path(r,radius);using var b=new SolidBrush(c);g.FillPath(b,p);}
    static GraphicsPath Path(RectangleF r,float radius){float d=radius*2;var p=new GraphicsPath();p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
}
