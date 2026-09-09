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
        public const int CurrentVersion = 1;
        public static string CurrentCoreBuild =>
            typeof(PlanBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion +
            "/" + typeof(PlanBuilder).Assembly.ManifestModule.ModuleVersionId;

        public int Version { get; set; }
        public int CatalogVersion { get; set; }
        public string CoreBuild { get; set; } = "";
        public string Producer { get; set; } = "";
        public string CapturedUtc { get; set; } = "";
        public long RequestedGeneration { get; set; }
        public long PublishedGeneration { get; set; }
        public string LatestError { get; set; } = "";
        public string CatalogGeneration { get; set; } = "";
        public string RequestFingerprint { get; set; } = "";
        public GameSnapshot? Snapshot { get; set; }
        public ReplayPreferences? Preferences { get; set; }
        public ReplayCatalog? Catalog { get; set; }
        public List<PlanTarget>? PreviousTargets { get; set; }
        public ReplayResult? Expected { get; set; }

        public Plan Rebuild(bool allowModelChange = false)
        {
            if (Version != CurrentVersion || CatalogVersion != PlannerData.CatalogVersion)
                throw new InvalidDataException("지원하지 않는 재현 자료 또는 카탈로그 형식입니다.");
            if (CoreBuild.Length == 0 || (!allowModelChange && CoreBuild != CurrentCoreBuild))
                throw new InvalidDataException("계산 코드가 저장 당시와 다릅니다. 변경 전후 비교에는 --allow-model-change를 지정하세요.");
            if (Snapshot?.Inventory is null || Preferences is null || Catalog is null || PreviousTargets is null ||
                Expected?.Facts is null || Expected.Scores is null || Expected.Facts.Count == 0 || Expected.Scores.Count == 0 ||
                CatalogGeneration.Length == 0 || PublishedGeneration <= 0 || RequestedGeneration < PublishedGeneration)
                throw new InvalidDataException("재현에 필요한 입력이나 기준 결과가 없습니다.");

            var preferences = Preferences.Restore();
            if (PlanFingerprint.Full(Snapshot, preferences, CatalogGeneration) != RequestFingerprint)
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

        public Catalog Restore()
        {
            if (Tablets is null || Charms is null || Combos is null ||
                Tablets.Any(item => item is null) || Charms.Any(item => item is null) || Combos.Any(item => item is null))
                throw new InvalidDataException("재현 카탈로그에 필요한 목록이 없습니다.");
            if (Tablets.Select(item => item.EntityId).Distinct().Count() != Tablets.Count ||
                Charms.Select(item => item.EntityId).Distinct().Count() != Charms.Count ||
                Combos.Select(item => item.Id).Distinct().Count() != Combos.Count)
                throw new InvalidDataException("재현 카탈로그에 중복 식별자가 있습니다.");
            return new Catalog(Tablets, Charms, Combos);
        }
    }

    public sealed class ReplayPreferences
    {
        public HashSet<string>? PriorityCategories { get; set; }
        public Dictionary<int, int>? PinnedCharms { get; set; }
        public HashSet<int>? HeldCharms { get; set; }
        public HashSet<int>? RetainedCharms { get; set; }
        public HashSet<int>? DeactivationAllowed { get; set; }
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
                PresetCharms = new HashSet<int>(PresetCharms),
                CharmValues = new CharmValueBook(CharmValues),
                Recommendations = Recommendations.Value,
            };
        }
    }
}
