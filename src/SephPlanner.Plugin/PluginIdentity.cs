using SephPlanner.Plugin.Host;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 어느 빌드가 어느 게임 위에서 어느 카탈로그로 돌고 있는지. 제보 하나를 진단할 때마다
    /// 필요한 값들인데 로그에도 덤프에도 없어서, 되물어야만 알 수 있었다.
    /// </summary>
    internal static class PluginIdentity
    {
        /// <summary>
        /// BepInEx 플러그인 GUID 이자 Harmony 패치 주인 이름. BepInEx 설정 파일 이름이 여기서 나오므로 바꾸지 않는다.
        /// </summary>
        public const string Guid = "dev.nyabi.sephplanner.bridge";

        public static string Describe()
        {
            var generation = CatalogDump.ActiveGeneration;
            var catalog = generation.Length == 0
                ? "없음"
                : generation + (CatalogDump.QueryVerificationPassed() ? " (검증 통과)" : " (검증 실패)");

            return $"SephPlanner {typeof(PluginIdentity).Assembly.GetName().Version} - " +
                   $"게임 {Application.version} / {CatalogDump.GameAssemblyId()}, " +
                   $"{PluginHost.Loader}, 카탈로그 {catalog}";
        }
    }
}
