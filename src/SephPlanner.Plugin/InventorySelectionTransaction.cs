#nullable enable
using System;

namespace SephPlanner.Plugin
{
    internal static class InventorySelectionTransaction
    {
        public static void Run<T>(T? selected, Func<T?> current, Action<T?> select,
            Func<bool> canRestore, Action write, Action<Exception> report) where T : class
        {
            try
            {
                // 선택 상세는 아이템 목록과 효과 객체의 좌표가 모두 반영된 뒤에 다시 읽는다.
                if (selected != null) select(null);
                write();
            }
            finally
            {
                try
                {
                    if (selected != null && current() == null && canRestore()) select(selected);
                }
                catch (Exception ex)
                {
                    // 표시 복원 실패가 먼저 발생한 교환 예외를 덮어쓰지 않게 한다.
                    report(ex);
                }
            }
        }
    }
}
