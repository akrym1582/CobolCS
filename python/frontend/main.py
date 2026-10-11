"""CLI入出力、設定検証とfrontendの接続。stdoutはJSON専用。"""

from __future__ import annotations
import argparse
import hashlib
import json
import pathlib
import sys

# ファイル起動とpackage importの両方で同じモジュールを使う。
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))
from python.frontend.models import DEFAULT, UPSTREAM, ParseError, ParserBase
from python.frontend.source import Frontend
from python.frontend.expressions import ExpressionParser
from python.frontend.declarations import DeclarationParser
from python.frontend.statements import StatementParser
from python.frontend.procedure import ProcedureParser
from python.frontend.sql import SqlParser
from python.frontend.files import FileParser
from python.frontend.control import ControlParser


class Parser(
    ProcedureParser,
    StatementParser,
    ControlParser,
    SqlParser,
    FileParser,
    DeclarationParser,
    ExpressionParser,
    ParserBase,
):
    """共通状態を共有する責務別parserの接続。"""


def main():
    """CLI引数・方言設定を検証し、各CBLを解析してJSON IRを出力する。
    設定のhash、入力のhash、採用した上流SHAも記録し、同じ入力で同じ成果物を作れるようにする。
    未指定のホスト規則を推測して埋めず、fujitsu-reviewでは必要な規則の明示を求める。
    """
    ap = argparse.ArgumentParser()
    ap.add_argument("input")
    ap.add_argument("--copybook-path", action="append", default=[])
    ap.add_argument("--config")
    ap.add_argument("--allow-missing-copy", action="store_true")
    args = ap.parse_args()
    config = dict(DEFAULT)
    supplied = {}
    if args.config:
        supplied = json.loads(pathlib.Path(args.config).read_text(encoding="utf-8"))
        if set(supplied) - set(DEFAULT):
            raise ValueError("Unknown config keys")
        config.update(supplied)
    if config["profile"] == "fujitsu-review":
        keys = (
            "fixedColumnPolicy",
            "dataEncoding",
            "nationalEncoding",
            "collation",
            "binaryLayout",
            "signStorage",
            "arithmeticProfile",
        )
        if any(k not in supplied for k in keys):
            raise ValueError(
                "fujitsu-review requires explicit host-sensitive profile fields; no host defaults are inferred"
            )
    if config["p64"] is not None and type(config["p64"]) is not bool:
        raise ValueError("p64 must be true, false or null (unset)")
    # Do not guess host-specific layout or accept unsupported config values.
    allowed = {
        "fixedColumnPolicy": ("unicode-ascii", "unicode-columns"),
        "dataEncoding": ("ascii", "fujitsu-ebcdic-kana"),
        "collation": ("ascii", "host-byte-order"),
        "controlFlowMode": ("native", "stub-control"),
        "sqlcaInclude": ("none", "portable-minimal"),
        "redefinesSizePolicy": ("equal-fixed", "max-family"),
        "decimalPoint": ("dot", "comma"),
        "arithmeticProfile": ("exact-rational", "approximate-math"),
    }
    for key, values in allowed.items():
        if config[key] not in values:
            raise ValueError("Unsupported config " + key)
    if config["signStorage"] not in ("leading-separate", "trailing-overpunch"):
        raise ValueError("Unsupported signStorage")
    if (
        config["signStorage"] == "trailing-overpunch"
        and config["dataEncoding"] != "fujitsu-ebcdic-kana"
    ):
        raise ValueError("Overpunch requires explicit EBCDIC kana profile")
    for key in ("sourceEncoding",):
        if config[key] != DEFAULT[key]:
            raise ValueError("Unsupported config " + key)
    if config["sourceFormat"] not in ("fixed", "free") or config["profile"] not in (
        "portable-test",
        "fujitsu-review",
    ):
        raise ValueError("Invalid profile/sourceFormat")
    if config["nationalEncoding"] not in ("utf-16be", "utf-16le"):
        raise ValueError("Invalid nationalEncoding")
    if config["binaryLayout"] not in ("big-endian", "little-endian"):
        raise ValueError("Invalid binaryLayout")
    for key in ("maxSteps", "timeoutMs", "mathPrecision", "maxSortBytes", "maxRecords"):
        if type(config[key]) is not int or config[key] <= 0:
            raise ValueError("Invalid " + key)
    if (
        config["mathPrecision"] > 100
        or len(config["currencySymbol"]) != 1
        or config["currencySymbol"] in "0123456789.,+- "
    ):
        raise ValueError("Invalid mathPrecision/currencySymbol")
    path = pathlib.Path(args.input).resolve()
    base = path if path.is_dir() else path.parent
    front = Frontend(
        config,
        [pathlib.Path(p).resolve() for p in args.copybook_path],
        base,
        args.allow_missing_copy,
    )
    files = (
        sorted(p for p in path.rglob("*") if p.is_file() and p.suffix.lower() in (".cbl", ".cob", ".cobol", ".sqb"))
        if path.is_dir()
        else [path]
    )
    if not files:
        raise ValueError("No CBL inputs")
    programs = []
    for file in files:
        try:
            tokens = front.load(file)
        except UnicodeDecodeError as e:
            raw = file.read_bytes()
            prefix = raw[: e.start].decode("utf-8", errors="strict")
            name = file.relative_to(base).as_posix()
            position = dict(
                line=prefix.count("\n") + 1,
                column=len(prefix.rsplit("\n", 1)[-1]) + 1,
                byteOffset=e.start,
            )
            src = dict(
                file=name,
                start=position,
                end={**position, "byteOffset": e.end},
                copyStack=[],
                raw=raw[e.start : e.end].hex(),
            )
            front.sources[name] = dict(
                path=name,
                sha256=hashlib.sha256(raw).hexdigest(),
                encoding="utf-8",
                text="",
                bytesHex=raw.hex(),
            )
            front.diagnostic(
                "INPUT_DECODE", "Invalid UTF-8 byte sequence (raw is hex)", src
            )
            continue
        if not tokens:
            raise ValueError("Empty source " + str(file))
        parser = Parser(front, tokens)
        try:
            programs.append(parser.parse())
        except ParseError as e:
            front.diagnostic(
                "PARSE_ERROR", str(e), tokens[min(parser.i, len(tokens) - 1)].source
            )
            front.rejected.extend(
                dict(
                    text=t.text,
                    ownerKind="unknown",
                    ownerId="$parseRejected",
                    source=t.source,
                )
                for t in tokens
            )
    if config["profile"] == "fujitsu-review":
        front.diagnostic(
            "FUJITSU_UNVERIFIED",
            "Host product, options, encoding and baseline are unverified",
            next(iter(front.sources.values())) and tokens[0].source,
            "review",
        )
    config_hash = hashlib.sha256(
        json.dumps(
            config, sort_keys=True, separators=(",", ":"), ensure_ascii=False
        ).encode()
    ).hexdigest()
    result = dict(
        schemaVersion="2.2",
        frontendVersion="0.2.2",
        upstreamCommit=UPSTREAM,
        configHash=config_hash,
        config=config,
        sources=list(front.sources.values()),
        rejectedTokens=front.rejected,
        programs=programs,
        diagnostics=front.diagnostics,
        excluded=front.excluded,
    )
    print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, UnicodeError) as e:
        print(str(e), file=sys.stderr)
        sys.exit(2)
