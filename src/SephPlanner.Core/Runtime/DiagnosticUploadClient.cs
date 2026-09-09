using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SephPlanner.Core.Runtime
{
    public sealed class DiagnosticUploadClient : IDisposable
    {
        public const string DefaultEndpoint = "https://sephplanner.nyabi.me/api/v1/reports";
        private readonly HttpClient _http;

        public DiagnosticUploadClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

        public DiagnosticUploadClient(HttpMessageHandler handler)
        {
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public static string ConsentKey(Uri endpoint) => DiagnosticArchive.SchemaVersion + "|" + endpoint.AbsoluteUri;

        public async Task<string> SendAsync(Uri endpoint, string consent, Guid reportId, byte[] archive, CancellationToken cancellation)
        {
            if (endpoint.Scheme != Uri.UriSchemeHttps || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
                endpoint.Query.Length != 0 || consent != ConsentKey(endpoint))
                throw new InvalidOperationException("진단 수신 주소에 대한 전송 동의가 없습니다.");
            if (reportId == Guid.Empty || archive.Length == 0 || archive.Length > DiagnosticArchive.MaximumArchiveBytes)
                throw new InvalidDataException("전송할 진단 자료가 올바르지 않습니다.");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("X-SephPlanner-Report-Id", reportId.ToString("N"));
            request.Headers.Add("X-SephPlanner-SHA256", DiagnosticArchive.Hash(archive));
            request.Content = new ByteArrayContent(archive);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Created && response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException("진단 서버가 전송을 받지 못했습니다. HTTP " + (int)response.StatusCode);
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var bytes = new byte[33];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count, bytes.Length - count), cancellation).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            var receipt = System.Text.Encoding.ASCII.GetString(bytes, 0, count);
            if (receipt != reportId.ToString("N")) throw new InvalidDataException("진단 접수 번호가 일치하지 않습니다.");
            return receipt;
        }

        public void Dispose() => _http.Dispose();
    }
}
