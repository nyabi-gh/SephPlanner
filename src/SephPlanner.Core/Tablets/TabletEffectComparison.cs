using System.Collections.Generic;
using SephPlanner.Core.Model;
using static SephPlanner.Core.Text.Localization;

namespace SephPlanner.Core.Tablets
{
    /// <summary>게임이 석판을 적용하며 남긴 효과 한 칸. 게임 <c>StoneTablet.AdditionEffectData</c>.</summary>
    public readonly struct AppliedTabletEffect
    {
        public AppliedTabletEffect(GridPos position, TabletEffectKind kind, int levelParam)
        {
            Position = position;
            Kind = kind;
            LevelParam = levelParam;
        }

        public GridPos Position { get; }
        public TabletEffectKind Kind { get; }
        public int LevelParam { get; }
    }

    /// <summary>우리 질의 해석을 게임이 적용한 효과 범위(<c>StoneTablet.EffectRange</c>)와 견준다.</summary>
    public static class TabletEffectComparison
    {
        /// <summary>처음 다른 곳을 적는다. 같으면 <c>null</c> 이다.</summary>
        public static string? FirstDifference(IReadOnlyList<QueryCell> cells, IReadOnlyList<AppliedTabletEffect> game)
        {
            if (cells.Count != game.Count)
                return Tr("효과 칸 수 {0} != {1}", cells.Count, game.Count);

            for (var i = 0; i < cells.Count; i++)
            {
                var expected = game[i];
                var (kind, levelParam) = QueryValue.ReadEffect(cells[i].Value);

                if (cells[i].Position != expected.Position)
                    return Tr("효과 [{0}] 위치 불일치", i);
                if (kind != expected.Kind)
                    return Tr("효과 [{0}] 종류 {1} != {2}", i, kind, expected.Kind);
                if (ReadsLevelParam(kind) && levelParam != expected.LevelParam)
                    return Tr("효과 [{0}] 수치 {1} != {2}", i, levelParam, expected.LevelParam);
            }
            return null;
        }

        /// <summary>
        /// 게임의 <c>ApplyEffect</c>·<c>RemoveEffect</c> 가 <c>levelParam</c> 을 읽는 종류. 나머지 종류의
        /// 수치는 행렬에 닿지 않는데, 모드가 거기에 제 표식을 심기도 한다(제보 6c1abf75 - 모드 석판
        /// 300045 의 <c>None</c> 칸 수치가 그 석판 번호였다).
        /// </summary>
        private static bool ReadsLevelParam(TabletEffectKind kind) =>
            kind == TabletEffectKind.IncreaseConstLevel || kind == TabletEffectKind.MultiplyConstLevel;
    }
}
