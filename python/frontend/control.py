"""明示stub-controlで原文と構造を保持する制御契約。bodyはnative命令へ変換しない。"""

import re
from .models import ParseError, VERBS


class ControlParser:
    def raw_tokens(self, start, end):
        return [dict(text=t.text, source=t.source) for t in self.ts[start:end]]

    def out_of_line(self, index):
        j = index
        if self.ts[j].upper == "WITH":
            j += 3
        if j >= len(self.ts):
            return None
        word = self.ts[j].upper
        if word in VERBS | {
            "UNTIL",
            "VARYING",
            "-",
            "+",
            "(",
            ".",
            "END-PERFORM",
        } or re.fullmatch(r"\d+(?:\.\d+)?", word):
            return None
        if j + 1 < len(self.ts) and self.ts[j + 1].upper in ("TIMES", "("):
            return None
        return j

    def find_stub_targets(self):
        targets = set()
        ranges = []
        for i in range(self.i, len(self.ts) - 1):
            if self.ts[i].upper != "PERFORM":
                continue
            j = self.out_of_line(i + 1)
            if j is not None:
                start = self.ts[j].upper
                targets.add(start)
                if j + 2 < len(self.ts) and self.ts[j + 1].upper in ("THRU", "THROUGH"):
                    end = self.ts[j + 2].upper
                    targets.add(end)
                    ranges.append((start, end))
        labels = [
            self.ts[i].upper
            for i in range(self.i, len(self.ts) - 1)
            if self.ts[i + 1].upper in (".", "SECTION")
            and self.ts[i].upper not in VERBS
            and self.ts[i - 1].text == "."
        ]
        for start, end in ranges:
            if (
                start in labels
                and end in labels
                and labels.index(start) <= labels.index(end)
            ):
                targets.update(labels[labels.index(start) : labels.index(end) + 1])
        return targets

    def stub(self, kind, start, subjects=(), cases=(), perform=None):
        self.diag(
            "CONTROL_STUB",
            kind
            + " requires explicit ControlStubPort implementation; native equivalence unverified",
            self.source(start),
            "review",
        )
        return dict(
            kind="controlStub",
            stubId=f"control-{self.program_id}-s{self.sid-1:06d}",
            controlKind=kind,
            subjects=list(subjects),
            cases=list(cases),
            perform=perform,
            tokens=self.raw_tokens(start, self.i),
            stateDependency="program-state",
        )

    def evaluate_stub(self):
        start = self.i - 1
        subjects = []
        while True:
            a = self.i
            expression = None
            unresolved = None
            try:
                expression = self.expr()
                if self.peek() not in ("ALSO", "WHEN"):
                    raise ParseError("Unresolved EVALUATE subject")
            except ParseError as error:
                unresolved = str(error)
                expression = None
                while self.peek() and self.peek() not in (
                    "ALSO",
                    "WHEN",
                    "END-EVALUATE",
                    ".",
                ):
                    self.take()
            if a == self.i:
                raise ParseError("Missing EVALUATE subject")
            subjects.append(
                dict(
                    expression=expression,
                    raw=self.source(a)["raw"],
                    unresolved=unresolved,
                )
            )
            if not self.accept("ALSO"):
                break
        cases = []
        while self.accept("WHEN"):
            if any(c["other"] for c in cases):
                raise ParseError("WHEN OTHER must be last")
            selections = []
            other = self.accept("OTHER")
            if not other:
                while True:
                    a = self.i
                    negated = self.accept("NOT")
                    low, high = None, None
                    kind = "value"
                    operator = None
                    if self.accept("ANY"):
                        kind = "any"
                    else:
                        try:
                            if self.peek() in ("=", "<", ">", "<=", ">=", "<>"):
                                operator = self.take().upper
                                kind = "partial"
                            low = self.expr(4)
                            if self.peek() in ("THRU", "THROUGH"):
                                self.take()
                                high = self.expr(4)
                                kind = "range"
                        except ParseError:
                            kind = "unresolved"
                            while self.peek() and self.peek() not in VERBS | {
                                "ALSO",
                                "WHEN",
                                "END-EVALUATE",
                                ".",
                            }:
                                self.take()
                    if self.i == a:
                        raise ParseError("Missing WHEN selection")
                    selections.append(
                        dict(
                            kind=kind,
                            raw=self.source(a)["raw"],
                            negated=negated,
                            operator=operator,
                            low=low,
                            high=high,
                        )
                    )
                    if not self.accept("ALSO"):
                        break
                if len(selections) != len(subjects):
                    raise ParseError("EVALUATE selection count mismatch")
            a = self.i
            nested = []
            while self.peek():
                word = self.peek()
                if not nested and word in ("WHEN", "END-EVALUATE", "."):
                    break
                if word in ("EVALUATE", "SEARCH"):
                    nested.append(word)
                elif word in ("END-EVALUATE", "END-SEARCH"):
                    expected = word[4:]
                    if not nested or nested[-1] != expected:
                        raise ParseError("Invalid nested stub scope")
                    nested.pop()
                self.take()
            if nested:
                raise ParseError("Unclosed nested control stub")
            body = self.source(a) if self.i > a else self.ts[max(0, a - 1)].source
            cases.append(
                dict(
                    id=f"case-{len(cases)+1}",
                    other=other,
                    selections=selections,
                    body=body,
                    tokens=self.raw_tokens(a, self.i),
                )
            )
        if not cases:
            raise ParseError("EVALUATE requires WHEN")
        self.end("END-EVALUATE")
        return self.stub("evaluate", start, subjects, cases)

    def perform_stub(self):
        start = self.i - 1
        test = "before"
        if self.accept("WITH"):
            self.require("TEST")
            test = self.take().upper.lower()
            if test not in ("before", "after"):
                raise ParseError("Invalid TEST phrase")
        name = self.take().text
        thru = name
        if self.peek() in ("THRU", "THROUGH"):
            self.take()
            thru = self.take().text
        times, until, varying = None, None, []
        if self.accept("WITH"):
            self.require("TEST")
            test = self.take().upper.lower()
            if test not in ("before", "after"):
                raise ParseError("Invalid TEST phrase")
        if self.accept("UNTIL"):
            until = self.expr()
        elif self.accept("VARYING"):
            while True:
                reference = self.reference()
                self.require("FROM")
                initial = self.expr()
                self.require("BY")
                by = self.expr()
                self.require("UNTIL")
                condition = self.expr()
                varying.append(
                    dict(
                        reference=reference, **{"from": initial}, by=by, until=condition
                    )
                )
                if not self.accept("AFTER"):
                    break
        elif self.peek() not in VERBS | {".", "", "END-IF", "ELSE", "END-PERFORM"}:
            times = self.expr(6)
            self.require("TIMES")
        if self.peek() not in VERBS | {".", "", "END-IF", "ELSE", "END-PERFORM"}:
            raise ParseError("Unconsumed PERFORM clause")
        metadata = dict(
            targetKind="paragraph",
            **{"from": name},
            thru=thru,
            test=test,
            times=times,
            until=until,
            varying=varying,
            targets=[],
            missingTarget=True,
        )
        return self.stub("perform", start, perform=metadata)

    def resolve_stub_targets(self):
        def walk(body):
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
                ):
                    if isinstance(s.get(key), list):
                        yield from walk(s[key])

        for p in self.paragraphs:
            for s in walk(p["statements"]):
                if s["kind"] != "controlStub" or not s["perform"]:
                    continue
                m = s["perform"]

                def indices(name):
                    return [
                        i
                        for i, p in enumerate(self.paragraphs)
                        if p["name"].upper() == name.upper()
                        or (p["section"] or "").upper() == name.upper()
                    ]

                start, end = indices(m["from"]), indices(m["thru"])
                m["targetKind"] = (
                    "section"
                    if any(
                        (p["section"] or "").upper() == m["from"].upper()
                        for p in self.paragraphs
                    )
                    else "paragraph"
                )
                if start and end:
                    if start[0] > end[-1]:
                        raise ParseError("Invalid stub PERFORM range")
                    selected = self.paragraphs[start[0] : end[-1] + 1]
                    m["targets"] = []
                    for paragraph in selected:
                        source = dict(paragraph["source"])
                        if paragraph["statements"]:
                            last = paragraph["statements"][-1]["source"]
                            if last["file"] == source["file"]:
                                source["end"] = last["end"]
                                original = bytes.fromhex(
                                    self.f.sources[source["file"]]["bytesHex"]
                                )
                                source["raw"] = original[
                                    source["start"]["byteOffset"] : source["end"][
                                        "byteOffset"
                                    ]
                                ].decode("utf-8")
                        m["targets"].append(dict(name=paragraph["name"], source=source))
                    m["missingTarget"] = False
