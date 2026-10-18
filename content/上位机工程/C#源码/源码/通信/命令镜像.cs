using System;
using System.IO;
using System.Buffers.Binary;
namespace 汽车综合实践;

/// <summary>
/// 一次通信周期的不可变命令。阅读顺序：操作窗口事件 → 通信服务.取命令 → 本类型 → 接口协议.编码。
///
/// 模式：0=关、1=自动、2=小、3=中、4=大。雨量：0～1000，自动档分档由 PLC 完成。
/// 标志位：bit0 向上保持，bit1 向下保持，bit2 手在位，bit3 软件停止，bit4 控制申请，bit5 雨量有效。
/// 其余位必须为零，防止协议版本或内存布局错误被悄悄接受。
///
/// 一次票、复位票、模式票是 16 位递增票号。按钮每触发一次，相应票加一；PLC 按票变化消费请求。
/// 周期重复发送同一票不会把“一次”重放成多次；重连握手先读取 PLC 已确认票再对齐本机票号。
/// 停止与向上/向下属于电平语义，不靠票号保持。该区别见通信服务.循环中的首帧握手。
/// </summary>
internal sealed record 命令镜像(ushort 模式, ushort 雨量, ushort 标志,
    ushort 一次票, ushort 复位票, ushort 模式票);



