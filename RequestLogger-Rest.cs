/*
⚙️ 특징

✅ REST API로 대량 요청 처리 가능
✅ 큐에 적재 후 비동기로 순차 처리 (서버 부하 감소)
✅ Thread-safe 구조
✅ 로그 파일 자동 append
*/

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace YourProject.Services
{
    public class RequestLogger : IDisposable
    {
        private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly string _logFilePath;
        private bool _isRunning = true;
        private readonly Task _worker;

        public RequestLogger(string logFilePath)
        {
            _logFilePath = logFilePath;
            _worker = Task.Run(ProcessQueueAsync);
        }

        public void Enqueue(string message)
        {
            _queue.Enqueue(message);
            _signal.Set();
        }

        private async Task ProcessQueueAsync()
        {
            while (_isRunning)
            {
                if (_queue.TryDequeue(out var msg))
                {
                    try
                    {
                        await File.AppendAllTextAsync(_logFilePath,
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {msg}\n");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] 로그 쓰기 실패: {ex.Message}");
                    }
                }
                else
                {
                    _signal.WaitOne(); // 큐 비면 대기
                }
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            _signal.Set();
        }
    }
}

===============================================================================================================

using Microsoft.AspNetCore.Mvc;
using YourProject.Services;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogController : ControllerBase
    {
        private readonly RequestLogger _logger;

        public LogController(RequestLogger logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public IActionResult Post([FromBody] LogRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest("Message cannot be empty.");

            _logger.Enqueue(request.Message);
            return Ok(new { status = "queued", message = request.Message });
        }
    }

    public class LogRequest
    {
        public string Message { get; set; }
    }
}
=============================================================================================
  using YourProject.Services;

var builder = WebApplication.CreateBuilder(args);

// RequestLogger를 싱글톤으로 등록
builder.Services.AddSingleton(new RequestLogger("requests.log"));
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
