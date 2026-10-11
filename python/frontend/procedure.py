"""procedureの位置付き解析。実行の意味はTS runtimeへ渡す。"""

from __future__ import annotations
import re
from .models import ParseError, Token, VERBS, ENDS, lit, binary


class ProcedureParser:
    def parse(self):
        """1プログラムの部・宣言・段落・手続きと依存契約を組み立てる。
        最後に全トークンの所属を確認し、未消費の意味トークンはunknownと診断へ残す。
        構文復旧で命令が消えても正常生成できたように見えることを防ぐための確認。
        """
        program_id = ""
        env = []
        # Every pre-data sentence is recognized structurally or diagnosed.
        while self.peek() and self.peek() != "DATA" and self.peek() != "PROCEDURE":
            a = self.i
            t = self.take()
            if t.upper == "PROGRAM-ID":
                self.require(".")
                program_id = self.take().text
                self.require(".")
            elif t.upper in ("IDENTIFICATION", "ENVIRONMENT"):
                self.require("DIVISION")
                self.require(".")
            elif t.upper == "CONFIGURATION":
                self.require("SECTION")
                self.require(".")
            elif t.upper == "SPECIAL-NAMES":
                self.require(".")
            elif t.upper == "DECIMAL-POINT":
                self.require("IS")
                self.require("COMMA")
                self.require(".")
                self.f.config["decimalPoint"] = "comma"
            elif t.upper == "CURRENCY":
                self.require("SIGN")
                self.require("IS")
                symbol = self.take().text
                if not symbol.startswith(('"', "'")) or len(symbol) != 3:
                    raise ParseError("CURRENCY SIGN requires one literal character")
                self.require(".")
                self.f.config["currencySymbol"] = symbol[1]
            elif t.upper in ("INPUT-OUTPUT", "FILE-CONTROL"):
                if t.upper == "INPUT-OUTPUT":
                    self.require("SECTION")
                self.require(".")
            elif t.upper == "SELECT":
                self.accept("OPTIONAL")
                name = self.take().text
                while self.peek() and self.peek() != ".":
                    self.take()
                self.require(".")
                env.append((name, self.ts[a : self.i], self.source(a)))
            elif t.text == ".":
                pass
            else:
                while self.peek() and self.peek() != ".":
                    self.take()
                self.accept(".")
                self.diag("UNKNOWN_HEADER", "Unsupported header syntax", self.source(a))
        for name, ts, src in env:
            self.dependency("file", name, " ".join(t.text for t in ts), src)
        if self.accept("DATA"):
            self.require("DIVISION")
            self.require(".")
            self.declarations()
        for name, ts, src in env:
            words = [t.upper for t in ts]
            dep = next(d for d in self.dependencies if d["name"] == name)
            if "STATUS" in words:
                n = words.index("STATUS") + 1
                if words[n] == "IS":
                    n += 1
                try:
                    s = self.resolve(ts[n].text)
                    dep["status"] = dict(
                        kind="reference",
                        symbolId=s["id"],
                        subscripts=[],
                        modification=None,
                    )
                except ParseError as e:
                    self.diag("FILE_STATUS", str(e), src)
            self.file_control(dep, ts, src)
        if not program_id:
            raise ParseError("Missing PROGRAM-ID")
        self.require("PROCEDURE")
        self.require("DIVISION")
        using = []
        if self.accept("USING"):
            mode = "reference"
            while self.peek() and self.peek() != ".":
                if self.accept("BY"):
                    word = self.take().upper
                    if word not in ("REFERENCE", "CONTENT", "VALUE"):
                        raise ParseError("Invalid PROCEDURE USING mode")
                    mode = word.lower()
                else:
                    symbol = self.resolve(self.take().text)
                    if symbol["scope"] != "LINKAGE" or symbol["parentId"]:
                        raise ParseError("PROCEDURE USING requires LINKAGE roots")
                    using.append(dict(symbolId=symbol["id"], mode=mode))
        self.require(".")
        self.procedure_start = self.i
        section = None
        self.program_id = program_id
        self.stub_target_names = (
            self.find_stub_targets()
            if self.f.config["controlFlowMode"] == "stub-control"
            else set()
        )
        while self.peek():
            if self.accept("."):
                continue
            if self.peek() == "END" and self.peek(1) == "PROGRAM":
                self.take()
                self.take()
                name = self.take().text
                self.require(".")
                for token_index in range(self.i - 4, self.i):
                    self.owners[token_index] = ("declaration", "$endProgram")
                if name != program_id or self.peek():
                    raise ParseError(
                        "Invalid END PROGRAM or multiple programs in one source"
                    )
                break
            if self.peek(1) == "SECTION":
                a = self.i
                section = self.take().text
                self.take()
                self.require(".")
                for token_index in range(a, self.i):
                    self.owners[token_index] = ("declaration", "$section")
                if (
                    not (self.peek(1) == "." and self.peek() not in VERBS)
                    and self.peek(1) != "SECTION"
                ):
                    self.paragraphs.append(
                        dict(
                            id=f"p{len(self.paragraphs):06d}",
                            name="$section-" + section,
                            section=section,
                            order=len(self.paragraphs),
                            statements=[],
                            source=self.source(a),
                        )
                    )
                continue
            if self.peek(1) == "." and self.peek() not in VERBS:
                a = self.i
                name = self.take().text
                self.take()
                p = dict(
                    id=f"p{len(self.paragraphs):06d}",
                    name=name,
                    section=section,
                    order=len(self.paragraphs),
                    statements=[],
                    source=self.source(a),
                )
                self.paragraphs.append(p)
                for token_index in range(a, self.i):
                    self.owners[token_index] = ("declaration", p["id"])
            elif not self.paragraphs:
                self.paragraphs.append(
                    dict(
                        id="p000000",
                        name="$entry",
                        section=section,
                        order=0,
                        statements=[],
                        source=self.ts[self.i].source,
                    )
                )
            paragraph = self.paragraphs[-1]
            if (
                paragraph["name"].upper() in self.stub_target_names
                or (section or "").upper() in self.stub_target_names
            ):
                a = self.i
                while (
                    self.peek()
                    and not (
                        self.peek(1) in (".", "SECTION")
                        and self.peek() not in VERBS
                        and (self.i == a or self.ts[self.i - 1].text == ".")
                    )
                    and not (self.peek() == "END" and self.peek(1) == "PROGRAM")
                ):
                    self.take()
                if self.i >= a:
                    sid = f"s{self.sid:06d}"
                    self.sid += 1
                    paragraph["statements"].append(
                        dict(
                            kind="controlTarget",
                            id=sid,
                            name=paragraph["name"],
                            source=(
                                self.source(a) if self.i > a else paragraph["source"]
                            ),
                            tokens=self.raw_tokens(a, self.i),
                        )
                    )
                    for token_index in range(a, self.i):
                        self.owners[token_index] = ("stubbed", sid)
            else:
                paragraph["statements"] += self.block(set(), top=True)
        for paragraph in self.paragraphs:
            if not paragraph["statements"] and (
                paragraph["name"].upper() in self.stub_target_names
                or (paragraph["section"] or "").upper() in self.stub_target_names
            ):
                paragraph["statements"].append(
                    dict(
                        kind="controlTarget",
                        id=f"s{self.sid:06d}",
                        name=paragraph["name"],
                        source=paragraph["source"],
                        tokens=[],
                    )
                )
                self.sid += 1
        if not self.paragraphs:
            raise ParseError("Empty procedure division")

        def statements(body):
            for s in body:
                yield s
                for key in (
                    "body",
                    "then",
                    "else",
                    "onError",
                    "notError",
                    "atEnd",
                    "notEnd",
                    "onOverflow",
                    "notOverflow",
                ):
                    if isinstance(s.get(key), list):
                        yield from statements(s[key])

                for case in s.get("cases", []):
                    if isinstance(case.get("body"), list):
                        yield from statements(case["body"])

        all_statements = sorted(
            (s for p in self.paragraphs for s in statements(p["statements"])),
            key=lambda s: int(s["id"][1:]),
        )
        for index, s in enumerate(all_statements):
            if s["kind"] == "nextSentence":
                boundary = next(
                    (
                        b
                        for b in all_statements[index + 1 :]
                        if b["kind"] == "sentenceBoundary"
                    ),
                    None,
                )
                if boundary is None:
                    raise ParseError(
                        "NEXT SENTENCE requires following separator period"
                    )
                s["sentenceId"] = boundary["id"]
        self.resolve_stub_targets()
        tokens = []
        for token_index, t in enumerate(self.ts):
            owner = self.owners.get(token_index)
            if owner is None:
                if token_index < self.procedure_start or t.text == ".":
                    owner = ("declaration", "$structure")
                else:
                    owner = ("unknown", "$unconsumed")
                    self.diag("UNCONSUMED_TOKEN", t.text, t.source)
            tokens.append(
                dict(text=t.text, ownerKind=owner[0], ownerId=owner[1], source=t.source)
            )
        for item in self.symbols:
            if (
                item["pic"] is None
                and item["initial"]
                and item["initial"].get("valueType") == "numeric"
            ):
                self.diag(
                    "GROUP_NUMERIC_VALUE",
                    "Group VALUE cannot be numeric",
                    item["source"],
                )
            if item["redefines"]:
                target = next(s for s in self.symbols if s["id"] == item["redefines"])
                if (
                    target["level"] != item["level"]
                    or target["parentId"] != item["parentId"]
                ):
                    self.diag(
                        "REDEFINES_RELATION",
                        "REDEFINES requires same-level preceding sibling",
                        item["source"],
                    )
                if target["occurs"] != 1 or item["occurs"] != 1:
                    self.diag(
                        "REDEFINES_OCCURS",
                        "REDEFINES of OCCURS items unsupported",
                        item["source"],
                    )

        for diagnostic in self.ds:
            diagnostic["programId"] = program_id
            location = diagnostic.get("source") or {}
            matching = [
                statement for statement in all_statements
                if statement["source"]["file"] == location.get("file")
                and statement["source"]["start"]["byteOffset"] <= location.get("start", {}).get("byteOffset", -1)
                and statement["source"]["end"]["byteOffset"] >= location.get("end", {}).get("byteOffset", -1)
            ]
            if matching:
                closest = min(matching, key=lambda item: item["source"]["end"]["byteOffset"] - item["source"]["start"]["byteOffset"])
                diagnostic["statementId"] = closest["id"]

        return dict(
            id=program_id,
            symbols=self.symbols,
            tokens=tokens,
            paragraphs=self.paragraphs,
            dependencies=self.dependencies,
            using=using,
            diagnostics=self.ds,
        )

    def block(self, stops, top=False):
        """ELSE/END-IF等の指定終端かピリオドまで、命令のブロックを読む。
        命令が解析できない場合は原文付きunknownを作り、次の既知命令から復旧する。
        回復しても未知処理を正常なCONTINUEへ変換したことにはしない。
        """
        body = []
        while self.peek() and self.peek() not in stops and self.peek() != ".":
            if top and (
                self.peek(1) in (".", "SECTION")
                and self.peek() not in VERBS
                or self.peek() == "END"
                and self.peek(1) == "PROGRAM"
            ):
                break
            a = self.i
            sid = f"s{self.sid:06d}"
            self.sid += 1
            try:
                node = self.statement()
            except (ParseError, ValueError) as e:
                # Never discard an uncertain region or create a successful fallback.
                block_verb = self.ts[a].upper
                if block_verb in {"IF", "PERFORM", "EVALUATE", "SEARCH", "STRING", "UNSTRING", "INSPECT", "CALL", "EXEC", "READ", "WRITE", "REWRITE", "DELETE", "START", "ADD", "SUBTRACT", "MULTIPLY", "DIVIDE", "COMPUTE"}:
                    # An uncertain block must not leak its body into ordinary execution.
                    self.i = a + 1
                    depth = 1
                    while self.peek() and self.peek() != ".":
                        word = self.take().upper
                        if word == block_verb:
                            depth += 1
                        elif word == "END-" + block_verb:
                            depth -= 1
                            if depth == 0:
                                break
                else:
                    if self.i == a:
                        self.take()
                    while self.peek() and self.peek() not in VERBS | stops | {"."}:
                        self.take()
                src = self.source(a)
                self.diag("UNSUPPORTED_STATEMENT", str(e), src)
                node = dict(kind="unknown", reason=str(e))
            for token_index in range(a, self.i):
                if node["kind"] == "unknown" or token_index not in self.owners:
                    self.owners[token_index] = (
                        (
                            "unknown"
                            if node["kind"] == "unknown"
                            else (
                                "stubbed"
                                if node["kind"] == "controlStub"
                                else "statement"
                            )
                        ),
                        sid,
                    )
            node.update(id=sid, source=self.source(a))
            body.append(node)
            if top and self.peek() == ".":
                token = self.take()
                body.append(
                    dict(
                        kind="sentenceBoundary",
                        id=f"s{self.sid:06d}",
                        source=token.source,
                    )
                )
                self.sid += 1
                break
        return body

    def end(self, word):
        """END-IFなどの明示終端を確認する。ピリオドは文全体のscopeを閉じる別の終端として認める。"""
        if not self.accept(word) and self.peek() != ".":
            raise ParseError("Missing " + word)
