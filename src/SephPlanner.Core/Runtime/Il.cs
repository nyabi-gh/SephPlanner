using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SephPlanner.Core.Runtime
{
    /// <summary>IL 한 걸음. 우리가 보는 것은 명령과 토큰, 그리고 상수뿐이다.</summary>
    public readonly struct IlStep
    {
        public readonly OpCode Op;
        public readonly int Token;
        public readonly long Operand;

        public IlStep(OpCode op, int token, long operand)
        {
            Op = op;
            Token = token;
            Operand = operand;
        }
    }

    /// <summary>메서드 본문 바이트를 명령으로 푼다.</summary>
    public static class Il
    {
        private static readonly Dictionary<short, OpCode> ByValue = Build();

        private static Dictionary<short, OpCode> Build()
        {
            var map = new Dictionary<short, OpCode>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is OpCode op)
                    map[op.Value] = op;
            return map;
        }

        private static int OperandSize(OperandType type)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                default: return -1;
            }
        }

        /// <summary>알 수 없는 명령을 만나면 거기서 멈춘다 - 그 뒤는 어긋나게 읽힌다.</summary>
        public static IEnumerable<IlStep> Decode(byte[] il)
        {
            var steps = new List<IlStep>();
            var index = 0;
            while (index < il.Length)
            {
                short code = il[index++];
                if (code == 0xFE)
                {
                    if (index >= il.Length) break;
                    code = (short)(0xFE00 | il[index++]);
                }
                if (!ByValue.TryGetValue(code, out var op)) break;

                int token = 0;
                long operand = 0;
                if (op.OperandType == OperandType.InlineSwitch)
                {
                    if (index + 4 > il.Length) break;
                    var count = BitConverter.ToInt32(il, index);
                    index += 4 + count * 4;
                }
                else
                {
                    var size = OperandSize(op.OperandType);
                    if (size < 0 || index + size > il.Length) break;
                    if (size == 4)
                    {
                        token = BitConverter.ToInt32(il, index);
                        operand = token;
                    }
                    else if (size == 1)
                    {
                        operand = op.OperandType == OperandType.ShortInlineI ? (sbyte)il[index] : il[index];
                        token = (int)operand;
                    }
                    else if (size == 8)
                    {
                        operand = BitConverter.ToInt64(il, index);
                    }
                    index += size;
                }
                steps.Add(new IlStep(op, token, operand));
            }
            return steps;
        }

        /// <summary><c>ldc.i4*</c>가 쌓는 값. 다른 명령이면 <c>null</c>.</summary>
        public static int? PushedInt(IlStep step)
        {
            var name = step.Op.Name ?? "";
            if (name == "ldc.i4" || name == "ldc.i4.s") return (int)step.Operand;
            if (name == "ldc.i4.m1") return -1;
            if (name.StartsWith("ldc.i4.", StringComparison.Ordinal) &&
                int.TryParse(name.AsSpan("ldc.i4.".Length), out var value)) return value;
            return null;
        }
    }
}
