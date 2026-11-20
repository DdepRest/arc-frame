"""Чтение контейнера .ZKZ (CCalc 9.x).

Формат разобран по 522 файлам из каталога CCalc и реестра заказов:

    смещение  размер  поле
    0         12      магия b"ADGRASKONZKZ"
    12        2       тип записи (uint16 LE): 0x64 = заказ (.ZKZ),
                      0xC4 = заказ в ccalc.mdb, 0x2C = вторая часть заказа,
                      0x69 = заказчик
    14        1       флаги (в наблюдаемых файлах всегда 0x01)
    15        4       длина тела (uint32 LE), равна размеру файла минус 19
    19        N       тело

Отдельного байта версии нет: после магии идут ровно 7 байт — тип (2),
флаг (1) и длина (4).

Тело обфусцировано самой программой: высокая энтропия (~7.9 бит/байт),
данные не читаются ни одним стандартным архиватором, при этом одинаковые
заказы дают побайтно одинаковые тела, а первые ~28 байт совпадают у файлов
разных лет. Значит преобразование позиционное и не содержит соли на файл;
в exe статической таблицы гаммы нет, поэтому для полного разбора нужен
рантайм-дамп (тело в памяти CCalc уже расшифровано).

Модуль даёт разбор заголовка, проверку целостности и извлечение тела.
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterator

MAGIC = b"ADGRASKONZKZ"
HEADER_SIZE = len(MAGIC) + 7

RECORD_TYPES = {
    0x64: "order-zkz",
    0xC4: "order-db",
    0x2C: "order-db-part2",
    0x69: "customer",
}


class ZkzError(Exception):
    """Файл не является контейнером .ZKZ или повреждён."""


@dataclass
class ZkzFile:
    path: Path
    record_type: int
    flags: int
    declared_size: int
    body: bytes = field(repr=False)

    @property
    def type_name(self) -> str:
        return RECORD_TYPES.get(self.record_type, "unknown")

    @property
    def file_size(self) -> int:
        return HEADER_SIZE + len(self.body)

    def as_dict(self) -> dict:
        return {
            "path": str(self.path),
            "type": self.record_type,
            "type_name": self.type_name,
            "flags": self.flags,
            "body_size": len(self.body),
            "file_size": self.file_size,
        }


def parse_bytes(data: bytes, path: Path | str = "<memory>") -> ZkzFile:
    """Разбирает содержимое файла .ZKZ."""
    path = Path(path)
    if len(data) < HEADER_SIZE:
        raise ZkzError(f"{path}: файл короче заголовка ({len(data)} байт)")
    if data[:len(MAGIC)] != MAGIC:
        raise ZkzError(f"{path}: нет магии {MAGIC!r}")

    record_type, flags, size = struct.unpack_from("<HBI", data, len(MAGIC))
    body = data[HEADER_SIZE:]
    if size != len(body):
        raise ZkzError(
            f"{path}: в заголовке длина {size}, фактически {len(body)} байт"
        )
    return ZkzFile(path, record_type, flags, size, body)


def parse_zkz(path: Path | str) -> ZkzFile:
    """Читает и разбирает файл .ZKZ."""
    path = Path(path)
    return parse_bytes(path.read_bytes(), path)


def iter_zkz(directory: Path | str) -> Iterator[ZkzFile]:
    """Разбирает все .ZKZ в каталоге (без рекурсии); битые файлы пропускает."""
    for path in sorted(Path(directory).iterdir()):
        if not path.is_file() or path.suffix.lower() != ".zkz":
            continue
        try:
            yield parse_zkz(path)
        except (ZkzError, OSError):
            continue


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Разбор контейнера .ZKZ (CCalc)")
    parser.add_argument("path", help="файл .ZKZ или каталог с ними")
    parser.add_argument("--json", action="store_true", help="вывод в JSON")
    parser.add_argument(
        "--dump-body",
        metavar="FILE",
        help="записать тело первого файла в FILE (сырые байты)",
    )
    args = parser.parse_args(argv)

    target = Path(args.path)
    if not target.exists():
        print(f"нет такого пути: {target}", file=sys.stderr)
        return 2

    files = [parse_zkz(target)] if target.is_file() else list(iter_zkz(target))
    if not files:
        print("файлы .ZKZ не найдены", file=sys.stderr)
        return 1

    if args.json:
        print(json.dumps([f.as_dict() for f in files], ensure_ascii=False, indent=2))
    else:
        print("%-8s %-15s %-6s %9s" % ("тип", "вид", "флаги", "тело"))
        for f in files:
            print(
                "%-8s %-15s %-6d %9d"
                % (hex(f.record_type), f.type_name, f.flags, len(f.body))
            )
        print(f"всего файлов: {len(files)}")

    if args.dump_body:
        Path(args.dump_body).write_bytes(files[0].body)
        print(f"тело записано в {args.dump_body}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    raise SystemExit(main())
