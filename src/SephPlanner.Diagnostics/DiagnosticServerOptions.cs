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
    // 빈도는 보낸 곳마다, 동시 처리는 곳마다와 서버 전체로 센다. 게임은 한 번 켤 때 1분에 하나만 보내므로 곳마다의 빈도는
    // 공유기 뒤 몇 사람을 감안한 값이다. 전체 동시 처리는 업로드 하나가 받는 동안 메모리에 압축 8MB 와 풀린 16MB 까지
    // 쥐는 것을 보고 정했다. 모두 추정값이다.
    public int UploadsPerMinute { get; set; } = 10;
    public int ConcurrentUploads { get; set; } = 2;
    public int TotalConcurrentUploads { get; set; } = 8;
    public int UploadTimeoutSeconds { get; set; } = 60;
    // 이보다 느리면 유예 뒤에 끊는다. 추정값이다 - 초당 4KB 로는 1MB 를 보내는 데 4분이 걸려, 그런 회선은 이미
    // 업로드 시간 제한에 걸린다. 유예는 Kestrel 의 1초 점검 주기보다 길어야 한다.
    public int MinimumUploadBytesPerSecond { get; set; } = 4096;
    public int MinimumUploadRateGraceSeconds { get; set; } = 5;

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(StorageDirectory) || !Path.IsPathFullyQualified(AdminTokenFile) ||
            MaximumStorageBytes <= 0 || MaximumReports <= 0 || MaximumReportsPerSource <= 0 || MaximumStorageBytesPerSource <= 0 ||
            RetentionDays is < 1 or > 14 || UploadsPerMinute <= 0 || ConcurrentUploads <= 0 || TotalConcurrentUploads <= 0 ||
            UploadTimeoutSeconds <= 0 || MinimumUploadBytesPerSecond <= 0 || MinimumUploadRateGraceSeconds < 2 ||
            TrustedProxies.Length == 0 ||
            TrustedProxies.Any(value => !System.Net.IPAddress.TryParse(value, out _)))
            throw new InvalidOperationException("진단 서버의 경로 또는 운영 제한 설정이 올바르지 않습니다.");
    }
}
