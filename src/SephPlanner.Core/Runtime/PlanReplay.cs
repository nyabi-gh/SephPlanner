using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Runtime
{
    // 게시된 계획의 입력이다. F10 순간의 새 관측값과 섞지 않는다.
    public sealed class PlanReplay
    {
        public const int CurrentVersion = 9;
        public static string CurrentCoreBuild =>
            typeof(PlanBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion +
            "/" + typeof(PlanBuilder).Assembly.ManifestModule.ModuleVersionId;

        public int Version { get; set; }
        public int CatalogVersion { get; set; }

        /// <summary>
        /// 지금 재생할 수 있는 카탈로그 형식.
        ///
        /// 형식이 올라갔다고 해서 옛 자료가 곧바로 못 읽는 것은 아니다. 더한 것뿐이면 빠진 항목은
        /// 수집하지 않은 것으로 읽히므로 그때의 결과가 그대로 나온다. 카탈로그 형식 검사는
        /// <c>--allow-model-change</c> 로도 넘을 수 없는 문턱이라, 여기서 빼면 그 버전으로 모은
        /// 제보가 영영 재생되지 않는다. 20 은 0.3.3·0.3.4 가 쓴 형식이다.
        ///
        /// 읽던 값의 뜻이 바뀌거나 사라지는 변경이면 여기에 남기지 말고 잘라낸다.
        /// </summary>
        private static readonly int[] SupportedCatalogVersions = { PlannerData.CatalogVersion, 22, 21, 20 };
        public string CoreBuild { get; set; } = "";
        public string Producer { get; set; } = "";
        public string CapturedUtc { get; set; } = "";
        public long RequestedGeneration { get; set; }
        public long PublishedGeneration { get; set; }
        public string LatestError { get; set; } = "";
        public string CatalogGeneration { get; set; } = "";
        public string RequestFingerprint { get; set; } = "";

        /// <summary>
        /// 잡은 계획에 조언까지 붙어 있었는가. 배치는 조언보다 먼저 게시되므로 F10 이 그 사이를
        /// 잡을 수 있다. 재현은 언제나 조언까지 풀기 때문에, 그때의 기준 결과와 그대로 견주면
        /// "아직 안 푼 것" 이 "다른 답" 으로 보고된다.
        ///
        /// 옛 자료에는 이 항목이 없어 <c>true</c> 로 읽히고, 그때는 조언이 배치와 한 번에
        /// 계산됐으므로 그것이 사실과 같다.
        /// </summary>
        public bool AdviceComplete { get; set; } = true;
        public GameSnapshot? Snapshot { get; set; }
        public ReplayPreferences? Preferences { get; set; }
        public ReplayCatalog? Catalog { get; set; }
        public List<PlanTarget>? PreviousTargets { get; set; }
        public ReplayResult? Expected { get; set; }

        public Plan Rebuild(bool allowModelChange = false)
        {
            var legacy = Version == 1 && CatalogVersion == 15;
            if (!legacy && (Version != CurrentVersion || !SupportedCatalogVersions.Contains(CatalogVersion)))
                throw new InvalidDataException("지원하지 않는 재현 자료 또는 카탈로그 형식입니다.");
            if (CoreBuild.Length == 0 || (!allowModelChange && CoreBuild != CurrentCoreBuild))
                throw new InvalidDataException("계산 코드가 저장 당시와 다릅니다. 변경 전후 비교에는 --allow-model-change를 지정하세요.");
            if (Snapshot?.Inventory is null || Preferences is null || Catalog is null || PreviousTargets is null ||
                Expected?.Facts is null || Expected.Scores is null || Expected.Facts.Count == 0 || Expected.Scores.Count == 0 ||
                CatalogGeneration.Length == 0 || PublishedGeneration <= 0 || RequestedGeneration < PublishedGeneration)
                throw new InvalidDataException("재현에 필요한 입력이나 기준 결과가 없습니다.");

            var preferences = Preferences.Restore();
            var matches = legacy
                ? PlanFingerprint.MatchesLegacyReplay(Snapshot, preferences, CatalogGeneration, RequestFingerprint)
                : PlanFingerprint.Full(Snapshot, preferences, CatalogGeneration) == RequestFingerprint;
            if (!matches)
                throw new InvalidDataException("스냅샷·설정의 지문이 게시된 계획과 다릅니다.");
            var previous = new Plan { Targets = PreviousTargets };
            return PlanBuilder.Build(Snapshot, Catalog.Restore(), preferences, out var blocker, previous) ??
                throw new InvalidDataException("저장된 입력으로 계획을 만들지 못했습니다: " + blocker);
        }
    }

    public sealed class ReplayCatalog
    {
        public List<TabletDefinition>? Tablets { get; set; }
        public List<CharmDefinition>? Charms { get; set; }
        public List<ComboDefinition>? Combos { get; set; }

        /// <summary>저장 당시의 점수 눈금. 옛 재현 자료에는 없으므로 그때는 기본값을 쓴다.</summary>
        public WorthScale? Scale { get; set; }

        public Catalog Restore()
        {
            if (Tablets is null || Charms is null || Combos is null ||
                Tablets.Any(item => item is null) || Charms.Any(item => item is null) || Combos.Any(item => item is null))
                throw new InvalidDataException("재현 카탈로그에 필요한 목록이 없습니다.");
            if (Tablets.Select(item => item.EntityId).Distinct().Count() != Tablets.Count ||
                Charms.Select(item => item.EntityId).Distinct().Count() != Charms.Count ||
                Combos.Select(item => item.Id).Distinct().Count() != Combos.Count)
                throw new InvalidDataException("재현 카탈로그에 중복 식별자가 있습니다.");
            return new Catalog(Tablets, Charms, Combos, Scale);
        }
    }

    public sealed class ReplayPreferences
    {
        public HashSet<string>? PriorityCategories { get; set; }
        public Dictionary<int, int>? PinnedCharms { get; set; }
        public HashSet<int>? HeldCharms { get; set; }
        public HashSet<int>? RetainedCharms { get; set; }
        public HashSet<int>? DeactivationAllowed { get; set; }

        /// <summary>없던 시절의 자료에는 이 항목이 없다. 그때는 제한을 건 것이 없다는 뜻이다.</summary>
        public Dictionary<int, int>? LevelCaps { get; set; }

        /// <summary>같은 이유로 없을 수 있다. 없으면 지정한 강화 대상이 없다는 뜻이다.</summary>
        public HashSet<int>? SupportTargets { get; set; }
        public HashSet<int>? PresetCharms { get; set; }
        public CharmValueFile? CharmValues { get; set; }
        public bool? Recommendations { get; set; }

        public static ReplayPreferences From(PlanPreferences preferences) => new ReplayPreferences
        {
            PriorityCategories = new HashSet<string>(preferences.PriorityCategories),
            PinnedCharms = new Dictionary<int, int>(preferences.PinnedCharms),
            HeldCharms = new HashSet<int>(preferences.HeldCharms),
            RetainedCharms = new HashSet<int>(preferences.RetainedCharms),
            DeactivationAllowed = new HashSet<int>(preferences.DeactivationAllowed),
            LevelCaps = new Dictionary<int, int>(preferences.LevelCaps),
            SupportTargets = new HashSet<int>(preferences.SupportTargets),
            PresetCharms = new HashSet<int>(preferences.PresetCharms),
            CharmValues = preferences.CharmValues.Export(),
            Recommendations = preferences.Recommendations,
        };

        public PlanPreferences Restore()
        {
            if (PriorityCategories is null || PinnedCharms is null || HeldCharms is null || RetainedCharms is null ||
                DeactivationAllowed is null || PresetCharms is null || CharmValues?.Charms is null || !Recommendations.HasValue)
                throw new InvalidDataException("재현 자료에 계산 설정이 빠졌습니다.");
            return new PlanPreferences
            {
                PriorityCategories = new HashSet<string>(PriorityCategories),
                PinnedCharms = new Dictionary<int, int>(PinnedCharms),
                HeldCharms = new HashSet<int>(HeldCharms),
                RetainedCharms = new HashSet<int>(RetainedCharms),
                DeactivationAllowed = new HashSet<int>(DeactivationAllowed),
                LevelCaps = LevelCaps is null
                    ? new Dictionary<int, int>()
                    : new Dictionary<int, int>(LevelCaps),
                SupportTargets = SupportTargets is null
                    ? new HashSet<int>()
                    : new HashSet<int>(SupportTargets),
                PresetCharms = new HashSet<int>(PresetCharms),
                CharmValues = new CharmValueBook(CharmValues),
                Recommendations = Recommendations.Value,
            };
        }
    }
}
