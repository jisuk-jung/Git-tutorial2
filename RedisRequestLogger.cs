/*
🧠 이 방식의 장점

✅ 여러 파드가 같은 큐(Redis List)에 메시지를 푸시 가능
✅ 백그라운드 워커가 중앙 큐에서 하나씩 꺼내서 처리
✅ 장애 복구, 스케일 아웃 용이
✅ Redis Pub/Sub 또는 Stream으로 확장 가능
*/

using StackExchange.Redis;
using System;
using System.IO;
using System.Threading.Tasks;

namespace YourProject.Services
{
    public class RedisRequestLogger
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly string _queueKey = "logQueue";
        private readonly string _logFilePath;
        private bool _isRunning = true;

        public RedisRequestLogger(IConnectionMultiplexer redis, string logFilePath)
        {
            _redis = redis;
            _logFilePath = logFilePath;
            Task.Run(ProcessQueueAsync);
        }

        public async Task EnqueueAsync(string message)
        {
            var db = _redis.GetDatabase();
            await db.ListRightPushAsync(_queueKey, message);
        }

        private async Task ProcessQueueAsync()
        {
            var db = _redis.GetDatabase();

            while (_isRunning)
            {
                var msg = await db.ListLeftPopAsync(_queueKey);
                if (msg.HasValue)
                {
                    await File.AppendAllTextAsync(_logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {msg}\n");
                }
                else
                {
                    await Task.Delay(100); // 큐가 비었으면 잠시 대기
                }
            }
        }

        public void Stop() => _isRunning = false;
    }
}

====================================================================================================================

using StackExchange.Redis;
using YourProject.Services;

var builder = WebApplication.CreateBuilder(args);

var redis = ConnectionMultiplexer.Connect("redis-service:6379"); // Kubernetes에서 서비스명으로 접근
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddSingleton(new RedisRequestLogger(redis, "requests.log"));

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();


===================================================================================================================

using Microsoft.AspNetCore.Mvc;
using YourProject.Services;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogController : ControllerBase
    {
        private readonly RedisRequestLogger _logger;

        public LogController(RedisRequestLogger logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] LogRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest("Message cannot be empty.");

            await _logger.EnqueueAsync(request.Message);
            return Ok(new { status = "queued", message = request.Message });
        }
    }

    public class LogRequest
    {
        public string Message { get; set; }
    }
}

===========================================================================================================

apiVersion: apps/v1
kind: Deployment
metadata:
  name: redis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: redis
  template:
    metadata:
      labels:
        app: redis
    spec:
      containers:
      - name: redis
        image: redis:7
        ports:
        - containerPort: 6379
---
apiVersion: v1
kind: Service
metadata:
  name: redis-service
spec:
  selector:
    app: redis
  ports:
  - port: 6379
    targetPort: 6379

