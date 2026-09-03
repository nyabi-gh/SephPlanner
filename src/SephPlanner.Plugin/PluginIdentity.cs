using BepInEx;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 어느 빌드가 어느 게임 위에서 어느 카탈로그로 돌고 있는지. 제보 하나를 진단할 때마다
    /// 필요한 값들인데 로그에도 덤프에도 없어서, 되물어야만 알 수 있었다.
    /// </summary>
    internal static class PluginIdentity
    {
        public static string Describe()
        {
            var generation = CatalogDump.ActiveGeneration;
            var catalog = generation.Length == 0
                ? "없음"
                : generation + (CatalogDump.QueryVerificationPassed() ? " (검증 통과)" : " (검증 실패)");

            return $"SephPlanner {typeof(PluginIdentity).Assembly.GetName().Version} - " +
                   $"게임 {Application.version} / {CatalogDump.GameAssemblyId()}, " +
                   $"BepInEx {typeof(BaseUnityPlugin).Assembly.GetName().Version}, 카탈로그 {catalog}";
        }
    }
}
