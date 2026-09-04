using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 적용기가 게임 인벤토리에 대고 하는 일 전부. 게임 타입은 이 뒤에 숨고, 걸음 순서·저널·
    /// 되돌리기·시간 상한은 <see cref="ApplyPlanRoutine"/>이 가짜 격자로 시험한다.
    ///
    /// 쓰기는 예외를 던지지 않고 실패 이유를 문자열로 돌려준다 - 반복자 본문에서는 예외를 잡을 수
    /// 없기 때문이다. 반영됐는지는 적용기가 따로 읽어서 본다. 읽기가 던지는 예외는 적용기가 잡아
    /// 오류로 다룬다.
    /// </summary>
    public interface IInventoryPort
    {
        /// <summary>인벤토리가 아직 있는가. 죽음·층 이동·접속 끊김이면 거짓이다.</summary>
        bool Alive { get; }

        /// <summary>
        /// 쓰기가 그 자리에서 끝나는가(호스트). 아니면 서버를 돌아오므로 걸음마다 기다려야 한다.
        /// </summary>
        bool WritesLandImmediately { get; }

        /// <summary>지금 멀티플레이 세션인가. 적용을 시작한 뒤 동료가 접속했을 수 있어 걸음마다 본다.</summary>
        bool IsMultiplayerSession { get; }

        /// <summary>그 칸에 지금 있는 인스턴스. 비어 있으면 0.</summary>
        int InstanceAt(GridPos cell);

        /// <summary>두 칸을 맞바꾼다. 실패하면 이유, 아니면 null. 거부됐는지는 돌려주는 값으로 알 수 없다.</summary>
        string? Swap(GridPos from, GridPos to);

        /// <summary>석판의 자리·각도·회전 가능 여부. 그런 석판이 없으면 거짓.</summary>
        bool TryFindTablet(int instanceId, out GridPos cell, out int rotation, out bool rotatable);

        /// <summary>그 칸을 우클릭한다 - 참가자 세션의 회전 한 걸음. 실패하면 이유.</summary>
        string? Press(GridPos cell);

        /// <summary>
        /// 각도를 그대로 쓴다(호스트). 도중에 실패하면 이미 돌린 것을 되돌리는 것까지 구현의 몫이고,
        /// 그 결과가 돌려주는 이유에 적힌다.
        /// </summary>
        string? Rotate(IReadOnlyList<TabletTurn> turns);

        /// <summary>게임이 계산해 둔 그 칸의 레벨. 없으면 0.</summary>
        int LevelAt(GridPos cell);
    }

    /// <summary>돌려야 할 석판 하나. 호스트는 목표 각도를 그대로 쓰고, 참가자는 횟수만큼 누른다.</summary>
    public readonly struct TabletTurn
    {
        public TabletTurn(int instanceId, GridPos cell, int rotation, int presses)
        {
            InstanceId = instanceId;
            Cell = cell;
            Rotation = rotation;
            Presses = presses;
        }

        public int InstanceId { get; }

        /// <summary>누르기 직전에 아직 이 석판의 칸인지 확인하는 데 쓴다.</summary>
        public GridPos Cell { get; }

        /// <summary>목표 각도(0~3).</summary>
        public int Rotation { get; }

        /// <summary>지금 각도에서 목표까지 눌러야 하는 횟수(1~3).</summary>
        public int Presses { get; }
    }
}
