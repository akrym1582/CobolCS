"""UTF-8、fixed/free、継続行と元位置を保持する読込み。"""

from __future__ import annotations
import pathlib
import hashlib
from .models import Token, PATTERN
from .copybooks import Copybooks


class Frontend(Copybooks):
    """ファイルの読込み・位置計算・COPY展開と、除外した領域の記録を担当する。
    記録は解析成功に限らず残し、失敗した入力が空の正常プログラムに見えないようにする。
    """

    def __init__(self, config, copies, base, allow_missing_copy=False):
        self.config, self.copies, self.base = config, copies, base
        self.allow_missing_copy = allow_missing_copy
        self.missing_copies = []
        self.sources, self.diagnostics, self.excluded = {}, [], []
        self.rejected = []
        self.copy_expansions = []

    def diagnostic(self, code, message, source, severity="error"):
        """原文位置付きの診断を蓄積する。error / warning / reviewを区別し、実行対応状況も残す。"""
        d = dict(
            code=code,
            message=message,
            source=source,
            severity=severity,
            stage="frontend",
            supported=False,
        )
        self.diagnostics.append(d)
        return d

    def load(self, path, stack=(), active=()):
        """前処理しながら、各文字が元ファイルのどこから来たかを一緒に記録する。

        stackはCOPY展開元の経路、activeは今読み込んでいるファイルの経路。
        activeへの再訪を循環COPYとして止め、同じCOPYを別の場所で使うこととは区別する。
        日本語は1文字でもUTF-8では複数byteなので、columnとbyteOffsetは別々に計算する。
        """
        path = path.resolve()
        if path in active:
            raise ValueError("COPY_CYCLE: " + str(path))
        raw = path.read_bytes()
        text = raw.decode("utf-8", errors="strict")
        name = (
            path.relative_to(self.base).as_posix()
            if path.is_relative_to(self.base)
            else path.name
        )
        if (
            name in self.sources
            and self.sources[name]["sha256"] != hashlib.sha256(raw).hexdigest()
        ):
            raise ValueError("SOURCE_PATH_COLLISION: " + name)
        self.sources[name] = dict(
            path=name,
            sha256=hashlib.sha256(raw).hexdigest(),
            encoding="utf-8",
            text=text,
            bytesHex=raw.hex(),
        )
        chars = []
        positions = []
        byte = 0
        open_quote = None
        free_continuation = False
        for line_no, line in enumerate(text.splitlines(keepends=True), 1):
            line_start = byte
            byte += len(line.encode("utf-8"))
            content = line.rstrip("\r\n")
            start = 0
            end = len(content)
            continuation = False

            # 文字位置colとUTF-8 byte位置を分ける。例えば日本は2文字だが6byte。
            def pos(col):
                return dict(
                    line=line_no,
                    column=col + 1,
                    byteOffset=line_start + len(content[:col].encode("utf-8")),
                )

            def span(a, b):
                return dict(
                    file=name,
                    start=pos(a),
                    end=pos(b),
                    copyStack=list(stack),
                    raw=content[a:b],
                )

            if self.config["sourceFormat"] == "fixed":
                if (
                    content[6:7] not in ("*", "/")
                    and self.config["fixedColumnPolicy"] == "unicode-ascii"
                    and any(ord(c) > 127 for c in content)
                ):
                    self.diagnostic(
                        "FIXED_JAPANESE_COLUMNS",
                        "Non-ASCII fixed columns require host verification",
                        span(0, len(content)),
                    )
                self.excluded.append(
                    dict(
                        reason="sequence-columns", source=span(0, min(6, len(content)))
                    )
                )
                if len(content) > 72:
                    self.excluded.append(
                        dict(
                            reason="identification-columns",
                            source=span(72, len(content)),
                        )
                    )
                indicator = content[6:7]
                if indicator in ("*", "/"):
                    self.excluded.append(
                        dict(reason="comment", source=span(0, len(content)))
                    )
                    continue
                if indicator not in ("", " ", "-"):
                    self.diagnostic(
                        "FIXED_INDICATOR",
                        "Unsupported fixed indicator",
                        span(6, min(7, len(content))),
                    )
                start = 7
                end = min(72, end)
                continuation = indicator == "-"
            if self.config["sourceFormat"] == "free":
                continuation = free_continuation
                if continuation:
                    start = len(content) - len(content.lstrip())
                    if content[start : start + 1] == "&":
                        start += 1
                trim_end = len(content.rstrip())
                free_continuation = (
                    trim_end > start and content[trim_end - 1 : trim_end] == "&"
                )
                if free_continuation:
                    end = trim_end - 1
            # Inline comments outside literals; positions are constructed now, never recovered by searching.
            segment = content[start:end]
            cut = len(segment)
            q = open_quote if continuation else None
            i = 0
            if continuation and open_quote:
                leading = len(segment) - len(segment.lstrip())
                if segment[leading : leading + 1] == open_quote:
                    start += leading + 1
                    segment = content[start:end]
            if chars and not (continuation and open_quote):
                chars.append(" ")
                positions.append((span(start, start), span(start, start)))
            q = open_quote if continuation else None
            while i < len(segment):
                c = segment[i]
                if not q and segment[i : i + 2] == "*>":
                    cut = i
                    break
                if c in ('"', "'"):
                    if q == c and segment[i + 1 : i + 2] == c:
                        i += 2
                        continue
                    q = None if q == c else (c if q is None else q)
                i += 1
            for j, c in enumerate(segment[:cut]):
                chars.append(c)
                positions.append(
                    (span(start + j, start + j), span(start + j + 1, start + j + 1))
                )
            if cut < len(segment):
                self.excluded.append(
                    dict(reason="comment", source=span(start + cut, end))
                )
            open_quote = q
        if open_quote:
            self.diagnostic(
                "UNTERMINATED_LITERAL", "Unterminated quoted literal", positions[-1][0]
            )
        # 連結後の文字にも、連結前に作った位置を対応させる。継続行やCOPY後に検索し直さない。
        joined = "".join(chars)
        tokens = []
        for m in PATTERN.finditer(joined):
            src = dict(positions[m.start()][0])
            src["end"] = positions[m.end() - 1][1]["end"]
            src["raw"] = m.group()
            tokens.append(Token(m.group(), src))
        return self.expand_copies(tokens, path, stack, active, name, raw)
