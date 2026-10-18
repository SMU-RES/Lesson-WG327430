using System;
using System.Collections.Generic;
using System.Drawing;

namespace AutomotiveGpu;

/// <summary>
/// 固定的轻量车身生成器。阅读顺序：Main → 操作窗口 → GlView.ApplyQuality → Build。
/// 低车鼻、短斜背、深色车顶和少量色块构成当前车辆外观。
/// 静态车身按材质合并并缓存，不在每次刷新时重建；显示坐标不参与 PLC 控制。
/// </summary>
public static class LowPolyCar
{
    /// <summary>
    /// minimal 只把八边形车轮减为六边形，软件渲染仍保留相同车身与机构安装坐标。
    /// 本方法只在首次使用一种细度时执行，生成结果由GlView编译进显示列表缓存。
    /// X负方向朝车头，Y向上，Z沿车宽；模型单位只用于显示，不参与PLC的速度或角度控制。
    /// </summary>
    public static Scene Build(bool minimal=false)
    {
        var b=new Builder();
        var shape=new Shape(-2.60,.64,.76,2.32,.84,.85,.99,1.58,1.77);
        var paint=new Paint("#315E9A","#5184C7","#273C58","#7398B6","#233953","#DAB66B");
        // 仅用一块底板与八边形阴影提供接地参照，避免圆台侧壁和装饰环消耗面数。
        b.Quad(new(-2.75,-.015,-1.30),new(2.60,-.015,-1.30),new(2.60,-.015,1.30),new(-2.75,-.015,1.30),"#D9E1E8",new(0,1,0));
        b.Ellipse(new(0,.002,0),2.43,1.04,8,"#BAC7D1");
        var stations=new (double X,double Y,double Z)[]{
            (shape.NoseX,shape.NoseY,shape.NoseZ),(-1.60,1.11,1.0),(-1.12,1.14,.99),
            (.45,1.15,.97),(1.60,1.13,1.0),(shape.TailX,shape.TailY,shape.TailZ)};
        (double Y,double Z) BodyAt(double x)
        {
            for(int i=0;i+1<stations.Length;i++)if(x<=stations[i+1].X)
            {
                var p=stations[i];var q=stations[i+1];double t=(x-p.X)/(q.X-p.X);
                return(p.Y+(q.Y-p.Y)*t,p.Z+(q.Z-p.Z)*t);
            }
            return(stations[^1].Y,stations[^1].Z);
        }
        // 四段半圆轮拱已足够表达车身开口，保持固定采样数以控制顶点负载。
        var samples=new SortedSet<double>{shape.NoseX,-.86,.86,shape.TailX};
        foreach(double axle in new[]{-1.49,1.49})
            for(int i=0;i<=4;i++)samples.Add(axle+.54*Math.Cos(i*Math.PI/4));
        var profile=new List<double>(samples);
        static double Bottom(double x)
        {
            double result=.44;
            foreach(double center in new[]{-1.49,1.49})
            {double d=Math.Abs(x-center);if(d<.54)result=Math.Max(result,.49+Math.Sqrt(Math.Max(0,.54*.54-d*d)));}
            return result;
        }
        foreach(double side in new[]{-1.0,1.0})
        {
            for(int i=0;i+1<profile.Count;i++)
            {
                double x=profile[i],xx=profile[i+1];var p=BodyAt(x);var q=BodyAt(xx);
                b.Quad(new(x,Bottom(x),p.Z*.97*side),new(xx,Bottom(xx),q.Z*.97*side),
                    new(xx,q.Y,q.Z*side),new(x,p.Y,p.Z*side),paint.Body,new(0,0,side));
            }
            for(int i=0;i+1<stations.Length;i++)
            {
                var p=stations[i];var q=stations[i+1];
                b.Quad(new(p.X,p.Y,p.Z*side),new(q.X,q.Y,q.Z*side),
                    new(q.X,q.Y+.045,q.Z*.76*side),new(p.X,p.Y+.045,p.Z*.76*side),paint.Top,new(0,1,side));
                if(q.X<=-1.12)
                    b.Quad(new(p.X,p.Y+.045,p.Z*.76*side),new(q.X,q.Y+.045,q.Z*.76*side),
                        new(q.X,q.Y+.07,0),new(p.X,p.Y+.07,0),paint.Top,new(0,1,0));
            }
            BuildSide(b,shape,paint,side);
            foreach(double axle in new[]{-1.49,1.49})b.Wheel(axle,side,minimal?6:8,paint.Rim);
        }
        b.Quad(new(shape.NoseX-.002,.45,-shape.NoseZ),new(shape.NoseX-.002,.45,shape.NoseZ),
            new(shape.NoseX-.002,shape.NoseY+.045,shape.NoseZ),new(shape.NoseX-.002,shape.NoseY+.045,-shape.NoseZ),paint.Body,new(-1,0,0));
        b.Quad(new(shape.TailX+.002,.45,-shape.TailZ),new(shape.TailX+.002,shape.TailY+.045,-shape.TailZ),
            new(shape.TailX+.002,shape.TailY+.045,shape.TailZ),new(shape.TailX+.002,.45,shape.TailZ),paint.Body,new(1,0,0));
        b.Quad(new(shape.RearGlassX,1.19,-.83),new(shape.TailX,shape.TailY+.045,-shape.TailZ*.76),
            new(shape.TailX,shape.TailY+.045,shape.TailZ*.76),new(shape.RearGlassX,1.19,.83),paint.Top,new(0,1,0));
        BuildCabin(b,shape,paint);BuildIdentity(b,shape,paint);
        return b.Scene;
    }
    // 车身尺寸和材料集中声明，网格数量只由车轮细度决定。
    sealed record Shape(double NoseX,double NoseY,double NoseZ,double TailX,double TailY,double TailZ,double RearTopX,double RearTopY,double RearGlassX);
    sealed record Paint(string Body,string Top,string Roof,string Glass,string Frame,string Rim);

