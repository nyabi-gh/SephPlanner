using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    /// <summary>
    /// 손으로 채우는 아티팩트 가치 항목. 게임 데이터로 잴 수 없는 아티팩트 - 능력치가 아니라
    /// 고유 효과에 값어치가 있는 것들 - 을 위한 자리다.
    ///
    /// 값의 단위는 다른 곳과 같은 <b>레벨</b>이다. 그래야 콤보 가치·조화의 수정 환산값과 한 자로
    /// 비교된다. <see cref="Tier"/>는 그 단위를 사람이 매기기 쉽게 다섯 칸으로 나눈 것뿐이고,
    /// 칸의 값은 측정된 분포에서 가져왔다.
    /// </summary>
    public sealed class CharmValueEntry
    {
        /// <summary>게임의 아티팩트 식별자. 사람이 읽는 열쇠이며 패치를 넘어 살아남는다.</summary>
        public string Id { get; set; } = "";

        /// <summary>엔티티 번호. 조회는 이쪽이 먼저다. 0이면 <see cref="Id"/>로만 찾는다.</summary>
        public int EntityId { get; set; }

        /// <summary>1(하위)~5(상위). 0은 미지정이다.</summary>
        public int Tier { get; set; }

        /// <summary>레벨 0에서의 값어치. 지정하면 <see cref="Tier"/>를 덮는다.</summary>
        public double? Base { get; set; }

        /// <summary>레벨 하나가 더 주는 값어치. 지정하면 <see cref="Tier"/>를 덮는다.</summary>
        public double? PerLevel { get; set; }

        /// <summary>
        /// 빌드 성향 꼬리표. 지금은 저장·표시만 하고 점수에 쓰지 않는다. 빌드 프로필이 여기에
        /// 가중치를 걸게 되면 그때 이어진다(docs/ROADMAP.md).
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>왜 이 값인지. 오버레이 툴팁에 그대로 보인다.</summary>
        public string Note { get; set; } = "";

        /// <summary>판단의 근거로 삼은 글. 참고 문헌으로만 적고 본문을 옮기지 않는다.</summary>
        public List<string> Sources { get; set; } = new List<string>();
    }

    /// <summary>
    /// <c>data/values/charms.json</c>의 내용. 게임에서 나오는 데이터가 아니라 우리가 만드는
    /// 데이터라서 저장소에 들어가고 배포물에 함께 나간다.
    /// </summary>
    public sealed class CharmValueFile
    {
        public int Version { get; set; }
        public List<CharmValueEntry> Charms { get; set; } = new List<CharmValueEntry>();
    }
}
