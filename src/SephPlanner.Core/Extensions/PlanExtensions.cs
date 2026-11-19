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

        /// <summary>예외를 던지거나 쓸 수 없는 표를 돌려줘 새 판까지 꺼졌는가.</summary>
        public bool Disabled { get; set; }

        /// <summary>
        /// 마지막으로 HUD 에 얹은 글자(<see cref="IPlanExtensionNotes"/>). 플레이어가 무엇을 보고 제보했는지 가르는
        /// 단서라 진단 파일에 남긴다. 얹은 것이 없으면 <c>null</c>.
        /// </summary>
        public ExtensionNotes? Notes { get; set; }
    }

    /// <summary>F3 "확장 모드" 묶음에 그릴 확장 하나의 줄들.</summary>
    public sealed class ExtensionOptionGroup
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>실패해 새 판까지 꺼졌는가. 그때는 줄을 받지 않고 이름만 보여 준다.</summary>
        public bool Disabled { get; set; }

        public List<ExtensionOption> Options { get; set; } = new List<ExtensionOption>();
    }

    /// <summary>
    /// 등록된 확장을 불러 기본 값어치 표 위에 덮는다. 확장 하나가 실패해도 플래너는 기본 표로 계속 돈다.
    /// 등록한 순서대로 덮으므로 같은 아티팩트를 둘이 건드리면 나중에 등록한 쪽이 이긴다.
    /// </summary>
    public sealed class ExtensionRegistry
    {
        /// <summary>확장 하나가 후보 목록 위에 얹을 수 있는 줄 수.</summary>
        public const int MaxLines = 3;

        /// <summary>
        /// 후보 꼬리표의 글자 수 상한. 꼬리표는 플래너의 콤보·가격·증가분과 한 칸을 나눠 쓰므로, 길면 그것들을 밀어낸다.
        /// </summary>
        public const int MaxTagLength = 12;

        private sealed class Slot
        {
            public IPlanExtension Extension = null!;
            public string Id = "";
            public string Version = "";
            public bool Disabled;
            public ExtensionNotes? Notes;
        }

        private readonly object _gate = new object();
        private readonly List<Slot> _slots = new List<Slot>();
        private PlanNotes _notes = PlanNotes.Empty;

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
                    Fail(slot, ex, warn);
                    continue;
                }
                if (entries != null) book = book.Overlay(entries);
            }
            return book;
        }

        /// <summary>
        /// 켜진 확장의 HUD 글자를 받아 합친다. <see cref="Overlay"/>와 같은 때 부른다. 줄은 등록한 순서대로 잇고,
        /// 같은 후보에 둘이 꼬리표를 주면 값어치 표처럼 나중에 등록한 쪽이 이긴다. 내용이 직전과 같으면 직전 객체를
        /// 그대로 돌려준다 - HUD 는 객체가 바뀌어야 다시 그린다.
        /// </summary>
        public PlanNotes Notes(GameSnapshot snapshot, ExtensionNoteContext context, Action<string> warn)
        {
            Slot[] slots;
            lock (_gate) slots = _slots.ToArray();

            var lines = new List<string>();
            var candidates = new Dictionary<int, CandidateNote>();
            foreach (var slot in slots)
            {
                ExtensionNotes? notes = null;
                if (!slot.Disabled && slot.Extension is IPlanExtensionNotes source)
                {
                    try
                    {
                        notes = Validate(source.Notes(snapshot, context));
                    }
                    catch (Exception ex)
                    {
                        Fail(slot, ex, warn);
                        continue;
                    }
                }
                lock (_gate) slot.Notes = notes;
                if (notes == null) continue;

                lines.AddRange(notes.Lines);
                foreach (var pair in notes.Candidates) candidates[pair.Key] = pair.Value;
            }

            var merged = lines.Count == 0 && candidates.Count == 0 ? PlanNotes.Empty : new PlanNotes(lines, candidates);
            lock (_gate)
            {
                if (!merged.SameAs(_notes)) _notes = merged;
                return _notes;
            }
        }

        /// <summary>
        /// F3 에 그릴 줄들. 줄의 읽기·쓰기는 감싸서 돌려주므로, 확장이 거기서 예외를 던져도 창은 그대로 있고 그
        /// 확장만 새 판까지 꺼진다.
        /// </summary>
        public List<ExtensionOptionGroup> OptionGroups(Action<string> warn)
        {
            Slot[] slots;
            lock (_gate) slots = _slots.ToArray();

            var groups = new List<ExtensionOptionGroup>();
            foreach (var slot in slots)
            {
                if (!(slot.Extension is IPlanExtensionOptions source)) continue;

                var group = new ExtensionOptionGroup { Id = slot.Id, Disabled = slot.Disabled };
                try
                {
                    group.Title = OneLine(source.Title);
                    if (!group.Disabled)
                        foreach (var option in source.Options() ?? Array.Empty<ExtensionOption>())
                        {
                            var wrapped = Wrap(slot, option, warn);
                            if (wrapped != null) group.Options.Add(wrapped);
                        }
                }
                catch (Exception ex)
                {
                    Fail(slot, ex, warn);
                    group.Disabled = true;
                    group.Options.Clear();
                }
                if (group.Title.Length == 0) group.Title = slot.Id;
                groups.Add(group);
            }
            return groups;
        }

        private ExtensionOption? Wrap(Slot slot, ExtensionOption? option, Action<string> warn)
        {
            if (option?.Choices == null || option.Choices.Length == 0) return null;

            var choices = Array.ConvertAll(option.Choices, choice => OneLine(choice));
            var read = option.Read;
            var write = option.Write;
            return new ExtensionOption
            {
                Label = OneLine(option.Label),
                Choices = choices,
                Read = () =>
                {
                    if (slot.Disabled || read == null) return 0;
                    try
                    {
                        return read();
                    }
                    catch (Exception ex)
                    {
                        Fail(slot, ex, warn);
                        return 0;
                    }
                },
                Write = index =>
                {
                    if (slot.Disabled || write == null) return;
                    try
                    {
                        write(index);
                    }
                    catch (Exception ex)
                    {
                        Fail(slot, ex, warn);
                    }
                },
            };
        }

        private void Fail(Slot slot, Exception ex, Action<string> warn)
        {
            lock (_gate)
            {
                if (slot.Disabled) return;
                slot.Disabled = true;
                slot.Notes = null;
            }
            warn($"확장 {slot.Id}({slot.Version})이 실패해 이번 판 동안 끕니다: {ex}");
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
                return _slots.ConvertAll(slot => new ExtensionStamp
                {
                    Id = slot.Id,
                    Version = slot.Version,
                    Disabled = slot.Disabled,
                    Notes = Copy(slot.Notes),
                });
        }

        private static ExtensionNotes? Copy(ExtensionNotes? notes)
        {
            if (notes == null) return null;
            var copy = new ExtensionNotes { Lines = new List<string>(notes.Lines) };
            foreach (var pair in notes.Candidates) copy.Candidates[pair.Key] = pair.Value.Copy();
            return copy;
        }

        /// <summary>
        /// 받은 글자를 HUD 에 그릴 수 있는 모양으로 다듬는다. 확장이 나중에 고쳐도 번지지 않게 복사한다.
        /// 아무것도 남지 않으면 <c>null</c>.
        /// </summary>
        private static ExtensionNotes? Validate(ExtensionNotes? notes)
        {
            if (notes == null) return null;

            var result = new ExtensionNotes();
            foreach (var line in notes.Lines ?? new List<string>())
            {
                if (result.Lines.Count >= MaxLines) break;
                var text = OneLine(line);
                if (text.Length > 0) result.Lines.Add(text);
            }
            foreach (var pair in notes.Candidates ?? new Dictionary<int, CandidateNote>())
            {
                if (pair.Key <= 0 || pair.Value == null) continue;
                var tag = OneLine(pair.Value.Tag);
                if (tag.Length > MaxTagLength) tag = tag.Substring(0, MaxTagLength - 1) + "…";
                var description = (pair.Value.Description ?? "").Trim();
                if (tag.Length == 0 && description.Length == 0) continue;
                result.Candidates[pair.Key] = new CandidateNote { Tag = tag, Description = description };
            }
            return result.Lines.Count == 0 && result.Candidates.Count == 0 ? null : result;
        }

        private static string OneLine(string? text) =>
            string.IsNullOrEmpty(text) ? "" : text!.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

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
