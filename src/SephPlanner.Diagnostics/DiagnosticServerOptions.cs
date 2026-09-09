namespace SephPlanner.Diagnostics;

public sealed class DiagnosticServerOptions
{
    public string StorageDirectory { get; set; } = "/data/reports";
    public string AdminTokenFile { get; set; } = "/run/secrets/diagnostics_admin_token";
    public string[] TrustedProxies { get; set; } = ["10.77.0.1"];
    // 초기 운영 추정값이다. 실제 수집량과 저장 공간에 맞춰 환경 변수로 조정한다.
    public long MaximumStorageBytes { get; set; } = 512L * 1024 * 1024;
    public int MaximumReports { get; set; } = 1000;
    public int RetentionDays { get; set; } = 14;
    public int UploadsPerMinute { get; set; } = 30;
    public int ConcurrentUploads { get; set; } = 2;
    public int UploadTimeoutSeconds { get; set; } = 60;

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(StorageDirectory) || !Path.IsPathFullyQualified(AdminTokenFile) ||
            MaximumStorageBytes <= 0 || MaximumReports <= 0 || RetentionDays is < 1 or > 14 || UploadsPerMinute <= 0 ||
            ConcurrentUploads <= 0 || UploadTimeoutSeconds <= 0 || TrustedProxies.Length == 0 ||
            TrustedProxies.Any(value => !System.Net.IPAddress.TryParse(value, out _)))
            throw new InvalidOperationException("진단 서버의 경로 또는 운영 제한 설정이 올바르지 않습니다.");
    }
}
