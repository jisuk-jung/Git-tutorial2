# app/logger.py
import logging
import queue
import threading
from logging.handlers import QueueHandler, QueueListener
from app.config import settings

# 로그 큐 생성
log_queue = queue.Queue()

# 실제 파일 핸들러
file_handler = logging.FileHandler(settings.log_file_path, encoding='utf-8')
formatter = logging.Formatter('%(asctime)s [%(levelname)s] %(message)s')
file_handler.setFormatter(formatter)

# QueueListener: 별도 스레드에서 안전하게 파일로 기록
listener = QueueListener(log_queue, file_handler)
listener.start()

# QueueHandler: 여러 스레드에서 동시에 안전하게 로그 enqueue
queue_handler = QueueHandler(log_queue)

logger = logging.getLogger("app_logger")
logger.setLevel(settings.log_level)
logger.addHandler(queue_handler)

# FastAPI 애플리케이션 종료 시 리스너 정리 함수
def stop_listener():
    listener.stop()


