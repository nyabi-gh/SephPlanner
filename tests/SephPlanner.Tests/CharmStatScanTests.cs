using System.Reflection.Emit;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

/// <summary>
/// 능력치 표 없이 자기 코드로 능력치를 올려 주는 아티팩트를 게임 IL 에서 찾아내는 쪽.
/// 클래스마다 손으로 적어 두면 게임이 패치될 때마다 조용히 낡으므로 코드를 읽는다.
/// </summary>
public class CharmStatScanTests
{
    private const int ArrayToken = 0x0A000001;
    private const int OtherToken = 0x0A000002;
    private const int AddCustomStat = 0x0B000001;
    private const int AddMaxHp = 0x0B000002;
    private const int SafeRandomAccess = 0x0B000003;
    private const int LevelToIdx = 0x0B000004;
    private const int AttackSpeed = 7;

    private sealed class FakeAssembly : ICharmAssembly
    {
        public List<CharmMethodBody> Bodies { get; } = new();

        public IEnumerable<CharmMethodBody> CharmMethods() => Bodies;

        public string? ArrayField(int token) => token == ArrayToken ? "atkSpeedByLevel" : null;

        public CalledMethod? Method(int token) => token switch
        {
            AddCustomStat => new CalledMethod { Name = "AddCustomStat", ArgumentCount = 2 },
            // 게임의 AddMaxHp 는 인자가 둘이다. 마지막 인자를 값으로 잡으면 배열을 놓친다.
            AddMaxHp => new CalledMethod { Name = "AddMaxHp", ArgumentCount = 2 },
            SafeRandomAccess => new CalledMethod
            { Name = "SafeRandomAccess", ArgumentCount = 2, IsStatic = true, ReturnsValue = true },
            LevelToIdx => new CalledMethod { Name = "LevelToIdx", ArgumentCount = 1, ReturnsValue = true },
            _ => null,
        };

        public string? Stat(int value) => value == AttackSpeed ? "AttackSpeed" : null;
    }

    private sealed class Emitter
    {
        private readonly List<byte> _bytes = new();

        public Emitter Op(OpCode op)
        {
            if (op.Size == 2) _bytes.Add((byte)(op.Value >> 8));
            _bytes.Add((byte)(op.Value & 0xFF));
            return this;
        }

        public Emitter Op(OpCode op, int token)
        {
            Op(op);
            _bytes.AddRange(BitConverter.GetBytes(token));
            return this;
        }

        public byte[] Done() => _bytes.ToArray();
    }

    private static CharmStatScanReport Scan(string typeName, byte[] il)
    {
        var assembly = new FakeAssembly();
        assembly.Bodies.Add(new CharmMethodBody { TypeName = typeName, MethodName = "OnEnabledEffect", Il = il });
        return CharmStatScan.Run(assembly);
    }

    [Fact]
    public void AStatAddedFromALevelArrayIsFound()
    {
        // NetworkAvatar.AddCustomStat(ECustomStat.AttackSpeed, atkSpeedByLevel[idx])
        var il = new Emitter()
            .Op(OpCodes.Ldarg_0)
            .Op(OpCodes.Ldc_I4_7)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldfld, ArrayToken)
            .Op(OpCodes.Ldc_I4_0)
            .Op(OpCodes.Ldelem_I4)
            .Op(OpCodes.Callvirt, AddCustomStat)
            .Op(OpCodes.Ret)
            .Done();

        var grant = Assert.Single(Scan("Charm_IncreaseAttackSpeed", il).Grants);

