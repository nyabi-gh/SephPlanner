using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 실행 결과를 기다리는 명령 하나. 파이프 스레드가 만들고 메인 스레드가 완료한다.
    /// </summary>
    internal sealed class PendingCommand
    {
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);
        private volatile string _result;

        public PendingCommand(ApplyPlanCommand command) => Command = command;

        public ApplyPlanCommand Command { get; }

        public void Complete(string result)
        {
            _result = result;
            _done.Set();
        }

        public string Await(int timeoutMs) => _done.Wait(timeoutMs) ? _result : null;
    }

    /// <summary>
    /// 오버레이가 보내는 명령을 받아 결과를 돌려주는 양방향 파이프. 게임 API는 메인 스레드에서만
    /// 안전하므로 여기서는 큐에 쌓기만 하고, 실행은 <c>Update</c>가 꺼내서 한 뒤 결과를 알려 준다.
    /// 파이프 스레드는 그 결과를 기다렸다가 오버레이에 써 준다. 응답이 없으면 사용자에게는
    /// "버튼을 눌렀는데 아무 일도 없는" 무음 실패가 된다.
    /// </summary>
    internal sealed class CommandPipeServer : IDisposable
    {
        /// <summary>게임이 백그라운드에서 멈춰 있으면 Update 가 돌지 않아 결과가 안 나올 수 있다.</summary>
        private const int ResponseTimeoutMs = 5000;

        private readonly Action<string> _log;
        private readonly ConcurrentQueue<PendingCommand> _pending = new ConcurrentQueue<PendingCommand>();
        private volatile bool _running = true;
        private volatile NamedPipeServerStream _pipe;

        public CommandPipeServer(Action<string> log)
        {
            _log = log;
            new Thread(Loop) { IsBackground = true, Name = "SephPlanner.CommandPipe" }.Start();
        }

        public bool TryDequeue(out PendingCommand command) => _pending.TryDequeue(out command);

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(
                        IpcContract.CommandPipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        // Dispose 가 이 참조를 닫아 WaitForConnection 블로킹을 깨운다.
                        _pipe = pipe;
                        pipe.WaitForConnection();
                        Serve(pipe);
                    }
                }
                catch (ThreadAbortException)
                {
                    _running = false;
                }
                catch (IOException)
                {
                    // 잠들지 않으면 파이프 이름이 점유된 상태에서 로그도 없이 코어를 태운다.
                    if (_running) Thread.Sleep(500);
                }
                catch (ObjectDisposedException)
                {
                    if (_running) Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    _log("명령 파이프 오류: " + ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        private void Serve(NamedPipeServerStream pipe)
        {
            var encoding = new UTF8Encoding(false);
            using (var reader = new StreamReader(pipe, encoding, false, 1024, leaveOpen: true))
            using (var writer = new StreamWriter(pipe, encoding, 1024, leaveOpen: true) { AutoFlush = true })
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    writer.WriteLine(Handle(line));
                }
            }
        }

        private string Handle(string line)
        {
            ApplyPlanCommand command;
            try
            {
                command = JsonConvert.DeserializeObject<ApplyPlanCommand>(line);
            }
            catch (JsonException ex)
            {
                _log("명령 해석 실패: " + ex.Message);
                return "명령을 해석하지 못했습니다. 플러그인과 오버레이 버전이 다를 수 있습니다.";
            }
            if (command == null) return "빈 명령입니다.";
            if (command.ProtocolVersion != IpcContract.ProtocolVersion)
            {
                _log($"명령 프로토콜 불일치: {command.ProtocolVersion} (기대 {IpcContract.ProtocolVersion})");
                return "플러그인과 오버레이 버전이 달라 명령을 거부했습니다. 둘을 함께 업데이트해 주세요.";
            }

            var pending = new PendingCommand(command);
            _pending.Enqueue(pending);
            return pending.Await(ResponseTimeoutMs)
                   ?? "게임이 응답하지 않아 결과를 확인하지 못했습니다. BepInEx 로그를 확인하세요.";
        }

        public void Dispose()
        {
            _running = false;
            try { _pipe?.Dispose(); } catch (Exception) { }
        }
    }
}
