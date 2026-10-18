using System;
using System.IO;
using System.Buffers.Binary;
namespace 汽车综合实践;

/// <summary>
/// 一帧经过协议校验的 PLC 状态，构造时克隆数组，发布后不可被通信缓冲区改写。
/// 同一个实例供二维、三维、诊断与记录使用，避免几个界面分别读取造成画面相互矛盾。
///
/// 字12是综合标志；bit3为右 PLC 在线，离线时即使留下旧角度数字也不认为它有效。
/// 字31是数据质量；bit0雨刷实测、bit1车窗实测、bit7雨刷显示姿态。
/// 雨刷位置有效专供真实测量列；雨刷显示有效供画面动画。二者必须保持不同含义。
/// 字29/30使用有符号整数，单位0.01度；经short解释后除100，负角度不会变成巨大正数。
/// 字16为车窗开度0～100；还须字12 bit13有效，255等越界值不可显示成满开。
///
/// 下一步：集成视图.cs处理反馈有效期；本地数据库.记测量只允许实测质量写实际角度列。
/// </summary>
internal sealed class 状态快照
{
    public IReadOnlyList<ushort> 字 { get; }
    public 状态快照(ushort[] 字) { if(字.Length!=64)throw new ArgumentException("状态必须64个字");this.字=Array.AsReadOnly((ushort[])字.Clone()); }
    public bool 车窗开度有效 => 车窗位置有效 && 标志(13) && 字[16]<=100;
    public bool 标志(int 位) => (字[12] & (1 << 位)) != 0;
    public bool 雨刷位置有效 => 标志(3) && (字[31] & 1) != 0;
    public bool 雨刷角度为估算 => 标志(3) && (字[31] & 0x80) != 0;
    public bool 雨刷显示有效 => 雨刷位置有效 || 雨刷角度为估算;
    public float 雨刷显示角度 => unchecked((short)字[29]) / 100f;
    public bool 车窗位置有效 => 标志(3) && (字[31] & 2) != 0;
    public float 雨刷实际角度 => unchecked((short)字[29]) / 100f;
    public float 车窗实际角度 => unchecked((short)字[30]) / 100f;
}




