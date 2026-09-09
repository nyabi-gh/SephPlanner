using SephPlanner.Diagnostics;

if (args is ["--healthcheck"])
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        using var response = await http.GetAsync("http://127.0.0.1:8080/health");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
    {
        Console.Error.WriteLine("진단 서버 상태 확인 실패: " + ex.Message);
        return 1;
    }
}
await DiagnosticServer.Build(args).RunAsync();
return 0;
