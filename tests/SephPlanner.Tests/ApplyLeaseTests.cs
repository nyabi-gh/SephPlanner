using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public sealed class ApplyLeaseTests
{
    /// <summary>
    /// 한 프레임 안에서는 <c>Update</c> 가 코루틴 재개보다 먼저 돈다. 앞 프레임에 재개된 적용은 이번 프레임의 검사에서
    /// 아직 진행 중이어야 한다 - 그래야 시한 직후 프레임에 두 번째 F8 이 끼어들지 못한다.
    /// </summary>
    [Fact]
    public void AnApplyResumedLastFrameIsStillHeldBeforeThisFramesResume()
    {
        var lease = new ApplyLease();
        var owner = lease.Take(100);
        for (var frame = 101; frame < 5000; frame++)
        {
            Assert.True(lease.IsHeld(frame));
            lease.Resumed(owner, frame);
        }
    }

    /// <summary>코루틴을 가진 오브젝트가 꺼지면 반복자가 재개되지도 정리되지도 않는다. 그런 적용이 F8 을 계속 잠그면 안 된다.</summary>
    [Fact]
    public void AnApplyThatStopsResumingIsReleasedWithoutFinishing()
    {
        var lease = new ApplyLease();
        var owner = lease.Take(10);
        lease.Resumed(owner, 11);

        Assert.True(lease.IsHeld(12));
        Assert.False(lease.IsHeld(13));
    }

    /// <summary>재개가 끊겨 다음 적용이 넘겨받은 뒤에 앞 적용이 늦게 끝나도 다음 적용의 잠금을 풀지 않는다.</summary>
    [Fact]
    public void ALateFinishDoesNotReleaseTheNextApply()
    {
        var lease = new ApplyLease();
        var first = lease.Take(10);
        var second = lease.Take(20);

        lease.Resumed(first, 21);
        Assert.False(lease.Release(first));
        Assert.True(lease.IsHeld(21));

        Assert.True(lease.Release(second));
        Assert.False(lease.IsHeld(21));
    }
}
