#nullable disable
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 손으로 채운 아티팩트 가치(<c>data/values/charms.json</c>). 게임에서 나오는 데이터가 아니라
    /// 우리가 만드는 데이터라서 DLL 안에 함께 들어 있다.
    /// </summary>
    internal static class CharmValueSource
    {
        private const string ResourceName = "SephPlanner.Values.Charms.json";

        private static CharmValueBook _book;

        public static CharmValueBook Book => _book ?? (_book = Load());

        private static CharmValueBook Load()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return CharmValueBook.Empty;

                    using (var reader = new StreamReader(stream))
                    {
                        var file = JsonConvert.DeserializeObject<CharmValueFile>(reader.ReadToEnd());
                        return new CharmValueBook(file);
                    }
                }
            }
            catch (Exception)
            {
                // 가치 데이터가 깨졌다고 플러그인이 죽으면 안 된다. 점수는 잰 값과 레어도로 물러선다.
                return CharmValueBook.Empty;
            }
        }
    }
}
