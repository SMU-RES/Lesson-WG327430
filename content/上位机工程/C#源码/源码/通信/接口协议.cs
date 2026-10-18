using System;
using System.IO;
using System.Buffers.Binary;
namespace 汽车综合实践;

/// <summary>
/// DB100/DB101 协议的字节边界。所有 WORD 均以 Siemens 大端顺序发送。
/// 这里的“字序号”从 0 开始，一个字占两个字节：例如字 2 对应 DBW4。
///
/// 写 DB100 的总长度为 64 字节：字0魔数 CA71、字1版本0400、字2提交序号、字3心跳，
/// 字4挡位、字5雨量、字6控制标志、字7一次票、字8复位票、字9模式票、字31尾序号。
/// 未使用字段保持零。通信服务先写奇数序号表示“正在更新”，最后独立写入偶数序号完成提交。
///
/// 读 DB101 为 128 字节，随后重读 DBW4。只有头序号、尾序号、重读序号一致且非零偶数才接受。
/// 这解决网络读跨越 PLC 发布周期时“半帧是旧值、半帧是新值”的问题。
///
/// 解码后只产生状态快照，下一步打开状态快照.cs 看质量位；不要绕过解码直接按数组下标画图。
/// 任何长度、版本、保留位或取值范围不符都抛异常，由通信服务进入失效和重连路径。
/// </summary>
internal static class 接口协议
{
    public const ushort 版本 = 0x0400;
    public static ushort 读字(byte[] 帧, int 序号) => BinaryPrimitives.ReadUInt16BigEndian(帧.AsSpan(序号 * 2, 2));
    public static void 写字(byte[] 帧, int 序号, ushort 值) => BinaryPrimitives.WriteUInt16BigEndian(帧.AsSpan(序号 * 2, 2), 值);
    public static byte[] 编码(命令镜像 命令, ushort 序号, ushort 心跳)
    {
        if (命令.模式 > 4 || 命令.雨量 > 1000 || (命令.标志 & 0xffc0) != 0 || 序号 == 0 || (序号 & 1) != 0)
            throw new ArgumentException("命令字段超范围或序号错误");
        // 新数组自动将保留字清零；不要复用未清空缓冲区，否则可能把旧数据当新字段发送。
        var 帧 = new byte[64];
        写字(帧, 0, 0xca71); 写字(帧, 1, 版本); 写字(帧, 2, 序号);
        写字(帧, 3, 心跳); 写字(帧, 4, 命令.模式); 写字(帧, 5, 命令.雨量);
        写字(帧, 6, 命令.标志); 写字(帧, 7, 命令.一次票); 写字(帧, 8, 命令.复位票);
        写字(帧, 9, 命令.模式票); 写字(帧, 31, 序号); return 帧;
    }
    public static 状态快照 解码(byte[] 帧, byte[] 复读序号)
    {
        if (帧.Length != 128 || 复读序号.Length != 2 || 读字(帧, 0) != 0xca72 || 读字(帧, 1) != 版本)
            throw new InvalidDataException("状态帧长度、标识或V4.0版本不一致");
        // 每次使用额外读到的同一头序号核对，不能只检查首次读取的头尾而省略复读。
        ushort 序号 = 读字(帧, 2);
        if (序号 == 0 || (序号 & 1) != 0 || 序号 != 读字(帧, 63) || 序号 != BinaryPrimitives.ReadUInt16BigEndian(复读序号))
            throw new InvalidDataException("读取期间状态发生变化，丢弃本帧");
        var 字 = new ushort[64]; for (int 号 = 0; 号 < 64; 号++) 字[号] = 读字(帧, 号);
        if (字[7] > 4 || 字[8] > 3 || 字[9] > 3 || 字[11] > 1000 || (字[13] & 0xff00) != 0 || (字[15] & 0xc000) != 0 || (字[19] & 0xfff8) != 0 || (字[16] > 100 && 字[16] != 255) || (字[31] & 0xff00) != 0)
            throw new InvalidDataException("PLC状态字段越界");
        // bit0=雨刷真实角度有效；bit7=用于显示的雨刷时间姿态。两种来源互斥。
        if ((字[31] & 0x81) == 0x81) throw new InvalidDataException("雨刷角度不能同时标为测量与估算");
        if ((字[39] & 0xfff0) != 0) throw new InvalidDataException("业务状态保留位不为零");
        for (int 号 = 41; 号 < 63; 号++) if (字[号] != 0) throw new InvalidDataException("保留字段不为零");
        return new 状态快照(字);
    }
}




