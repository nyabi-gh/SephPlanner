using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Core.Extensions
{
    /// <summary>
    /// 다른 창작마당 모드가 아티팩트 값어치를 판 상황에 맞춰 바꿔 넣는 통로. <b>실험적이다.</b> 처음 몇 판 동안은
    /// 모양이 바뀔 수 있고, 약속하는 것은 이 인터페이스와 <see cref="CharmValueEntry"/>, 같은 객체에 골라 붙이는
    /// <see cref="IPlanExtensionOptions"/>(F3 설정 줄)·<see cref="IPlanExtensionNotes"/>(HUD 글자)뿐이다.
    ///
    /// 돌려준 표는 기본 표(<c>data/values/charms.json</c>) 위에 덮인다. 값의 단위는 기본 표와 같은 <b>레벨</b>이라
    /// 콤보 가치·조화의 수정 환산과 한 자로 비교된다. 등록은 <see cref="PlanExtensions.Register"/>로 한다.
    /// </summary>
    public interface IPlanExtension
    {
        /// <summary>진단 파일과 재현 자료에 남는 이름. 보통 모드의 modId 를 쓴다.</summary>
        string Id { get; }

        /// <summary>
        /// 계획을 지을 때마다 메인 스레드에서 불린다. <c>null</c>이면 손대지 않는다. 예외를 던지면 이 확장은
        /// 새 판이 시작될 때까지 꺼진다.
        /// </summary>
        CharmValueFile? Values(GameSnapshot snapshot);
    }
}
