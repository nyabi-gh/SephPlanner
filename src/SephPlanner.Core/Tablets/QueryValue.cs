using System.Globalization;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 질의 값 문자열의 해석. 게임의 <c>AdditionEffectData</c>/<c>AdditionCriteriaData</c> 생성자와
    /// 같은 규칙을 따른다.
    /// </summary>
    public static class QueryValue
    {
        public static (TabletEffectKind Kind, int LevelParam) ReadEffect(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                return (TabletEffectKind.IncreaseConstLevel, level);

            if (value == "X") return (TabletEffectKind.Disable, 0);
            if (value == "IGNORECRITERIA") return (TabletEffectKind.IgnoreCriteria, 0);

            var parts = value.Split('/');
            if (parts.Length == 2 && parts[0] == "MUL" &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var factor))
                return (TabletEffectKind.MultiplyConstLevel, factor);

            return (TabletEffectKind.None, 0);
        }

        public static TabletCriteriaKind ReadCriteria(string value)
        {
            switch (value)
            {
                case "ITEM": return TabletCriteriaKind.AnyItem;
                case "CHARM": return TabletCriteriaKind.OnlyCharm;
                case "PLACED": return TabletCriteriaKind.Placed;
                default: return TabletCriteriaKind.None;
            }
        }
    }
}
