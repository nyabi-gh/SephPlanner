using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Overlay;

/// <summary>
/// 플러그인이 여는 명명 파이프에 붙어 스냅샷을 받아온다. 게임이 꺼져 있으면 조용히 재시도한다.
/// </summary>
public sealed class SnapshotClient
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public event Action<GameSnapshot>? SnapshotReceived;
    public event Action<bool>? ConnectionChanged;

    /// <summary>버전이 다른 스냅샷을 받았다. 인자는 플러그인이 보낸 버전이다.</summary>
    public event Action<int>? ProtocolMismatch;

    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".", IpcContract.PipeName, PipeDirection.In, PipeOptions.Asynchronous);

                // 게임이 아직 안 켜졌으면 타임아웃 후 다시 시도한다.
                await pipe.ConnectAsync(2000, token);
                ConnectionChanged?.Invoke(true);

                using var reader = new StreamReader(pipe);
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line is null) break; // 파이프가 닫혔다.
                    if (line.Length == 0) continue;

                    GameSnapshot? snapshot;
                    try
                    {
                        snapshot = JsonSerializer.Deserialize<GameSnapshot>(line, Options);
                    }
                    catch (JsonException)
                    {
                        continue; // 프로토콜이 어긋난 한 줄 때문에 연결을 끊지는 않는다.
                    }

                    if (snapshot is null) continue;

                    // 버전이 다른 스냅샷은 화면까지 보내지 않는다. 개명·삭제된 필드가 기본값으로
                    // 읽히면 IsMultiplayer 같은 안전 잠금이 열린 쪽으로 무너지기 때문이다.
                    if (snapshot.ProtocolVersion != IpcContract.ProtocolVersion)
                    {
                        ProtocolMismatch?.Invoke(snapshot.ProtocolVersion);
                        continue;
                    }

                    SnapshotReceived?.Invoke(snapshot);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            ConnectionChanged?.Invoke(false);
            try { await Task.Delay(1000, token); } catch (OperationCanceledException) { return; }
        }
    }
}
