using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class TabletRotationTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(0, 3, 3)]
    [InlineData(3, 0, 1)]
    [InlineData(2, 1, 3)]
    public void PressCountFollowsTheGameDirection(int current, int target, int presses)
    {
        Assert.Equal(presses, TabletRotation.PressesFrom(current, target));
    }

    /// <summary>
    /// 방향을 뒤집어 세면 한 번 누를 것을 세 번 누른다. 게임의 <c>Rotate()</c> 가 각도를 1 씩
    /// 올리므로, 센 횟수만큼 올렸을 때 목표에 정확히 닿아야 한다.
    /// </summary>
    [Fact]
    public void PressingThatManyTimesLandsOnTheTarget()
    {
        for (var current = 0; current < TabletRotation.Steps; current++)
        {
            for (var target = 0; target < TabletRotation.Steps; target++)
            {
                var landed = (current + TabletRotation.PressesFrom(current, target)) % TabletRotation.Steps;
                Assert.Equal(target, landed);
            }
        }
    }

    /// <summary>적용 도중 실패하면 눌러 둔 만큼 마저 돌려 원래 각도로 되돌린다.</summary>
    [Fact]
    public void PressingBackReturnsToWhereItStarted()
    {
        for (var pressed = 0; pressed < 8; pressed++)
        {
            var back = TabletRotation.PressesBack(pressed);
            Assert.InRange(back, 0, TabletRotation.Steps - 1);
            Assert.Equal(0, (pressed + back) % TabletRotation.Steps);
        }
    }
}
