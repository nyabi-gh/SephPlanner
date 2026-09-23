using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// GitHub Releases 에서 최신 안정판을 찾고 그 zip 을 받는다.
    ///
    /// API 를 쓰지 않는다. <c>releases/latest</c> 가 최신 안정판 태그로 302 를 돌려주므로 그
    /// <c>Location</c> 한 줄이면 버전을 알 수 있고, 요청 한도도 JSON 파서도 필요 없다.
    /// 프리릴리스와 드래프트는 GitHub 가 거기서 이미 빼 준다.
    /// </summary>
    public sealed class UpdateClient : IDisposable
    {
        public const string Repository = "https://github.com/nyabi-gh/SephPlanner";
        public const string AssetPrefix = "SephPlanner-v";
        public const string MacAssetPrefix = "SephPlanner-macos-v";
        public const int MaximumAssetBytes = 32 * 1024 * 1024;
        private const int MaximumRedirects = 5;
        private const string TagPrefix = "/releases/tag/v";
        private readonly HttpClient _http;
        private readonly bool _mac;

        public UpdateClient(bool mac) : this(new HttpClientHandler { AllowAutoRedirect = false }, mac) { }

        public UpdateClient(HttpMessageHandler handler, bool mac)
        {
            _mac = mac;
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SephPlanner");
        }

        public static Uri LatestRelease => new Uri(Repository + "/releases/latest");

        public static Uri AssetOf(Version version, bool mac) =>
            new Uri(Repository + "/releases/download/v" + Format(version) + "/" +
                (mac ? MacAssetPrefix : AssetPrefix) + Format(version) + ".zip");

        /// <summary>
        /// 버전은 세 자리로만 견준다. 어셈블리 버전은 <c>0.3.9.0</c> 이고 태그는 <c>0.3.9</c> 인데,
        /// <see cref="Version"/> 은 빠진 자리를 -1 로 보아 같은 판을 다르다고 한다.
        /// </summary>
        public static Version Normalize(Version version) =>
            new Version(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));

        public static string Format(Version version) => Normalize(version).ToString(3);

        /// <summary>지금보다 새 안정판이 있으면 그 버전, 없으면 <c>null</c>.</summary>
        public async Task<Version?> CheckAsync(Version current, CancellationToken cancellation)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, LatestRelease);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location == null)
                throw new HttpRequestException("최신 버전을 확인하지 못했습니다. HTTP " + (int)response.StatusCode);
            var latest = ParseTag(new Uri(LatestRelease, response.Headers.Location));
            return latest > Normalize(current) ? latest : null;
        }

        /// <summary>
        /// 그 버전의 배포 zip 을 통째로 받는다. GitHub 는 자산을 다른 호스트로 넘기므로 리다이렉트를
        /// 따라가되, HTTPS 가 아니거나 자격 증명이 붙은 곳으로는 가지 않는다.
        /// </summary>
        public async Task<byte[]> DownloadAsync(Version version, CancellationToken cancellation)
        {
            var location = AssetOf(version, _mac);
            for (var hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, location);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    if (hop == MaximumRedirects || response.Headers.Location == null)
                        throw new HttpRequestException("배포 파일 주소를 따라가지 못했습니다.");
                    location = new Uri(location, response.Headers.Location);
                    if (location.Scheme != Uri.UriSchemeHttps || location.UserInfo.Length != 0)
                        throw new HttpRequestException("배포 파일이 안전하지 않은 주소로 넘어갔습니다.");
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new HttpRequestException("배포 파일을 받지 못했습니다. HTTP " + (int)response.StatusCode);
                var length = response.Content.Headers.ContentLength;
                if (length > MaximumAssetBytes) throw new InvalidDataException("배포 파일이 너무 큽니다.");
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = new MemoryStream(length.HasValue ? (int)length.Value : 0);
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + read > MaximumAssetBytes) throw new InvalidDataException("배포 파일이 너무 큽니다.");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static bool IsRedirect(HttpStatusCode status) =>
            status == HttpStatusCode.Moved || status == HttpStatusCode.Found ||
            status == HttpStatusCode.SeeOther || status == HttpStatusCode.TemporaryRedirect ||
            (int)status == 308;

        private static Version ParseTag(Uri tag)
        {
            var path = tag.AbsolutePath;
            var start = path.LastIndexOf(TagPrefix, StringComparison.Ordinal);
            if (tag.Host != "github.com" || start < 0 ||
                !Version.TryParse(path.AsSpan(start + TagPrefix.Length), out var version) || version.Build < 0)
                throw new InvalidDataException("최신 버전 태그를 읽지 못했습니다: " + tag);
            return Normalize(version);
        }

        public void Dispose() => _http.Dispose();
    }
}
