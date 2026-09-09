using System.Collections.Generic;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Planning
{
    /// <summary>
    /// 사용자가 고른 빌드 방향. 계산마다 새로 만들어 넘긴다(불변으로 취급).
    /// </summary>
    public sealed class PlanPreferences
    {
        public static readonly PlanPreferences None = new PlanPreferences();

        /// <summary>
        /// 밀고 있는 콤보 카테고리. 후보 추천에서 이 카테고리의 콤보 진행을 훨씬 크게 치고,
        /// 카테고리가 맞는 아티팩트를 위로 올린다. 자리로 콤보를 얻는 아티팩트는 지정 콤보 배치를 먼저 만족시킨다.
        /// </summary>
        public HashSet<string> PriorityCategories { get; set; } = new HashSet<string>();

        /// <summary>
        /// 강화 우선으로 지정한 아티팩트. 엔티티 번호 → 단계(<see cref="MinPinLevel"/>~
        /// <see cref="MaxPinLevel"/>, 0 은 없음)이고, 배치에서 이득을 <see cref="WeightOf"/>배로
        /// 쳐서 양수면 좋은 칸을 먼저 받고 음수면 남에게 양보한다. 점수도 그 기준으로 계산되므로
        /// 지정을 바꾸면 화면의 점수가 달라진다. 패널티에는 배수를 적용하지 않는다.
        ///
        /// 석판 배치까지 함께 끌려간다 - 솔버는 총점이 가장 큰 배치를 고르므로, 무거운 아티팩트가
        /// 있으면 그 칸의 레벨을 올려 주는 석판 배치를 선호하게 된다.
        /// </summary>
        public Dictionary<int, int> PinnedCharms { get; set; } = new Dictionary<int, int>();

        public const int MaxPinLevel = 3;
        public const int MinPinLevel = -3;

        /// <summary>
        /// 단계별 배수. 아티팩트 하나의 값어치가 대개 레벨 한두 개 단위라, 이만큼 벌어져야
        /// 단계마다 실제로 답이 달라진다 - 2배는 비슷한 것들 사이에서 앞서고, 4배는 웬만한
        /// 것보다 앞서고, 10배는 다른 것을 희생해서라도 최우선이다. 내리는 쪽은 그 거울이다 -
        /// 0.5배는 비슷한 것들에게 양보하고, 0.25배는 웬만한 것에게 양보하고, 0.1배는 남는 자리면
        /// 어디든 간다.
        ///
        /// 레벨 상한 위로 올리지는 못한다. 상한에 닿은 뒤로 배수가 하는 일은 그 아티팩트가
        /// 꺼지거나 밀려나지 않게 버티는 것뿐이다. 음수 배수는 두지 않는다 - 값어치가 음수이면
        /// 솔버가 그 아티팩트를 꺼지는 칸으로 보내려 하고, 막상 거기 가면 꺼진 것으로 잡혀
        /// 자리 유지 몫만 남아 의도가 흐지부지된다. "덜 중요하게 봐라"는 1 보다 작은 배수로 말한다.
        /// </summary>
        public static double WeightOf(int level)
        {
            switch (level)
            {
                case 1: return 2.0;
                case 2: return 4.0;
                case 3: return 10.0;
                case -1: return 0.5;
                case -2: return 0.25;
                case -3: return 0.1;
                default: return 1.0;
            }
        }

        /// <summary>
        /// 제한 해제 칸 고정. 여기 든 아티팩트(엔티티 번호)는 배치 조건을 무시하는 칸
        /// (<c>IGNORECRITERIA</c> 효과가 걸린 칸)에만 앉힌다. 자물쇠처럼 조건이 까다로운 것은 빈 칸이
        /// 많을 때 양옆을 비우는 공짜 자리로 갔다가 가방이 차면 옮겨 다니는데, 사용자는 석판이 계속
        /// 제약을 풀어 주기를 원했다. 그런 칸이 모자라면 계획을 포기하지 않고 그 고정만 무시하며
        /// <see cref="Arrangement.UnheldCharms"/>로 알린다.
        /// </summary>
        public HashSet<int> HeldCharms { get; set; } = new HashSet<int>();

        /// <summary>소유한 해당 종류의 아티팩트 전부를 활성 상태로 유지한다.</summary>
        public HashSet<int> RetainedCharms { get; set; } = new HashSet<int>();
        public HashSet<int> DeactivationAllowed { get; set; } = new HashSet<int>();

        /// <summary>
        /// 프리셋 코드가 알려 준, 그 빌드가 노리는 아티팩트(엔티티 번호). 손으로 지정하는
        /// <see cref="PinnedCharms"/>와 섞지 않는다 — 이쪽은 "무엇을 집을지"에 대한 조언이라
        /// 추천 끄기의 지배를 받고, 배치 가중치에는 관여하지 않는다.
        /// </summary>
        public HashSet<int> PresetCharms { get; set; } = new HashSet<int>();

        /// <summary>
        /// 손으로 채운 아티팩트 가치. 없으면 게임에서 잰 값과 레어도만으로 판단한다.
        /// 배치 점수까지 바꾸는 값이라 추천 끄기의 지배를 받지 않는다 - 어디에 놓을지의 문제다.
        /// </summary>
        public CharmValueBook CharmValues { get; set; } = CharmValueBook.Empty;

        /// <summary>
        /// 거짓이면 후보 평가를 아예 돌리지 않는다. 화면에서 가리기만 하는 것이 아니라 계산도
        /// 하지 않는 것이 정직하고, 후보마다 배치를 다시 푸는 비용도 아낀다. 배치(정렬)는 그대로다.
        /// </summary>
        public bool Recommendations { get; set; } = true;
    }
}
