using System;

namespace SephPlanner.Core.Combat
{
    internal sealed class CombatClock
    {
        private double _time;
        private double _error;

        internal double Advance(double start, double interval)
        {
            if (start != _time) { _time = start; _error = 0; }
            if (double.IsInfinity(interval)) { _time = interval; _error = 0; return _time; }
            // 소수 주기를 반복해 더할 때 종료 경계를 넘나들지 않도록 합산 오차를 보존한다.
            var adjusted = interval - _error;
            var next = _time + adjusted;
            _error = (next - _time) - adjusted;
            _time = next;
            return next;
        }
    }

    internal sealed class CombatMana
    {
        private readonly double _maximum;
        private readonly double _regeneration;
        private readonly double _delay;
        private double _balance;
        private double _regenerationStarts;

        internal CombatMana(double maximum, double initial, double regeneration, double delay)
        { _maximum = maximum; _balance = initial; _regeneration = regeneration; _delay = delay; }

        internal double At(double time) => Math.Min(_maximum,
            _balance + Math.Max(0, time - _regenerationStarts) * _regeneration);

        internal double AvailableAt(int cost) => cost <= _balance ? 0 : cost > _maximum || _regeneration <= 0 ?
            double.PositiveInfinity : _regenerationStarts + (cost - _balance) / _regeneration;

        internal void Spend(int cost, double time)
        {
            if (time < AvailableAt(cost)) throw new InvalidOperationException("마나가 회복되기 전에 소비하려고 했습니다.");
            // 충족 시각으로 판정한다. 같은 시각을 마나로 역산한 미세 오차를 다시 부족으로 판단하지 않는다.
            _balance = Math.Max(0, At(time) - cost);
            _regenerationStarts = time + _delay;
        }
    }
}
