"""位置付きtoken、設定、共通IR構築とparserの状態。"""

import re
import sys
import pathlib
import json
from dataclasses import dataclass

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "vendor/pythonbol"))
from cobol_safe_translator.pic_parser import parse_pic

UPSTREAM = json.loads((ROOT / "vendor/pythonbol/UPSTREAM.json").read_text())["commit"]
# portable-testは合成試験の規則。富士通の文字コード・符号・格納方式の既定値とはみなさない。
DEFAULT = dict(
    profile="portable-test",
    externalAdapter="none",
    sourceFormat="free",
    sourceEncoding="utf-8",
    fixedColumnPolicy="unicode-ascii",
    dataEncoding="ascii",
    nationalEncoding="utf-16be",
    collation="ascii",
    binaryLayout="big-endian",
    signStorage="leading-separate",
    arithmeticProfile="exact-rational",
    controlFlowMode="native",
    sqlcaInclude="none",
    whenCompiled=None,
    redefinesSizePolicy="equal-fixed",
    decimalPoint="dot",
    currencySymbol="$",
    mathPrecision=34,
    maxSortBytes=16777216,
    maxRecords=100000,
    maxSteps=1000000,
    timeoutMs=10000,
)
# 命令開始語とブロック終端語は別に持つ。未知構文から復旧する時も、次の既知命令を消さない。
VERBS = set(
    "SORT MERGE RELEASE RETURN REWRITE START DELETE CANCEL SEARCH NEXT INITIALIZE MOVE DISPLAY ADD SUBTRACT MULTIPLY DIVIDE COMPUTE IF EVALUATE PERFORM GO GOBACK STOP EXIT CONTINUE CALL EXEC OPEN READ WRITE CLOSE SET STRING UNSTRING INSPECT".split()
)
ENDS = set(
    "ELSE END-IF END-EVALUATE END-PERFORM END-ADD END-SUBTRACT END-MULTIPLY END-DIVIDE END-COMPUTE END-READ WHEN END-CALL ON NOT AT END-EXEC END-STRING END-UNSTRING END-SEARCH END-WRITE END-REWRITE END-DELETE END-START END-RETURN INVALID".split()
)
# 引用符内のピリオドは文字列の一部としてまとめる。引用符の外にあるピリオドだけを終端にできる。
PATTERN = re.compile(
    r""""(?:""|[^"\n])*"|'(?:''|[^'\n])*'|\d+(?:\.\d+)?|(?:[^\W\d]|_)[\w\-]*|==|\*\*|<>|<=|>=|[.,()+*/=<>:\-]|\S""",
    re.UNICODE,
)


@dataclass
class Token:
    """原文と位置を持つ1トークン。例えばMOVE、項目名、数値、引用符付き文字列がそれぞれ1件になる。
    元のtextは保存し、比較が必要な時だけupperで大文字化する。日本語名やリテラルを書き換えない。
    """

    text: str
    source: dict

    @property
    def upper(self):
        return self.text.upper()


def lit(v, typ="numeric"):
    """数値・文字・真偽値のliteralノードを作る。数値はJSON境界で精度を保つため十進文字列にする。"""
    return dict(kind="literal", value=v, valueType=typ)


def binary(op, a, b):
    """左右の式と演算子を持つノードを作る。文字列の計算コードにせず、後工程で型検査できる形にする。"""
    return dict(kind="binary", op=op, left=a, right=b)


class ParseError(Exception):
    pass


