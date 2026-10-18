using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AutomotiveGpu;

/// <summary>
/// 渲染器无关的三维向量。X负方向朝车头，Y向上，Z沿车宽；只是显示坐标，不是PLC机械角度。
/// Cross求三角形法线，Dot判断朝向，Normalize把法线变成单位长度，供OpenGL基本光照使用。
/// </summary>
public readonly record struct Vec3(double X,double Y,double Z)
{
    public double Length=>Math.Sqrt(X*X+Y*Y+Z*Z);
    public Vec3 Normalize(){double length=Length;return length>1e-12?this/length:new(0,1,0);}
    public static Vec3 Cross(Vec3 a,Vec3 b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    public static double Dot(Vec3 a,Vec3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    public static Vec3 operator +(Vec3 a,Vec3 b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static Vec3 operator -(Vec3 a)=>new(-a.X,-a.Y,-a.Z);
    public static Vec3 operator *(Vec3 a,double factor)=>new(a.X*factor,a.Y*factor,a.Z*factor);
    public static Vec3 operator *(double factor,Vec3 a)=>a*factor;
    public static Vec3 operator /(Vec3 a,double divisor)=>new(a.X/divisor,a.Y/divisor,a.Z/divisor);
}
/// <summary>按颜色合并的网格集合，由GlView编译为一次构建、重复使用的显示列表。</summary>
public sealed class Scene { public List<Mesh> Meshes { get; }=new(); }
/// <summary>Indices每三个索引构成一个三角形；颜色和法线随网格保存，不依赖外部模型文件。</summary>
public sealed class Mesh
{
    public List<Vec3> Positions { get; set; }=new();
    public List<Vec3> Normals { get; set; }=new();
    public List<int> Indices { get; set; }=new();
    public Color Color { get; set; }=Color.Gray;
    public void EnsureNormals()
    {
        if(Normals.Count==Positions.Count)return;
        var normals=new Vec3[Positions.Count];
        for(int i=0;i+2<Indices.Count;i+=3)
        {
            int a=Indices[i],b=Indices[i+1],c=Indices[i+2];
            Vec3 n=Vec3.Cross(Positions[b]-Positions[a],Positions[c]-Positions[a]);
            normals[a]+=n;normals[b]+=n;normals[c]+=n;
        }
        Normals=normals.Select(n=>n.Normalize()).ToList();
    }
}
/// <summary>
/// 公共机构坐标。Main→GlView读取SnapshotReader→WiperPose/WindowOutline→绘制动态机构。
/// 车身外形在LowPolyCar改变，前风挡、雨刷枢轴和前侧窗安装平面保持不变。
/// 静态车身由 LowPolyCar 生成，本类只负责矢量和活动机构的坐标映射。
/// </summary>
public static class CarGeometry
{
    /// <summary>玻璃越高越向车身中央收窄，sign=-1/+1分别生成两侧安装平面。</summary>
    public static Vec3 Side(double x,double y,double sign)=>new(x,y,sign*(.896-(y-1.15)*.307));
    /// <summary>
    /// PLC显示角度-120～120映射为屏幕雨刷-40～40度，只用于反馈动画，不生成PLC指令。
    /// X/Y臂长与风挡斜率一致，Z分量形成扫掠。枢轴和臂端坐标与静态挡风玻璃保持一致。
    /// </summary>
    public static (Vec3 Pivot,Vec3 Tip) WiperPose(double motor,double lateralPosition)
    {
        double a=Math.Clamp(motor,-120,120)/3*Math.PI/180;
        Vec3 pivot=new(-1.055,1.244,lateralPosition);
        return(pivot,new(pivot.X+.48*Math.Cos(a),pivot.Y+.357*Math.Cos(a),lateralPosition+.51*Math.Sin(a)));
    }
    /// <summary>
    /// 当前可见玻璃，顺序为左下/右下/右上/左上。0%全关，100%全开。
    /// 上沿下降时左上角沿A柱移动，保持在模型开口内；输入仍来自SnapshotReader。
    /// </summary>
    public static Vec3[] WindowOutline(double percent,double side)
    {
        double y=1.15+.58*(1-Math.Clamp(percent,0,100)/100);
        if(y<=1.152)return Array.Empty<Vec3>();
        double top=Math.Min(y,1.725),left=-1.035+.645*(top-1.15)/.575;
        return new[]{Side(-1.035,1.15,side),Side(.40,1.15,side),Side(.40,top,side),Side(left,top,side)};
    }
    /// <summary>通用水平裁剪辅助函数，按边与高度平面的交点裁出下方多边形。</summary>
    public static Vec3[] ClipBelow(Vec3[] polygon,double height)
    {
        var result=new List<Vec3>();
        for(int i=0;i<polygon.Length;i++)
        {
            var a=polygon[i];var b=polygon[(i+1)%polygon.Length];bool ain=a.Y<=height,bin=b.Y<=height;
            if(ain)result.Add(a);
            if(ain!=bin){double t=(height-a.Y)/(b.Y-a.Y);result.Add(a+(b-a)*t);}
        }
        return result.ToArray();
    }
}
