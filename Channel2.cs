namespace MyApp.Models
{
    public class MessageItem
    {
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public string Content { get; set; } = string.Empty;
    }
}

=======================================================================================

using System.Threading.Channels;
using MyApp.Models;

namespace MyApp.Services
{
    public class MessageChannelService
    {
        private readonly Channel<MessageItem> _channel;

        public MessageChannelService()
        {
            _channel = Channel.CreateUnbounded<MessageItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,   // 로그 워커 1개만 읽음
                    SingleWriter = false   // 여러 요청이 동시에 Write
                });
        }

        public ChannelWriter<MessageItem> Writer => _channel.Writer;
        public ChannelReader<MessageItem> Reader => _channel.Reader;

        public ValueTask WriteAsync(MessageItem item, CancellationToken token = default)
            => _channel.Writer.WriteAsync(item, token);

        public void Complete() => _channel.Writer.Complete();
    }
}

=======================================================================================

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyApp.Models;
using System.Threading.Channels;

namespace MyApp.Services
{
    public class MessageLogWorker : BackgroundService
    {
        private readonly ChannelReader<MessageItem> _reader;
        private readonly ILogger<MessageLogWorker> _logger;
        private readonly string _logFilePath;

        public MessageLogWorker(MessageChannelService service, ILogger<MessageLogWorker> logger)
        {
            _reader = service.Reader;
            _logger = logger;
            _logFilePath = Path.Combine(AppContext.BaseDirectory, "messages.log");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MessageLogWorker started.");

            var buffer = new List<MessageItem>();
            var lastFlush = DateTime.UtcNow;

            try
            {
                await foreach (var msg in _reader.ReadAllAsync(stoppingToken))
                {
                    buffer.Add(msg);

                    // 주기적 flush 조건
                    if ((DateTime.UtcNow - lastFlush).TotalSeconds >= 5 || buffer.Count >= 500)
                    {
                        await FlushAsync(buffer);
                        lastFlush = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker cancellation requested.");
            }
            finally
            {
                // 종료 시 남은 메시지 flush
                if (buffer.Count > 0)
                {
                    await FlushAsync(buffer);
                    _logger.LogInformation("Final flush: {Count} messages", buffer.Count);
                }
                _logger.LogInformation("MessageLogWorker stopped.");
            }
        }

        private async Task FlushAsync(List<MessageItem> buffer)
        {
            try
            {
                await WriteToFileAsync(buffer);
                _logger.LogInformation("Flushed {Count} messages.", buffer.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing messages to log file.");
            }
            finally
            {
                buffer.Clear();
            }
        }

        private async Task WriteToFileAsync(IEnumerable<MessageItem> messages)
        {
            using var writer = new StreamWriter(_logFilePath, append: true);
            foreach (var msg in messages)
            {
                await writer.WriteLineAsync($"[{msg.ReceivedAt:O}] {msg.Content}");
            }
        }
    }
}

===================================================================================================

using Microsoft.AspNetCore.Mvc;
using MyApp.Models;
using MyApp.Services;

namespace MyApp.Controllers
{
    [ApiController]
    [Route("api/messages")]
    public class MessageController : ControllerBase
    {
        private readonly MessageChannelService _channelService;
        private readonly ILogger<MessageController> _logger;

        public MessageController(MessageChannelService channelService, ILogger<MessageController> logger)
        {
            _channelService = channelService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> PostAsync([FromBody] string message, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(message))
                return BadRequest("Message cannot be empty.");

            var item = new MessageItem { Content = message };
            await _channelService.WriteAsync(item, token);

            _logger.LogDebug("Queued message: {Message}", message);
            return Ok(new { status = "queued" });
        }
    }
}
==================================================================================================================

using MyApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<MessageChannelService>();
builder.Services.AddHostedService<MessageLogWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();

========================================================================


✅ 동작 방식 요약
단계	동작
1. REST 요청 수신	/api/messages POST 요청 → Channel.Writer.WriteAsync
2. Channel에 버퍼링	Writer가 비동기로 메시지 저장
3. 백그라운드 워커 실행	Reader가 지속적으로 ReadAllAsync로 읽음
4. 주기적 flush (5초 or 500개)	로그 파일에 배치로 저장
5. 앱 종료 시	남은 메시지를 모두 flush 후 종료

🧠 특징 요약
항목	설명
손실 방지	ReadAllAsync로 지속 소비 + 종료 시 최종 flush
높은 성능	Channel은 락이 거의 없음
안정성	try/catch 로 IO 예외 안전하게 처리
확장성	로그 주기 / batch 크기 조정 가능

📘 커스터마이즈 포인트
항목	기본값	변경 예시
Flush 주기	5초	if ((DateTime.UtcNow - lastFlush).TotalSeconds >= 3)
Flush 개수	500	if (buffer.Count >= 1000)
로그 파일 경로	messages.log	Path.Combine("C:\\logs", "messages.log")