class ParserBase:
    def __init__(self, front, tokens):
        self.f, self.ts, self.i = front, tokens, 0
        self.symbols = []
        self.paragraphs = []
        self.dependencies = []
        self.ds = []
        self.sid = 0
        self.owners = {}
        self.procedure_start = 0

    def peek(self, n=0):
        """次のトークンを消費せず大文字で見る。n=1ならさらに1つ先を見る。末尾では空文字を返す。"""
        return self.ts[self.i + n].upper if self.i + n < len(self.ts) else ""

    def take(self):
        """トークンを1つ取り出して読み取り位置を進める。入力不足を空文字などの正常値へ置換しない。"""
        if self.i >= len(self.ts):
            raise ParseError("Unexpected end of source")
        t = self.ts[self.i]
        self.i += 1
        return t

    def accept(self, s):
        """期待語と一致した場合だけ1つ進める。THENなど、省略可能な語の処理で使う。"""
        if self.peek() == s:
            self.i += 1
            return True
        return False

    def require(self, s):
        """必須語を検査して進める。不一致は解析エラーにし、acceptのように読み飛ばさない。"""
        if not self.accept(s):
            raise ParseError("Expected " + s + ", got " + self.peek())

    def source(self, a, b=None):
        """消費した範囲の元位置をまとめる。endは範囲の直後を指す。
        同じ原文を検索して位置を復元せず、トークンの位置と保存済み原文byteから取り出す。
        """
        b = self.i if b is None else b
        first = self.ts[a].source
        last = self.ts[max(a, b - 1)].source
        src = dict(first)
        spans = []
        for token in self.ts[a:b]:
            part = token.source
            if spans and spans[-1]["file"] == part["file"] and spans[-1]["copyStack"] == part["copyStack"] and spans[-1]["end"]["byteOffset"] <= part["start"]["byteOffset"]:
                spans[-1]["end"] = part["end"]
            else:
                spans.append({key: part[key] for key in ("file", "start", "end", "copyStack")})
        src["spans"] = spans
        if first["file"] == last["file"] and first["copyStack"] == last["copyStack"]:
            src["end"] = last["end"]
            data = self.f.sources[first["file"]]["text"].encode("utf-8")
            src["raw"] = data[
                first["start"]["byteOffset"] : last["end"]["byteOffset"]
            ].decode("utf-8")
        else:
            src["raw"] = " ".join(t.text for t in self.ts[a:b])
        return src

    def diag(self, code, message, src, severity="error"):
        d = self.f.diagnostic(code, message, src, severity)
        self.ds.append(d)

    def resolve(self, name, qualifiers=()):
        """名前とOF/IN修飾から1つの宣言を選び、安定したsymbol IDへ解決する。
        例えば金額 OF Aと金額 OF Bを区別する。修飾なしで候補が複数あれば曖昧として拒否する。
        欠落COPYを許容したscaffoldだけは、未確定項目をstubとして保持し、型や値を推測しない。
        """
        candidates = [
            s
            for s in self.symbols
            if s["name"].upper() == name.upper() and s["name"].upper() != "FILLER"
        ]

        def qualified(s):
            names = []
            p = s["parentId"]
            while p:
                parent = next(x for x in self.symbols if x["id"] == p)
                names.append(parent["name"].upper())
                p = parent["parentId"]
            index = 0
            for q in qualifiers:
                try:
                    index = names.index(q.upper(), index) + 1
                except ValueError:
                    return False
            return True

        candidates = [s for s in candidates if qualified(s)]
        if not candidates and name.upper() in ("RETURN-CODE", "SORT-RETURN"):
            sid = f"f{len(self.symbols)+1:06d}"
            item = dict(
                id=sid,
                name=name.upper(),
                qualifiedName=name.upper(),
                stub=False,
                scope="WORKING-STORAGE",
                level=77,
                pic=dict(
                    raw="S9(9)", category="numeric", digits=9, scale=0, signed=True
                ),
                usage="COMP-5",
                children=[],
                parentId=None,
                occurs=1,
                occursClause=None,
                redefines=None,
                initial=lit("0"),
                conditions=[],
                source=self.ts[max(0, self.i - 1)].source,
            )
            self.symbols.append(item)
            return item
        if not candidates and self.f.allow_missing_copy and self.f.missing_copies:
            qualified = name + "".join(" OF " + q for q in qualifiers)
            old = next(
                (
                    s
                    for s in self.symbols
                    if s["stub"] and s["qualifiedName"].upper() == qualified.upper()
                ),
                None,
            )
            if old:
                return old
            sid = f"f{len(self.symbols)+1:06d}"
            src = self.ts[max(0, self.i - 1)].source
            s = dict(
                id=sid,
                name=name,
                qualifiedName=qualified,
                stub=True,
                scope="WORKING-STORAGE",
                level=77,
                pic=None,
                usage="DISPLAY",
                children=[],
                parentId=None,
                occurs=1,
                occursClause=None,
                redefines=None,
                initial=None,
                conditions=[],
                source=src,
            )
            self.symbols.append(s)
            self.diag(
                "MISSING_COPY_SYMBOL",
                qualified
                + " requires an explicit fixture PIC/layout; no type inferred",
                src,
            )
            # Stub references are review items; valid layout is supplied only at execution.
            self.ds[-1]["severity"] = "review"
            return s
        if len(candidates) != 1:
            raise ParseError("Unresolved or ambiguous reference: " + name)
        return candidates[0]

    def dependency(
        self,
        kind,
        name,
        text,
        src,
        inputs=(),
        outputs=(),
        modes=(),
        status=None,
        record=None,
    ):
        """CALL/SQL/fileを、実行時にfixtureを注入できる独立した依存契約としてIRへ保存する。"""
        d = dict(
            id=f"d{len(self.dependencies)+1:06d}",
            kind=kind,
            name=name,
            text=text,
            inputs=list(inputs),
            outputs=list(outputs),
            modes=list(modes),
            status=status,
            recordId=record,
            source=src,
        )
        self.dependencies.append(d)
        return d
