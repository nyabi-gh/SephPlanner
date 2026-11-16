using System;
using System.Collections.Generic;
using System.Reflection;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Extensions
{
    /// <summary>확장을 등록하는 곳. 확장 모드는 자기 <c>OnModLoaded</c>에서 한 번 부른다.</summary>
    public static class PlanExtensions
    {
        public static ExtensionRegistry Shared { get; } = new ExtensionRegistry();

        public static void Register(IPlanExtension extension) => Shared.Register(extension);
    }

    /// <summary>진단 파일과 재현 자료에 남기는 확장 하나의 이름과 상태.</summary>
    public sealed class ExtensionStamp
    {
        public string Id { get; set; } = "";

        /// <summary>확장 어셈블리의 버전. 창작마당 글의 버전과 다를 수 있다.</summary>
        public string Version { get; set; } = "";

        /// <summary>예외를 던져 이번 판 동안 꺼졌는가.</summary>
        public bool Disabled { get; set; }
    }

    /// <summary>
    /// 등록된 확장을 불러 기본 값어치 표 위에 덮는다. 확장 하나가 실패해도 플래너는 기본 표로 계속 돈다.
    /// 등록한 순서대로 덮으므로 같은 아티팩트를 둘이 건드리면 나중에 등록한 쪽이 이긴다.
    /// </summary>
    public sealed class ExtensionRegistry
    {
        private sealed class Slot
        {
            public IPlanExtension Extension = null!;
            public string Id = "";
            public string Version = "";
            public bool Disabled;
        }

        private readonly object _gate = new object();
        private readonly List<Slot> _slots = new List<Slot>();

        public void Register(IPlanExtension extension)
        {
            if (extension is null) throw new ArgumentNullException(nameof(extension));
            var id = extension.Id;
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("확장 이름(Id)이 비어 있습니다.", nameof(extension));

            lock (_gate)
            {
                if (_slots.Exists(slot => slot.Id == id))
                    throw new ArgumentException("같은 이름의 확장이 이미 등록돼 있습니다: " + id, nameof(extension));
                _slots.Add(new Slot { Extension = extension, Id = id, Version = VersionOf(extension) });
            }
        }

        public bool IsEmpty
        {
            get { lock (_gate) return _slots.Count == 0; }
        }

        /// <summary>
        /// 켜진 확장의 표를 차례로 덮는다. 확장이 예외를 던지거나 쓸 수 없는 표를 돌려주면 그 확장만 끄고
        /// <paramref name="warn"/>으로 알린다. 돌려받은 표는 복사해 쓰므로 확장이 나중에 고쳐도 계산 중인 계획에
        /// 번지지 않는다.
        /// </summary>
        public CharmValueBook Overlay(GameSnapshot snapshot, CharmValueBook baseline, Action<string> warn)
        {
            Slot[] slots;
            lock (_gate) slots = _slots.ToArray();

            var book = baseline;
            foreach (var slot in slots)
            {
                if (slot.Disabled) continue;
                List<CharmValueEntry>? entries;
                try
                {
                    entries = Validate(slot.Extension.Values(snapshot));
                }
                catch (Exception ex)
                {
                    lock (_gate) slot.Disabled = true;
                    warn($"확장 {slot.Id}({slot.Version})이 실패해 이번 판 동안 끕니다: {ex}");
                    continue;
                }
                if (entries != null) book = book.Overlay(entries);
            }
            return book;
        }

        /// <summary>새 판이 시작되면 꺼 둔 확장을 다시 켠다.</summary>
        public void Restore()
        {
            lock (_gate)
            {
                foreach (var slot in _slots) slot.Disabled = false;
            }
        }

        public List<ExtensionStamp> Stamps()
        {
            lock (_gate)
                return _slots.ConvertAll(slot => new ExtensionStamp { Id = slot.Id, Version = slot.Version, Disabled = slot.Disabled });
        }

        private static List<CharmValueEntry>? Validate(CharmValueFile? file)
        {
            if (file is null) return null;
            if (file.Charms is null) throw new InvalidOperationException("값어치 표에 Charms 목록이 없습니다.");

            var entries = new List<CharmValueEntry>(file.Charms.Count);
            foreach (var entry in file.Charms)
            {
                if (entry is null) throw new InvalidOperationException("값어치 표에 빈 항목이 있습니다.");
                var copy = entry.Copy();
                if (copy.EntityId <= 0 && copy.Id.Length == 0)
                    throw new InvalidOperationException("엔티티 번호도 식별자도 없는 항목이 있습니다.");
                if (!IsFinite(copy.Base) || !IsFinite(copy.PerLevel) ||
                    (copy.ByLevel != null && !copy.ByLevel.TrueForAll(double.IsFinite)))
                    throw new InvalidOperationException("유한하지 않은 값어치가 있습니다: " + Describe(copy));
                entries.Add(copy);
            }
            return entries;
        }

        private static bool IsFinite(double? value) =>
            !value.HasValue || double.IsFinite(value.Value);

        private static string Describe(CharmValueEntry entry) =>
            entry.Id.Length > 0 ? entry.Id : entry.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string VersionOf(IPlanExtension extension)
        {
            var assembly = extension.GetType().Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "";
        }
    }
}