    static void BuildSide(Builder b,Shape shape,Paint paint,double side)
    {
        Vec3 S(double x,double y)=>CarGeometry.Side(x,y,side);Vec3 normal=new(0,0,side);
        // A柱、前门上沿、B柱和腰线共用Side，前侧窗不画静态玻璃。
        // 动态玻璃使用同一安装面裁剪，必须保持这个前窗轮廓。
        var frame=new[]{S(-1.095,1.125),S(-.40,1.748),S(.44,1.76),S(shape.RearTopX,shape.RearTopY),S(shape.RearGlassX,1.125)};
        for(int i=0;i+1<frame.Length;i++)b.Ribbon(frame[i],frame[i+1],.026,paint.Frame,normal);
        b.Ribbon(S(.435,1.14),S(.435,1.745),.027,paint.Frame,normal);
        b.Ribbon(S(-1.105,1.125),S(shape.RearGlassX,1.125),.014,paint.Top,normal);
        b.Quad(S(.47,1.16),S(shape.RearGlassX-.10,1.16),S(shape.RearTopX-.04,shape.RearTopY-.035),S(.47,1.714),paint.Glass,normal);
        b.Ribbon(new(.46,.56,side*.982),new(.46,1.10,side*.987),.005,paint.Frame,normal);
    }
    static void BuildCabin(Builder b,Shape shape,Paint paint)
    {
        // 前风挡四角和雨刷枢轴保持不变；低面数只删装饰，不移动反馈动画的安装位置。
        b.Quad(new(-1.16,1.15,-.875),new(-1.16,1.15,.875),new(-.39,1.724,.705),new(-.39,1.724,-.705),paint.Frame,new(-1,1,0));
        b.Quad(new(-1.105,1.205,-.817),new(-1.105,1.205,.817),new(-.423,1.714,.674),new(-.423,1.714,-.674),paint.Glass,new(-1,1,0));
        // 两块平面替代曲面车顶。前半段固定，后半段固定为短斜背。
        Vec3 R(double x,double y,double side)=>CarGeometry.Side(x,y,side);
        b.Quad(R(-.40,1.748,-1),R(.44,1.76,-1),R(.44,1.76,1),R(-.40,1.748,1),paint.Roof,new(0,1,0));
        b.Quad(R(.44,1.76,-1),R(shape.RearTopX,shape.RearTopY,-1),R(shape.RearTopX,shape.RearTopY,1),R(.44,1.76,1),paint.Roof,new(0,1,0));
        double rw=Math.Abs(CarGeometry.Side(shape.RearTopX,shape.RearTopY,1).Z);
        b.Quad(new(shape.RearTopX,shape.RearTopY,-rw),new(shape.RearTopX,shape.RearTopY,rw),
            new(shape.RearGlassX,1.17,.86),new(shape.RearGlassX,1.17,-.86),paint.Frame,new(1,1,0));
        b.Quad(new(shape.RearTopX+.03,shape.RearTopY-.01,-rw+.04),new(shape.RearTopX+.03,shape.RearTopY-.01,rw-.04),
            new(shape.RearGlassX-.04,1.22,.79),new(shape.RearGlassX-.04,1.22,-.79),paint.Glass,new(1,1,0));
        // 开窗后仍见座舱深度，但只保留底板、仪表台和两块靠背，不构建细座椅。
        b.Quad(new(-1.04,.72,-.72),new(1.60,.72,-.72),new(1.60,.72,.72),new(-1.04,.72,.72),"#52626D",new(0,1,0));
        b.Quad(new(-.94,1.20,-.68),new(-.66,1.20,-.68),new(-.66,1.20,.68),new(-.94,1.20,.68),"#52626D",new(0,1,0));
        b.Quad(new(-.66,1.04,-.68),new(-.66,1.20,-.68),new(-.66,1.20,.68),new(-.66,1.04,.68),"#52626D",new(1,0,0));
        foreach(double z in new[]{-.42,.42})
            b.Quad(new(.25,.80,z-.20),new(.25,1.40,z-.20),new(.25,1.40,z+.20),new(.25,.80,z+.20),"#81919A",new(-1,0,0));
    }
    static void BuildIdentity(Builder b,Shape shape,Paint paint)
    {
        // 灯具与格栅都是单层色块。斜灯、低车鼻和金色点缀表达运动感，不加灯罩细分。
        double front=shape.NoseX-.009,back=shape.TailX+.009;
        b.Quad(new(front,.53,-.38),new(front,.53,.38),new(front,shape.NoseY-.035,.34),new(front,shape.NoseY-.035,-.34),paint.Frame,new(-1,0,0));
        foreach(double s in new[]{-1.0,1.0})
        {
            const double slant=.11;
            b.Quad(new(front,shape.NoseY-.025,s*.44),new(front+slant,shape.NoseY-.02,s*.72),
                new(front+slant,shape.NoseY+.025,s*.72),new(front,shape.NoseY+.035,s*.44),"#F2D389",new(-1,0,s));
            b.Quad(new(back,.73,s*.44),new(back,.73,s*.73),new(back,.80,s*.73),new(back,.80,s*.44),"#AA6267",new(1,0,0));
        }
        b.Ribbon(new(front,.458,-.68),new(front,.458,.68),.014,paint.Frame,new(-1,0,0));
        // 尾翼和两根支架只增加三个平面，保留外形而不堆积细节网格。
        b.Quad(new(1.88,1.24,-.96),new(2.28,1.24,-.96),new(2.28,1.24,.96),new(1.88,1.24,.96),paint.Roof,new(0,1,0));
        foreach(double s in new[]{-1.0,1.0})b.Ribbon(new(2.01,1.08,s*.53),new(2.01,1.24,s*.53),.026,paint.Frame,new(0,0,s));
    }
    /// <summary>
    /// 按颜色合并三角网格。平面保存单位法线，省掉曲面法线、纹理和高光材质。
    /// Quad分成两面，Ribbon也是窄四边形；车轮每段固定5面，材质相同的三角形共用一个网格。
    /// </summary>
    sealed class Builder
    {
        public Scene Scene { get; }=new();
        readonly Dictionary<int,Mesh> materials=new();
        Mesh Material(string color)
        {
            Color c=ColorTranslator.FromHtml(color);int key=c.ToArgb();
            if(materials.TryGetValue(key,out var found))return found;
            var mesh=new Mesh{Color=c};materials.Add(key,mesh);Scene.Meshes.Add(mesh);return mesh;
        }
        public void Triangle(Vec3 a,Vec3 b,Vec3 c,string color,Vec3 outward)
        {
            Vec3 normal=Vec3.Cross(b-a,c-a);if(normal.Length<1e-10)return;
            if(Vec3.Dot(normal,outward)<0){(b,c)=(c,b);normal=-normal;}normal=normal.Normalize();
            var mesh=Material(color);int start=mesh.Positions.Count;
            mesh.Positions.Add(a);mesh.Positions.Add(b);mesh.Positions.Add(c);
            mesh.Normals.Add(normal);mesh.Normals.Add(normal);mesh.Normals.Add(normal);
            mesh.Indices.Add(start);mesh.Indices.Add(start+1);mesh.Indices.Add(start+2);
        }
        public void Quad(Vec3 a,Vec3 b,Vec3 c,Vec3 d,string color,Vec3 outward)
        {Triangle(a,b,c,color,outward);Triangle(a,c,d,color,outward);}
        public void Ribbon(Vec3 a,Vec3 b,double halfWidth,string color,Vec3 outward)
        {Vec3 offset=Vec3.Cross((b-a).Normalize(),outward).Normalize()*halfWidth;Quad(a-offset,b-offset,b+offset,a+offset,color,outward);}
        public void Ellipse(Vec3 center,double rx,double rz,int sides,string color)
        {
            for(int i=0;i<sides;i++)
            {
                double a=i*Math.PI*2/sides,c=(i+1)*Math.PI*2/sides;
                Triangle(center,center+new Vec3(rx*Math.Cos(a),0,rz*Math.Sin(a)),center+new Vec3(rx*Math.Cos(c),0,rz*Math.Sin(c)),color,new(0,1,0));
            }
        }
        public void Wheel(double x,double side,int sides,string rim)
        {
            // 轮胎外圈、外侧壁和轮盖共5面/段，轮盖以交错扇区颜色表达结构。
            Vec3 P(double r,double a,double z)=>new(x+r*Math.Cos(a),.49+r*Math.Sin(a),side*z);
            for(int i=0;i<sides;i++)
            {
                double a=i*Math.PI*2/sides,c=(i+1)*Math.PI*2/sides,m=(a+c)/2;
                Quad(P(.475,a,.88),P(.475,c,.88),P(.475,c,1.13),P(.475,a,1.13),"#34414B",new(Math.Cos(m),Math.Sin(m),0));
                Quad(P(.475,a,1.131),P(.475,c,1.131),P(.28,c,1.131),P(.28,a,1.131),"#293843",new(0,0,side));
                Triangle(new(x,.49,side*1.133),P(.28,a,1.133),P(.28,c,1.133),i%2==1?"#506783":rim,new(0,0,side));
            }
        }
    }
}
