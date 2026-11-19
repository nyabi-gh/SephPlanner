using System;
using System.Collections.Generic;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Core.Extensions
{
    /// <summary>
    /// 확장이 HUD 에 글자를 얹는 통로. <b>실험적이다.</b> <see cref="IPlanExtension"/>을 구현한 같은 객체에 붙인다.
    ///
    /// 받은 글자는 색과 서식을 플래너가 정하고, 서식 태그는 글자 그대로 보인다. 진단 파일(<c>report.json</c>)에도
    /// 남는다 - 플레이어가 무엇을 보고 제보했는지 알아야 해서다.
    /// </summary>
    public interface IPlanExtensionNotes
    {
        /// <summary>
        /// <see cref="IPlanExtension.Values"/>와 같은 때, 계획을 지을 때 한 번 메인 스레드에서 불린다. HUD 는 받아 둔
        /// 것을 그린다. <c>null</c>이면 아무것도 얹지 않는다. 예외를 던지면 이 확장은 새 판이 시작될 때까지 꺼진다.
        /// </summary>
        ExtensionNotes? Notes(GameSnapshot snapshot, ExtensionNoteContext context);
    }

    /// <summary>
    /// 확장이 읽기만 하는 플레이어 지정. 플레이어가 직접 정한 것은 계산이 추정한 순서보다 앞서므로, 확장의 순서와
    /// 어긋날 수 있다는 것을 확장이 알리게 한다.
    /// </summary>
    public sealed class ExtensionNoteContext
    {
        public static readonly ExtensionNoteContext None = new ExtensionNoteContext(false, Array.Empty<string>());

        public ExtensionNoteContext(bool hasImportedBuild, IEnumerable<string> priorityCategories)
        {
            HasImportedBuild = hasImportedBuild;
            PriorityCategories = new List<string>(priorityCategories).AsReadOnly();
        }

        /// <summary>빌드 창에서 가져온 빌드 코드가 있어 그 아티팩트를 후보 맨 위로 올리고 있는가.</summary>
        public bool HasImportedBuild { get; }

        /// <summary>플레이어가 빌드 창에서 콤보 우선으로 고른 카테고리 식별자. 가져온 빌드가 미는 것은 들어가지 않는다.</summary>
        public IReadOnlyList<string> PriorityCategories { get; }
    }

    /// <summary>확장 하나가 HUD 에 얹는 것.</summary>
    public sealed class ExtensionNotes
    {
        /// <summary>후보 목록 바로 위 줄. 최대 <see cref="ExtensionRegistry.MaxLines"/>줄, 한 줄씩 넘치면 자른다.</summary>
        public List<string> Lines { get; set; } = new List<string>();

        /// <summary>아티팩트 정의 번호(<c>OfferCandidate.DefinitionId</c>)별 후보 꼬리표.</summary>
        public Dictionary<int, CandidateNote> Candidates { get; set; } = new Dictionary<int, CandidateNote>();
    }

    /// <summary>
    /// 후보 줄 하나에 붙는 것. 줄 끝에는 플래너의 증가분 숫자가 이미 있으므로 꼬리표에는 숫자를 넣지 않는다 -
    /// 뜻이 다른 숫자 둘이 한 줄에 서면 헷갈린다. 수치는 <see cref="Description"/>으로 보낸다.
    /// </summary>
    public sealed class CandidateNote
    {
        /// <summary>줄 오른쪽 맨 앞에 붙는 짧은 글자. 8글자 안팎(예: <c>[S] 추천</c>).</summary>
        public string Tag { get; set; } = "";

        /// <summary>그 후보에 마우스를 올렸을 때 뜨는 설명 끝에 덧붙는 글.</summary>
        public string Description { get; set; } = "";

        public CandidateNote Copy() => new CandidateNote { Tag = Tag, Description = Description };
    }

    /// <summary>켜진 확장들의 글자를 합친 것. HUD 가 그린다.</summary>
    public sealed class PlanNotes
    {
        public static readonly PlanNotes Empty =
            new PlanNotes(new List<string>(), new Dictionary<int, CandidateNote>());

        private readonly Dictionary<int, CandidateNote> _candidates;

        internal PlanNotes(List<string> lines, Dictionary<int, CandidateNote> candidates)
        {
            Lines = lines.AsReadOnly();
            _candidates = candidates;
        }

        public IReadOnlyList<string> Lines { get; }

        public bool IsEmpty => Lines.Count == 0 && _candidates.Count == 0;

        public CandidateNote? Candidate(int definitionId) =>
            _candidates.TryGetValue(definitionId, out var note) ? note : null;

        internal bool SameAs(PlanNotes other)
        {
            if (Lines.Count != other.Lines.Count || _candidates.Count != other._candidates.Count) return false;
            for (var i = 0; i < Lines.Count; i++)
                if (!string.Equals(Lines[i], other.Lines[i], StringComparison.Ordinal)) return false;
            foreach (var pair in _candidates)
            {
                if (!other._candidates.TryGetValue(pair.Key, out var theirs)) return false;
                if (!string.Equals(pair.Value.Tag, theirs.Tag, StringComparison.Ordinal) ||
                    !string.Equals(pair.Value.Description, theirs.Description, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }
}
