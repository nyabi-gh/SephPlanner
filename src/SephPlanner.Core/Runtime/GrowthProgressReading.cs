using System;
using System.Globalization;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 게임이 화면에 보내는 성장 진행도 표시를 숫자로 되돌린다.
    ///
    /// 게임은 진행도를 서버에만 두고 소유자에게는 <c>SetEffectHUDValue</c>로 표시 문자열만 보낸다.
    /// 이름은 <c>Charm_{인스턴스}</c>, 값은 <c>n/목표</c> 꼴이다. 참가자 세션에서 진행도를 알 수 있는
    /// 길은 이것뿐이라, 읽는 규칙만 따로 떼어 게임 없이 시험한다.
    ///
    /// 모르는 모양은 조용히 0으로 만들지 않고 실패로 돌려준다. 0은 "아직 아무것도 못 채웠다"는
    /// 뜻이라, 못 읽은 것을 0으로 적으면 둘을 구분할 수 없게 된다.
    /// </summary>
    public static class GrowthProgressReading
    {
        private const string Prefix = "Charm_";

        public static bool TryRead(string? effectName, string? value, out int instanceId, out int progress)
        {
            instanceId = 0;
            progress = 0;
            if (effectName is null || value is null) return false;
            if (!effectName.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            if (!int.TryParse(effectName.AsSpan(Prefix.Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out instanceId)) return false;

            var slash = value.IndexOf('/');
            var counted = slash < 0 ? value.AsSpan() : value.AsSpan(0, slash);
            if (!int.TryParse(counted.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out progress) ||
                progress < 0)
            {
                instanceId = 0;
                progress = 0;
                return false;
            }
            return true;
        }
    }
}
