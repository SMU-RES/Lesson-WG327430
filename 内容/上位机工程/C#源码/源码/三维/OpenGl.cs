using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutomotiveGpu;

/// <summary>
/// Windows 原生 OpenGL 1.1 / WGL 函数声明层，只调用系统 opengl32.dll，不要求新版显卡着色器。
/// 初读者可先跳过这里，直接从下面的 GlView 构造函数、ApplyQuality 和 OnPaint 阅读。
/// </summary>
internal static class Gl
{
    const string Lib = "opengl32.dll";
    [DllImport(Lib)] public static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport(Lib)] public static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);
    [DllImport(Lib)] public static extern bool wglDeleteContext(IntPtr context);
    [DllImport(Lib)] public static extern IntPtr wglGetProcAddress(string name);
    [DllImport(Lib)] public static extern IntPtr glGetString(uint name);
    [DllImport(Lib)] public static extern void glClearColor(float r,float g,float b,float a);
    [DllImport(Lib)] public static extern void glClear(uint mask);
    [DllImport(Lib)] public static extern void glViewport(int x,int y,int width,int height);
    [DllImport(Lib)] public static extern void glEnable(uint value);
    [DllImport(Lib)] public static extern void glDisable(uint value);
    [DllImport(Lib)] public static extern void glDepthFunc(uint value);
    [DllImport(Lib)] public static extern void glDepthMask(byte value);
    [DllImport(Lib)] public static extern void glBlendFunc(uint a,uint b);
    [DllImport(Lib)] public static extern void glMatrixMode(uint value);
    [DllImport(Lib)] public static extern void glLoadIdentity();
    [DllImport(Lib)] public static extern void glPushMatrix();
    [DllImport(Lib)] public static extern void glPopMatrix();
    [DllImport(Lib)] public static extern void glMultMatrixd(double[] matrix);
    [DllImport(Lib)] public static extern void glTranslated(double x,double y,double z);
    [DllImport(Lib)] public static extern void glScaled(double x,double y,double z);
    [DllImport(Lib)] public static extern void glFrustum(double left,double right,double bottom,double top,double near,double far);
    [DllImport(Lib)] public static extern void glLightfv(uint light,uint property,float[] values);
    [DllImport(Lib)] public static extern void glLightModelfv(uint property,float[] values);
    [DllImport(Lib)] public static extern void glLightModeli(uint property,int value);
    [DllImport(Lib)] public static extern void glMaterialfv(uint face,uint property,float[] values);
    [DllImport(Lib)] public static extern void glMaterialf(uint face,uint property,float value);
    [DllImport(Lib)] public static extern void glColorMaterial(uint face,uint mode);
    [DllImport(Lib)] public static extern void glShadeModel(uint mode);
    [DllImport(Lib)] public static extern void glBegin(uint mode);
    [DllImport(Lib)] public static extern void glEnd();
    [DllImport(Lib)] public static extern void glVertex3d(double x,double y,double z);
    [DllImport(Lib)] public static extern void glNormal3d(double x,double y,double z);
    [DllImport(Lib)] public static extern void glColor4ub(byte r,byte g,byte b,byte a);
    [DllImport(Lib)] public static extern uint glGenLists(int count);
    [DllImport(Lib)] public static extern void glNewList(uint list,uint mode);
    [DllImport(Lib)] public static extern void glEndList();
    [DllImport(Lib)] public static extern void glCallList(uint list);
    [DllImport(Lib)] public static extern void glDeleteLists(uint list,int count);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr handle);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr handle,IntPtr dc);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr dc,ref PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr dc,int format,ref PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr dc);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate bool SwapInterval(int interval);
    [StructLayout(LayoutKind.Sequential)] public struct PixelFormatDescriptor
    {
        public ushort Size,Version;
        public uint Flags;
        public byte PixelType,ColorBits,RedBits,RedShift,GreenBits,GreenShift,BlueBits,BlueShift,AlphaBits,AlphaShift,AccumBits,AccumRedBits,AccumGreenBits,AccumBlueBits,AccumAlphaBits,DepthBits,StencilBits,AuxBuffers,LayerType,Reserved;
        public uint LayerMask,VisibleMask,DamageMask;
    }
    public static void Color(Color c, byte? alpha=null) => glColor4ub(c.R,c.G,c.B,alpha ?? c.A);
    public static void Vertex(Vec3 p) => glVertex3d(p.X,p.Y,p.Z);
    public static void Normal(Vec3 p) => glNormal3d(p.X,p.Y,p.Z);
}

