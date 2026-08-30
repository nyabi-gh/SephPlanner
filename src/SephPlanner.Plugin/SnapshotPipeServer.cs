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
                        pipe.WaitForConnection();
                        _log("오버레이 연결됨");
                        Serve(pipe);
                        _log("오버레이 연결 끊김");
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
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

        public void Dispose() => _running = false;
    }
}
