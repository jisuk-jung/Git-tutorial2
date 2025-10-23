/*요청을 큐에 담고 순차적으로 로그 파일에 기록하기*/
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

class RequestLogger
{
    private readonly ConcurrentQueue<string> _requestQueue = new ConcurrentQueue<string>();
    private readonly AutoResetEvent _queueEvent = new AutoResetEvent(false);
    private readonly string _logFilePath;
    private bool _isRunning = true;

    public RequestLogger(string logFilePath)
    {
        _logFilePath = logFilePath;

        // 백그라운드에서 큐 처리 시작
        Task.Run(ProcessQueueAsync);
    }

    public void EnqueueRequest(string request)
    {
        _requestQueue.Enqueue(request);
        _queueEvent.Set(); // 새 요청이 들어왔음을 알림
    }

    private async Task ProcessQueueAsync()
    {
        while (_isRunning)
        {
            if (_requestQueue.TryDequeue(out var request))
            {
                try
                {
                    await File.AppendAllTextAsync(_logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {request}\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 로그 파일 쓰기 실패: {ex.Message}");
                }
            }
            else
            {
                // 큐가 비었으면 신호 기다림
                _queueEvent.WaitOne();
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _queueEvent.Set(); // 종료 신호
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        var logger = new RequestLogger("requests.log");

        // 예시: 대량 요청을 큐에 넣기
        for (int i = 0; i < 1000; i++)
        {
            logger.EnqueueRequest($"Request #{i}");
        }

        Console.WriteLine("모든 요청을 큐에 추가했습니다. 로그 파일에 순차적으로 기록 중입니다...");

        // 잠시 대기 후 종료 (테스트용)
        await Task.Delay(5000);
        logger.Stop();

        Console.WriteLine("로깅 종료.");
    }
}
