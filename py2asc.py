# app/async_rotating_logger.py
import aiofiles
import asyncio
import os
import json
from datetime import datetime
from app.config import settings

# 비동기 락 (asyncio 환경에서 thread-safe 역할)
lock = asyncio.Lock()

async def get_log_path() -> str:
    """오늘 날짜 기준의 로그 파일 경로를 반환"""
    today = datetime.now().strftime("%Y-%m-%d")
    os.makedirs(settings.log_dir, exist_ok=True)
    return os.path.join(settings.log_dir, f"{today}.log")

async def rotate_log_file_if_needed(log_path: str):
    """파일 크기가 설정된 한도를 넘으면 회전"""
    max_size = settings.log_file_size_mb * 1024 * 1024  # MB → bytes
    if os.path.exists(log_path) and os.path.getsize(log_path) >= max_size:
        for i in range(settings.log_backup_count, 0, -1):
            old_file = f"{log_path}.{i}"
            next_file = f"{log_path}.{i + 1}"
            if os.path.exists(old_file):
                if i == settings.log_backup_count:
                    os.remove(old_file)
                else:
                    os.rename(old_file, next_file)
        os.rename(log_path, f"{log_path}.1")

async def write_log_async(data: dict):
    """비동기로 thread-safe하게 로그 작성"""
    async with lock:
        log_path = await get_log_path()
        await rotate_log_file_if_needed(log_path)

        log_line = f"{datetime.now().isoformat()} - {json.dumps(data, ensure_ascii=False)}\n"

        async with aiofiles.open(log_path, "a", encoding="utf-8") as f:
            await f.write(log_line)