        Assert.Equal("Charm_IncreaseAttackSpeed", grant.TypeName);
        Assert.Equal("atkSpeedByLevel", grant.Field);
        Assert.Equal("AttackSpeed", grant.Stat);
    }

    [Fact]
    public void TheArrayIsStillFoundThroughAHelperCall()
    {
        // 게임은 배열을 SafeRandomAccess(arr, LevelToIdx(level)) 로 읽는 곳이 많다.
        // 호출을 지나며 출처를 잃으면 그 아티팩트들이 통째로 빠진다.
        var il = new Emitter()
            .Op(OpCodes.Ldarg_0)
            .Op(OpCodes.Ldc_I4_7)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldfld, ArrayToken)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldc_I4_1).Op(OpCodes.Call, LevelToIdx)
            .Op(OpCodes.Call, SafeRandomAccess)
            .Op(OpCodes.Callvirt, AddCustomStat)
            .Op(OpCodes.Ret)
            .Done();

        Assert.Equal("atkSpeedByLevel", Assert.Single(Scan("Charm_X", il).Grants).Field);
    }

    [Fact]
    public void AGranterWhoseNameIsTheStatNeedsNoConstant()
    {
        // AddMaxHp 는 능력치를 인자로 받지 않는다. 그리고 인자가 둘이라 값이 마지막이 아니다.
        var il = new Emitter()
            .Op(OpCodes.Ldarg_0)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldfld, ArrayToken)
            .Op(OpCodes.Ldc_I4_0)
            .Op(OpCodes.Ldelem_I4)
            .Op(OpCodes.Ldc_I4_1)
            .Op(OpCodes.Callvirt, AddMaxHp)
            .Op(OpCodes.Ret)
            .Done();

        Assert.Equal("MaxHp", Assert.Single(Scan("Charm_IncreaseHP", il).Grants).Stat);
    }

    [Fact]
    public void TheEffectBeingTakenBackIsNotCountedAsAGrant()
    {
        // 효과를 거둘 때 같은 배열을 음수로 되돌린다. 그것까지 세면 부호가 뒤집힌다.
        var il = new Emitter()
            .Op(OpCodes.Ldarg_0)
            .Op(OpCodes.Ldc_I4_7)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldfld, ArrayToken)
            .Op(OpCodes.Ldc_I4_0)
            .Op(OpCodes.Ldelem_I4)
            .Op(OpCodes.Neg)
            .Op(OpCodes.Callvirt, AddCustomStat)
            .Op(OpCodes.Ret)
            .Done();

        var report = Scan("Charm_IncreaseAttackSpeed", il);

        Assert.Empty(report.Grants);
        Assert.Equal("Charm_IncreaseAttackSpeed", Assert.Single(report.Unresolved));
    }

    [Fact]
    public void AValueThatIsNotJustTheArrayIsLeftAlone()
    {
        // "잎 하나당 피해 +N" 처럼 배열에 다른 수를 곱해 주는 것은 레벨당 능력치가 아니다.
        // 그것을 능력치 표로 삼으면 값어치가 통째로 틀린다.
        var il = new Emitter()
            .Op(OpCodes.Ldarg_0)
            .Op(OpCodes.Ldc_I4_7)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldfld, ArrayToken)
            .Op(OpCodes.Ldc_I4_0)
            .Op(OpCodes.Ldelem_I4)
            .Op(OpCodes.Ldarg_1)
            .Op(OpCodes.Mul)
            .Op(OpCodes.Callvirt, AddCustomStat)
            .Op(OpCodes.Ret)
            .Done();

        Assert.Empty(Scan("Charm_GoldIsDamage", il).Grants);
    }

    [Fact]
    public void AnUnknownStatConstantIsNotGuessed()
    {
        var il = new Emitter()
            .Op(OpCodes.Ldarg_0)
            .Op(OpCodes.Ldc_I4_5)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldfld, ArrayToken)
            .Op(OpCodes.Ldc_I4_0)
            .Op(OpCodes.Ldelem_I4)
            .Op(OpCodes.Callvirt, AddCustomStat)
            .Op(OpCodes.Ret)
            .Done();

        Assert.Empty(Scan("Charm_X", il).Grants);
    }

    [Fact]
    public void AFieldThatIsNotAnArrayIsNotAStatTable()
    {
        var il = new Emitter()
            .Op(OpCodes.Ldarg_0)
            .Op(OpCodes.Ldc_I4_7)
            .Op(OpCodes.Ldarg_0).Op(OpCodes.Ldfld, OtherToken)
            .Op(OpCodes.Callvirt, AddCustomStat)
            .Op(OpCodes.Ret)
            .Done();

        Assert.Empty(Scan("Charm_X", il).Grants);
    }
}