/// <summary>
/// 上位机内嵌三维控件。阅读路线：Main 创建操作窗口 → 创建本控件并接收状态 →
/// OnHandleCreated 建立 WGL 上下文 → ApplyQuality 缓存静态车身 → OnPaint 绘制车身和两个活动机构。
/// 状态来自 SnapshotReader，绘图不写 PLC、不更改电机速度，也不把渲染帧率当成控制周期。
/// </summary>
public sealed class GlView : Control
{
    readonly SnapshotReader state;
    // 定时器只是唤醒绘制。真实帧间隔由 Stopwatch 截止时间控制，避免旧 Windows 的计时粒度造成突发连画。
    readonly System.Windows.Forms.Timer frames = new() { Interval = 10 };
    readonly Stopwatch fpsClock = Stopwatch.StartNew();
    readonly double[] cameraMatrix = new double[16], beamMatrix = new double[16];
    readonly double[] renderTimes=new double[64],sortedTimes=new double[64];
    // 只缓存当前车身的两种车轮细度；降级时复用已建网格，不在每一帧重新生成。
    readonly uint[] bodyLists=new uint[2];
    readonly int[] bodyTriangles=new int[2];
    public QualityPolicy Quality { get; }=new();
    static readonly float[] KeyPosition={-3,5,4,0},NoSpecular={0,0,0,1};
    static readonly double[] WiperZ={-.39,.32};
    IntPtr dc, context;
    uint bodyList, cylinderList, sphereList;
    Point? drag;
    int frameCount,renderTimeCount;
    bool period;
    long lastRenderedTicks,nextRenderTicks;
    double yaw=2.48, pitch=.28, distance=6.0;
    public string Renderer { get; private set; } = "正在初始化 OpenGL…";
    public string Version { get; private set; } = "";
    public string? Failure { get; private set; }
    public bool SoftwareRenderer { get; private set; }
    public bool RenderingPaused { get; private set; }
    public bool RenderingEnabled { get; private set; } = true;
    public double Fps { get; private set; }
    public double P95FrameMs { get; private set; }
    public int RenderWidth { get; private set; }
    public int RenderHeight { get; private set; }
    public int TriangleCount { get; private set; }
    public long RenderedFrames { get; private set; }
    public event Action<double,double>? FrameRendered;
    public GlView(SnapshotReader state)
    {
        this.state=state;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.Opaque,true);
        BackColor=Color.FromArgb(239,246,252); Cursor=Cursors.SizeAll; TabStop=true;
        frames.Tick += (_,_) =>
        {
            var form=FindForm();bool active=RenderingEnabled&&Visible&&form!=null&&form.WindowState!=FormWindowState.Minimized&&Gl.GetForegroundWindow()==form.Handle;
            if(!active) {SetTimerPrecision(false);if(!RenderingPaused){RenderingPaused=true;Fps=0;frameCount=0;renderTimeCount=0;fpsClock.Restart();Quality.ReportWindow(0,false);}return;}
            SetTimerPrecision(true);
            if(RenderingPaused){RenderingPaused=false;fpsClock.Restart();frameCount=0;renderTimeCount=0;lastRenderedTicks=0;nextRenderTicks=0;}
            Invalidate();
        };
        MouseDown += (_,e) => { if(e.Button==MouseButtons.Left) { drag=e.Location; Capture=true; Focus(); } };
        MouseMove += (_,e) => { if(drag is not { } p)return; yaw-=(e.X-p.X)*.008; pitch=Math.Clamp(pitch+(e.Y-p.Y)*.006,.06,1.25); drag=e.Location; };
        MouseUp += (_,_) => { drag=null; Capture=false; };
        MouseCaptureChanged += (_,_) => { if(!Capture)drag=null; };
        MouseWheel += (_,e) => { distance=Math.Clamp(distance-e.Delta*.0038,5.1,13); };
    }
    protected override CreateParams CreateParams { get { var cp=base.CreateParams; cp.ClassStyle|=0x20; return cp; } }
    protected override void OnHandleCreated(EventArgs e)
    {
        // WinForms 句柄准备好后才申请 DC、选择像素格式并建立 WGL。像素格式对同一窗口只设置一次。
        base.OnHandleCreated(e);
        try
        {
            Failure=null; nextRenderTicks=lastRenderedTicks=0;
            dc=Gl.GetDC(Handle);
            var pfd=new Gl.PixelFormatDescriptor { Size=(ushort)Marshal.SizeOf<Gl.PixelFormatDescriptor>(),Version=1,Flags=0x00000004|0x00000020|0x00000001,PixelType=0,ColorBits=32,DepthBits=24,StencilBits=8,LayerType=0 };
            int pixel=Gl.ChoosePixelFormat(dc,ref pfd);
            if(pixel==0 || !Gl.SetPixelFormat(dc,pixel,ref pfd))throw new InvalidOperationException("无法设置 OpenGL 像素格式");
            context=Gl.wglCreateContext(dc);
            if(context==IntPtr.Zero || !Gl.wglMakeCurrent(dc,context))throw new InvalidOperationException("无法创建 OpenGL 上下文");
            Renderer=Marshal.PtrToStringAnsi(Gl.glGetString(0x1F01)) ?? "未知渲染器";
            Version=Marshal.PtrToStringAnsi(Gl.glGetString(0x1F02)) ?? "";
            SoftwareRenderer=Renderer.Contains("GDI Generic",StringComparison.OrdinalIgnoreCase)||Renderer.Contains("llvmpipe",StringComparison.OrdinalIgnoreCase)||Renderer.Contains("SwiftShader",StringComparison.OrdinalIgnoreCase);
            var swap=Gl.wglGetProcAddress("wglSwapIntervalEXT");
            if(swap!=IntPtr.Zero && swap.ToInt64() is not 1 and not 2 and not 3 and not -1)Marshal.GetDelegateForFunctionPointer<Gl.SwapInterval>(swap)(0);
            Gl.glEnable(0x0B71); Gl.glDepthFunc(0x0203); Gl.glEnable(0x0BA1); Gl.glShadeModel(0x1D01);
            Gl.glEnable(0x0B50); Gl.glEnable(0x4000); Gl.glDisable(0x4001); Gl.glDisable(0x4002);
            Gl.glLightModelfv(0x0B53,new float[]{.42f,.44f,.46f,1}); Gl.glLightModeli(0x0B52,1);
            Gl.glColorMaterial(0x0408,0x1602); Gl.glEnable(0x0B57);
            // 使用一盏主光和环境光；关闭镜面高光与额外光源，降低软件OpenGL负载。
            Gl.glLightfv(0x4000,0x1201,new float[]{.66f,.68f,.70f,1});
            Quality.Select(SoftwareRenderer);ApplyQuality();
            cylinderList=CompileUnitCylinder();
            var sphere=new Scene();sphere.Meshes.Add(new Mesh{Positions=new(){new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)},Indices=new(){0,2,4,4,2,1,1,2,5,5,2,0,0,4,3,4,1,3,1,5,3,5,0,3}});sphereList=Compile(sphere,false);
            fpsClock.Restart(); if(RenderingEnabled)frames.Start();
        }
        catch(Exception ex) { Failure=ex.Message; Renderer="OpenGL 初始化失败"; }
    }
    public void SetRenderingEnabled(bool enabled)
    {
        RenderingEnabled=enabled;
        if(!enabled) { frames.Stop(); SetTimerPrecision(false); RenderingPaused=true; Fps=0; frameCount=0; renderTimeCount=0; }
        else if(context!=IntPtr.Zero) { nextRenderTicks=0; frames.Start(); Invalidate(); }
    }
    void SetTimerPrecision(bool enabled)
    {
        if(enabled && !period)period=Gl.timeBeginPeriod(1)==0;
        else if(!enabled && period){Gl.timeEndPeriod(1);period=false;}
    }
    void ApplyQuality()
    {
        int cacheIndex=(int)Quality.Level;
        if(bodyLists[cacheIndex]==0)
        {
            Scene car=LowPolyCar.Build(Quality.Level==DetailLevel.Minimal);
            bodyTriangles[cacheIndex]=car.Meshes.Sum(m=>m.Indices.Count/3);bodyLists[cacheIndex]=Compile(car);
        }
        bodyList=bodyLists[cacheIndex];TriangleCount=bodyTriangles[cacheIndex];
        Gl.glDisable(0x4001);Gl.glDisable(0x4002);
    }
    uint CompileUnitCylinder()
    {
        // 四边形截面的单位杆只生成一次；每根雨刷通过矩阵复用，动态机构最多增加116个三角形。
        uint list=Gl.glGenLists(1);Gl.glNewList(list,0x1300);Gl.glBegin(0x0004);
        for(int i=0;i<4;i++)
        {
            double a=i*Math.PI/2,b=(i+1)*Math.PI/2,ax=Math.Cos(a),ay=Math.Sin(a),bx=Math.Cos(b),by=Math.Sin(b);
            Gl.glNormal3d(ax,ay,0);Gl.glVertex3d(ax,ay,0);Gl.glVertex3d(ax,ay,1);Gl.glNormal3d(bx,by,0);Gl.glVertex3d(bx,by,0);
            Gl.glVertex3d(bx,by,0);Gl.glNormal3d(ax,ay,0);Gl.glVertex3d(ax,ay,1);Gl.glNormal3d(bx,by,0);Gl.glVertex3d(bx,by,1);
            Gl.glNormal3d(0,0,1);Gl.glVertex3d(0,0,1);Gl.glVertex3d(ax,ay,1);Gl.glVertex3d(bx,by,1);
            Gl.glNormal3d(0,0,-1);Gl.glVertex3d(0,0,0);Gl.glVertex3d(bx,by,0);Gl.glVertex3d(ax,ay,0);
        }
        Gl.glEnd();Gl.glEndList();return list;
    }
    uint Compile(Scene scene,bool materials=true)
    {
        // OpenGL 1.1 显示列表把顶点、法线、颜色记在驱动中。静态车身每帧只需 glCallList。
        uint list=Gl.glGenLists(1); if(list==0)throw new InvalidOperationException("显卡无法分配静态网格");
        Gl.glNewList(list,0x1300);
        foreach(var mesh in scene.Meshes)
        {
            if(materials)
            {
                Gl.Color(mesh.Color,255);
                Gl.glMaterialfv(0x0408,0x1202,NoSpecular);
                Gl.glMaterialf(0x0408,0x1601,0);
            }
            Gl.glBegin(0x0004);
            for(int i=0;i<mesh.Indices.Count;i+=3)
            {
                int a=mesh.Indices[i],b=mesh.Indices[i+1],c=mesh.Indices[i+2];
                if(mesh.Normals.Count!=mesh.Positions.Count)
                {
                    Vec3 p=mesh.Positions[a],q=mesh.Positions[b],r=mesh.Positions[c];
                    var n=Cross(q-p,r-p); Gl.Normal(Unit(n));
                }
                foreach(int index in new[]{a,b,c}) { if(mesh.Normals.Count==mesh.Positions.Count)Gl.Normal(mesh.Normals[index]); Gl.Vertex(mesh.Positions[index]); }
            }
            Gl.glEnd();
        }
        Gl.glEndList(); return list;
    }
    protected override void OnPaintBackground(PaintEventArgs pevent) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        if(!RenderingEnabled)return;
        if(Failure!=null || context==IntPtr.Zero)
        {
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics,Failure ?? Renderer,Font,ClientRectangle,Color.FromArgb(251,191,36),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);return;
        }
        long started=Stopwatch.GetTimestamp();
        if(started<nextRenderTicks)return;
        long framePeriod=Stopwatch.Frequency/Quality.FrameRateLimit;
        nextRenderTicks=nextRenderTicks==0?started+framePeriod:nextRenderTicks+framePeriod;
        // 窗口调整或调试停顿后丢弃过期帧，不补画一串历史画面。PLC 通信继续按自己的周期工作。
        if(nextRenderTicks<=started)nextRenderTicks=started+framePeriod;
        double frameInterval=lastRenderedTicks==0?0:(started-lastRenderedTicks)*1000.0/Stopwatch.Frequency;
        lastRenderedTicks=started;
        Gl.wglMakeCurrent(dc,context);
        int w=Math.Max(1,ClientSize.Width),h=Math.Max(1,ClientSize.Height);
        Gl.glViewport(0,0,w,h); Gl.glClearColor(.937f,.965f,.988f,1); Gl.glClear(0x00004000|0x00000100);
        if(!SoftwareRenderer)BackgroundGradient();
        var cap=Quality.PixelLimit;double scale=Math.Min(1,Math.Min((double)cap.Width/w,(double)cap.Height/h));
        RenderWidth=Math.Max(1,(int)(w*scale));RenderHeight=Math.Max(1,(int)(h*scale));Gl.glViewport((w-RenderWidth)/2,(h-RenderHeight)/2,RenderWidth,RenderHeight);
        Gl.glMatrixMode(0x1701); Gl.glLoadIdentity(); double top=.1*Math.Tan(42*Math.PI/360); Gl.glFrustum(-top*RenderWidth/RenderHeight,top*RenderWidth/RenderHeight,-top,top,.1,80);
        Gl.glMatrixMode(0x1700); Gl.glLoadIdentity();
        Gl.glLightfv(0x4000,0x1203,KeyPosition);
        var eye=new Vec3(distance*Math.Cos(yaw)*Math.Cos(pitch),.95+distance*Math.Sin(pitch),distance*Math.Sin(yaw)*Math.Cos(pitch));
        // 相机围绕车中心转动：f 指向观察目标，s/u 分别是屏幕水平/竖直轴。拖拽只改变观察方向。
        var f=Unit(new Vec3(0,.85,0)-eye); var s=Unit(Cross(f,new Vec3(0,1,0))); var u=Cross(s,f);
        cameraMatrix[0]=s.X;cameraMatrix[1]=u.X;cameraMatrix[2]=-f.X;cameraMatrix[4]=s.Y;cameraMatrix[5]=u.Y;cameraMatrix[6]=-f.Y;cameraMatrix[8]=s.Z;cameraMatrix[9]=u.Z;cameraMatrix[10]=-f.Z;cameraMatrix[15]=1;
        Gl.glMultMatrixd(cameraMatrix); Gl.glTranslated(-eye.X,-eye.Y,-eye.Z);
        Gl.glCallList(bodyList);
        var pose=state.VisualPose();
        if(state.WiperValid) DrawWipers(pose.Wiper);
        if(state.WindowValid) DrawWindows(pose.Window,eye.Z);
        Gl.SwapBuffers(dc);
        double renderMs=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
        if(renderTimeCount<renderTimes.Length)renderTimes[renderTimeCount++]=renderMs;
        RenderedFrames++;FrameRendered?.Invoke(renderMs,frameInterval);
        frameCount++;
        if(fpsClock.Elapsed.TotalSeconds>=1)
        {
            Fps=frameCount/fpsClock.Elapsed.TotalSeconds;frameCount=0;fpsClock.Restart();
            Array.Copy(renderTimes,sortedTimes,renderTimeCount);Array.Sort(sortedTimes,0,renderTimeCount);P95FrameMs=renderTimeCount==0?0:sortedTimes[Math.Clamp((int)Math.Ceiling(renderTimeCount*.95)-1,0,renderTimeCount-1)];renderTimeCount=0;
            if(Quality.ReportWindow(Fps,!RenderingPaused))ApplyQuality();
        }
    }
    void BackgroundGradient()
    {
        Gl.glDisable(0x0B50);Gl.glDisable(0x0B71);
        Gl.glMatrixMode(0x1701);Gl.glLoadIdentity();Gl.glMatrixMode(0x1700);Gl.glLoadIdentity();
        Gl.glBegin(0x0007);Gl.glColor4ub(224,236,247,255);Gl.glVertex3d(-1,-1,0);Gl.glVertex3d(1,-1,0);Gl.glColor4ub(249,252,255,255);Gl.glVertex3d(1,1,0);Gl.glVertex3d(-1,1,0);Gl.glEnd();
        Gl.glEnable(0x0B71);Gl.glEnable(0x0B50);
    }
    void DrawWipers(double motor)
    {
        // PLC 的显示角度映射为雨刷摆角；几何计算放在 CarGeometry.WiperPose，供网格验证复用。
        // 这里仅用当前快照绘制，不根据小/中/大档自设计时器，避免 UI 时间改变实体控制逻辑。
        Gl.glMaterialfv(0x0408,0x1202,NoSpecular);Gl.glMaterialf(0x0408,0x1601,0);
        foreach(double z in WiperZ)
        {
            var (pivot,tip)=CarGeometry.WiperPose(motor,z);
            Beam(pivot,tip,.018,Color.FromArgb(15,22,30));
            Beam(new Vec3(tip.X-.09,tip.Y-.065,tip.Z),new Vec3(tip.X+.10,tip.Y+.078,tip.Z),.026,Color.FromArgb(9,18,26));
            Gl.glPushMatrix();Gl.glTranslated(pivot.X,pivot.Y,pivot.Z);Gl.glScaled(.041,.025,.041);Gl.Color(Color.FromArgb(23,33,43));Gl.glCallList(sphereList);Gl.glPopMatrix();
        }
    }
    void Beam(Vec3 a,Vec3 b,double radius,Color color)
    {
        // a→b 是局部 Z 轴，叉积构造另外两条正交轴；把缓存的单位圆柱变换到雨刷臂的空间位置。
        Vec3 delta=b-a,axis=Unit(delta),u=Unit(Cross(axis,Math.Abs(axis.Y)<.9?new Vec3(0,1,0):new Vec3(1,0,0))),v=Cross(axis,u);
        beamMatrix[0]=u.X;beamMatrix[1]=u.Y;beamMatrix[2]=u.Z;beamMatrix[4]=v.X;beamMatrix[5]=v.Y;beamMatrix[6]=v.Z;beamMatrix[8]=axis.X;beamMatrix[9]=axis.Y;beamMatrix[10]=axis.Z;beamMatrix[12]=a.X;beamMatrix[13]=a.Y;beamMatrix[14]=a.Z;beamMatrix[15]=1;
        Gl.glPushMatrix();Gl.glMultMatrixd(beamMatrix);Gl.glScaled(radius,radius,Length(delta));Gl.Color(color);Gl.glCallList(cylinderList);Gl.glPopMatrix();
    }
    void DrawWindows(double percent,double eyeZ)
    {
        // percent=0 表示全关，100 表示全开。先画远侧再画近侧，透明玻璃不写深度，减少排序遮挡。
        // 两侧窗口共享同一开度；前门开口必须与 LowPolyCar.BuildSide 中的前后窗框立柱一致。
        if(percent>=99.65)return;
        Gl.glEnable(0x0BE2);Gl.glBlendFunc(0x0302,0x0303);Gl.glDepthMask(0);
        Gl.glMaterialfv(0x0408,0x1202,NoSpecular);Gl.glMaterialf(0x0408,0x1601,0);
        for(int i=0;i<2;i++)
        {
            double side=(eyeZ>=0?-1:1)*(i==0?1:-1);
            var outline=CarGeometry.WindowOutline(percent,side);
            if(outline.Length==0)continue;
            var bottomLeft=outline[0];var bottomRight=outline[1];var topRight=outline[2];var topLeft=outline[3];
            Gl.Normal(Unit(new Vec3(0,.307,side))); Gl.glBegin(0x0006);
            WindowVertex(bottomLeft);WindowVertex(bottomRight);WindowVertex(topRight);WindowVertex(topLeft);
            Gl.glEnd();
            Beam(topRight,topLeft,.008,Color.FromArgb(177,203,215));
        }
        Gl.glDepthMask(1);Gl.glDisable(0x0BE2);
    }
    static void WindowVertex(Vec3 p) {double t=Math.Clamp((p.Y-1.15)/.58,0,1);Gl.glColor4ub((byte)(190+21*t),(byte)(221+12*t),(byte)(237+8*t),224);Gl.Vertex(p);}
    static double Length(Vec3 a)=>Math.Sqrt(a.X*a.X+a.Y*a.Y+a.Z*a.Z);
    static Vec3 Unit(Vec3 a) {double length=Length(a);return length<1e-12?new Vec3(0,1,0):new Vec3(a.X/length,a.Y/length,a.Z/length);}
    static Vec3 Cross(Vec3 a,Vec3 b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    public void ResetView() { yaw=2.48;pitch=.28;distance=6.0;Invalidate(); }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        // 关闭、DPI 调整或句柄重建时释放车身缓存。OpenGL 资源属于创建它的上下文，不能跨句柄复用。
        frames.Stop();
        if(context!=IntPtr.Zero) { Gl.wglMakeCurrent(dc,context);foreach(uint list in bodyLists)if(list!=0)Gl.glDeleteLists(list,1);foreach(uint list in new[]{cylinderList,sphereList})if(list!=0)Gl.glDeleteLists(list,1);Gl.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);Gl.wglDeleteContext(context);context=IntPtr.Zero; }
        if(dc!=IntPtr.Zero) {Gl.ReleaseDC(Handle,dc);dc=IntPtr.Zero;}
        Array.Clear(bodyLists,0,bodyLists.Length); Array.Clear(bodyTriangles,0,bodyTriangles.Length);
        bodyList=cylinderList=sphereList=0;
        SetTimerPrecision(false);
        base.OnHandleDestroyed(e);
    }
    protected override void Dispose(bool disposing) { if(disposing)frames.Dispose();base.Dispose(disposing); }
}
