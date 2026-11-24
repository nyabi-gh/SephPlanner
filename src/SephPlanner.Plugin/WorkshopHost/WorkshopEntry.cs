using SephPlanner.Plugin.Host;
using UnityEngine;

namespace SephPlanner.Plugin.Workshop
{
    /// <summary>
    /// 게임의 창작마당 모드 로더가 부르는 진입점(metadata.json 의 <c>entryClass</c>). 타이틀이 아니라 첫 월드 입장 때
    /// 데이터베이스보다 먼저 한 번 불리고, 프로세스가 끝날 때까지 내려가지 않는다 - 1.1.1 의 <c>WorkshopDllActivator</c> 는
    /// 이미 켠 모드를 다시 만들지 않고, 창작마당 모드에 <c>UnloadMod</c> 를 부르는 곳이 없다.
    ///
    /// 이 클래스는 MonoBehaviour 가 아니어서 플래너를 따로 만든 오브젝트에 붙이고 장면이 바뀌어도 남긴다.
    /// </summary>
    public sealed class WorkshopEntry : WorkshopModBase
    {
        /// <summary>Player.log 에서 우리 줄을 찾는 머리. 진단이 게임 로그를 모을 때 이것으로 우리 줄을 거른다.</summary>
        private const string LogPrefix = "[SephPlanner] ";

        protected override void OnModLoaded()
        {
            Log.Use(Write);
            var legacy = BepInExConfigImport.Read(PluginHost.GameFolder);
            PluginHost.Use(new ModOptionsSettingStore(Context.Options, legacy),
                $"창작마당 {Context.Definition.ModId} {Context.Definition.Version} #{Context.FileId}", "Player.log", LogPrefix);
            var host = new GameObject("SephPlanner");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<SephPlannerPlugin>();
        }

        private static void Write(LogLevel level, object message)
        {
            switch (level)
            {
                case LogLevel.Error: Debug.LogError(LogPrefix + message); break;
                case LogLevel.Warning: Debug.LogWarning(LogPrefix + message); break;
                default: Debug.Log(LogPrefix + message); break;
            }
        }
    }
}
