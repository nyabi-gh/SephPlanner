namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 자동 배치가 진행 중인지. 적용 코루틴이 프레임마다 재개될 때 <see cref="Resumed"/> 로 알린다.
    ///
    /// 끝을 알리지 않은 적용도 재개가 끊기면 끝난 것으로 본다. 유니티는 코루틴을 가진 오브젝트가 꺼질 때 반복자를
    /// <c>Dispose</c> 하지 않아 <c>finally</c> 가 돌지 않으므로, 이것이 없으면 F8 이 세션 내내 잠긴다. 시각으로 재지 않는
    /// 것은, 한 프레임 안에서 <c>Update</c> 가 코루틴 재개보다 먼저 돌기 때문이다 - 반복자 자신의 대기 시한과 같은 시각을
    /// 넘긴 첫 프레임에 아직 재개되지 않은 적용이 풀려, 두 적용이 같은 가방을 민다.
    /// </summary>
    public sealed class ApplyLease
    {
        private object? _owner;
        private int _resumedFrame;

        /// <summary>
        /// 앞 프레임에 재개됐으면 아직 산 것이다. 이번 프레임의 재개는 이 검사 뒤에 온다. 그래서 적용은 매 프레임
        /// 재개되어야 한다 - 시간을 두고 기다리는 대기 객체를 넘기면 그 사이 풀린다.
        /// </summary>
        public bool IsHeld(int frame) => _owner != null && frame - _resumedFrame <= 1;

        public object Take(int frame)
        {
            var owner = new object();
            _owner = owner;
            _resumedFrame = frame;
            return owner;
        }

        public void Resumed(object owner, int frame)
        {
            if (ReferenceEquals(_owner, owner)) _resumedFrame = frame;
        }

        /// <summary>
        /// 아직 이 적용이 쥐고 있을 때만 놓는다. 재개가 끊겨 다른 적용이 넘겨받은 뒤에 늦게 끝난 적용이 남의 것을
        /// 풀지 않게 한다.
        /// </summary>
        public bool Release(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return false;
            _owner = null;
            return true;
        }
    }
}
