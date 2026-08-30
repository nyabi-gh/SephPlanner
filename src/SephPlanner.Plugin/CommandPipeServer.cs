using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Newtonsoft.Json;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 오버레이가 보내는 명령을 받는 역방향 파이프. 게임 API는 메인 스레드에서만 안전하므로
    /// 여기서는 큐에 쌓기만 하고, 실행은 <c>Update</c>가 꺼내서 한다.
    /// </summary>
    internal sealed class CommandPipeServer : IDisposable
    {
        private readonly Action<string> _log;
        private readonly ConcurrentQueue<ApplyPlanCommand> _pending = new ConcurrentQueue<ApplyPlanCommand>();
        private volatile bool _running = true;

        public CommandPipeServer(Action<string> log)
        {
            _log = log;
            new Thread(Loop) { IsBackground = true, Name = "SephPlanner.CommandPipe" }.Start();
        }

        public bool TryDequeue(out ApplyPlanCommand command) => _pending.TryDequeue(out command);

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(
                        IpcContract.CommandPipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        pipe.WaitForConnection();
                        using (var reader = new StreamReader(pipe))
                        {
                            string line;
                            while (_running && (line = reader.ReadLine()) != null)
                            {
                                if (line.Length == 0) continue;
                                Enqueue(line);
                            }
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    _running = false;
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    _log("명령 파이프 오류: " + ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        private void Enqueue(string line)
        {
            try
            {
                var command = JsonConvert.DeserializeObject<ApplyPlanCommand>(line);
                if (command == null) return;
                if (command.ProtocolVersion != IpcContract.ProtocolVersion)
                {
                    _log($"명령 프로토콜 불일치: {command.ProtocolVersion} (기대 {IpcContract.ProtocolVersion})");
                    return;
                }
                _pending.Enqueue(command);
            }
            catch (JsonException ex)
            {
                _log("명령 해석 실패: " + ex.Message);
            }
        }

        public void Dispose() => _running = false;
    }
}
