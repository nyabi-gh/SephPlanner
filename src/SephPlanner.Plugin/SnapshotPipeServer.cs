using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 최신 스냅샷을 명명 파이프로 흘려보낸다. 오버레이가 붙어 있지 않아도 게임은 계속 돌아야 하므로
    /// 전송은 전부 백그라운드 스레드에서 하고 예외를 게임 루프로 넘기지 않는다.
    /// </summary>
    internal sealed class SnapshotPipeServer : IDisposable
    {
        private readonly Action<string> _log;
        private volatile bool _running = true;
        private volatile string _latest;
        private volatile NamedPipeServerStream _pipe;

        public SnapshotPipeServer(Action<string> log)
        {
            _log = log;
            new Thread(Loop) { IsBackground = true, Name = "SephPlanner.Pipe" }.Start();
        }

        public void Publish(string json) => _latest = json;

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(
                        IpcContract.PipeName, PipeDirection.Out, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        // Dispose 가 이 참조를 닫아 WaitForConnection 블로킹을 깨운다.
                        _pipe = pipe;
                        pipe.WaitForConnection();
                        _log("오버레이 연결됨");
                        Serve(pipe);
                        _log("오버레이 연결 끊김");
                    }
                }
                catch (ThreadAbortException)
                {
                    // 게임이 닫히면서 스레드가 정리되는 정상 경로다.
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
                    _log("파이프 오류: " + ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        private void Serve(NamedPipeServerStream pipe)
        {
            string lastSent = null;
            while (_running && pipe.IsConnected)
            {
                var current = _latest;
                if (current != null && current != lastSent)
                {
                    var bytes = Encoding.UTF8.GetBytes(current + "\n");
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();
                    lastSent = current;
                }
                Thread.Sleep(100);
            }
        }

        public void Dispose()
        {
            // 플래그만 세우면 WaitForConnection 에 블로킹된 스레드가 유일한 파이프 인스턴스를
            // 쥔 채 살아남아, 다음 서버가 뜰 때 생성 실패 스핀으로 이어진다. 닫아서 깨운다.
            _running = false;
            try { _pipe?.Dispose(); } catch (Exception) { }
        }
    }
}
