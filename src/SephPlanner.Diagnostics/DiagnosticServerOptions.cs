namespace SephPlanner.Diagnostics;

public sealed class DiagnosticServerOptions
{
    public string StorageDirectory { get; set; } = "/data/reports";
    public string AdminTokenFile { get; set; } = "/run/secrets/diagnostics_admin_token";
    // 비워 두면 Validate 가 막는다. 전달 헤더를 신뢰할 프록시는 배포가 지정하며 저장소에 적지 않는다.
    public string[] TrustedProxies { get; set; } = [];
    // 초기 운영 추정값이다. 실제 수집량과 저장 공간에 맞춰 환경 변수로 조정한다.
    public long MaximumStorageBytes { get; set; } = 512L * 1024 * 1024;
    public int MaximumReports { get; set; } = 1000;
    // 한 곳(IPv4 주소 하나, IPv6 /64 하나)이 보관함을 채워 남의 제보를 막지 못하게 하는 몫이다. 이것도 추정값이다 -
    // 게임 한 번에 1분마다 하나씩 보낼 수 있으니, 한 사람이 14일 동안 보낼 만한 양보다 넉넉히 잡았다.
    public int MaximumReportsPerSource { get; set; } = 50;
    public long MaximumStorageBytesPerSource { get; set; } = 64L * 1024 * 1024;
    public int RetentionDays { get; set; } = 14;
    public int UploadsPerMinute { get; set; } = 30;
    public int ConcurrentUploads { get; set; } = 2;
    public int UploadTimeoutSeconds { get; set; } = 60;

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(StorageDirectory) || !Path.IsPathFullyQualified(AdminTokenFile) ||
            MaximumStorageBytes <= 0 || MaximumReports <= 0 || MaximumReportsPerSource <= 0 || MaximumStorageBytesPerSource <= 0 || RetentionDays is < 1 or > 14 || UploadsPerMinute <= 0 ||
            ConcurrentUploads <= 0 || UploadTimeoutSeconds <= 0 || TrustedProxies.Length == 0 ||
            TrustedProxies.Any(value => !System.Net.IPAddress.TryParse(value, out _)))
            throw new InvalidOperationException("진단 서버의 경로 또는 운영 제한 설정이 올바르지 않습니다.");
    }
}
