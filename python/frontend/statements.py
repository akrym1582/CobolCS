"""statementsの位置付き解析。実行の意味はTS runtimeへ渡す。"""

from __future__ import annotations
import re
from .models import ParseError, Token, VERBS, ENDS, lit, binary


class StatementParser:
    def statement(self):
        """1命令を型付きIRへ変換する。各verbの分岐を見れば、対応する構文と拒否する句が分かる。
        MOVEやIFを実行する場所ではない。ここではoperandと子ブロックを構造化する。
        """
        verb = self.take().upper
        if verb == "COPY-STUB":
            name = self.take().text
            raise ParseError(
                "Missing procedure COPY "
                + name
                + "; behavior must be implemented explicitly"
            )
        if verb == "MOVE":
            if self.peek() in ("CORRESPONDING", "CORR"):
                self.take()
                source = self.reference()
                self.require("TO")
                target = self.reference()
                return dict(
                    kind="corresponding",
                    verb="MOVE",
                    value=source,
                    target=target,
                    onError=[],
                    notError=[],
                    hasSizeError=False,
                )
            value = self.expr()
            self.require("TO")
            targets = []
            while self.peek() and self.peek() not in VERBS | ENDS | {"."}:
                targets.append(self.reference())
            if not targets:
                raise ParseError("MOVE requires target")
            return dict(kind="move", value=value, targets=targets)
        if verb == "INITIALIZE":
            targets = []
            while self.peek() and self.peek() not in VERBS | ENDS | {
                "WITH",
                "REPLACING",
                ".",
            }:
                targets.append(self.reference())
            with_filler = False
            if self.accept("WITH"):
                self.require("FILLER")
                with_filler = True
            replacing = []
            if self.accept("REPLACING"):
                categories = {
                    "NUMERIC": "numeric",
                    "ALPHANUMERIC": "text",
                    "ALPHABETIC": "alphabetic",
                    "NATIONAL": "national",
                    "NUMERIC-EDITED": "numeric-edited",
                }
                while self.peek() in categories:
                    category = categories[self.take().upper]
                    self.accept("DATA")
                    self.require("BY")
                    replacing.append(dict(category=category, value=self.expr(6)))
            if not targets:
                raise ParseError("INITIALIZE requires targets")
            return dict(
                kind="initialize",
                targets=targets,
                withFiller=with_filler,
                replacing=replacing,
            )
        if verb == "STRING":
            sendings = []
            pending = []
            while self.peek() not in ("INTO", ""):
                pending.append(self.expr(6))
                if self.accept("DELIMITED"):
                    self.require("BY")
                    delimiter = None if self.accept("SIZE") else self.expr(6)
                    sendings.extend(
                        dict(value=value, delimiter=delimiter) for value in pending
                    )
                    pending = []
            if pending or not sendings:
                raise ParseError("STRING requires DELIMITED BY for each group")
            self.require("INTO")
            target = self.reference()
            pointer = None
            if self.accept("WITH") or self.peek() == "POINTER":
                self.require("POINTER")
                pointer = self.reference()
            on, not_on, has = self.overflow_clauses("END-STRING")
            return dict(
                kind="stringTransfer",
                sendings=sendings,
                target=target,
                pointer=pointer,
                onOverflow=on,
                notOverflow=not_on,
                hasOverflow=has,
            )
        if verb == "UNSTRING":
            value = self.reference()
            self.require("DELIMITED")
            self.require("BY")
            delimiters = []
            while True:
                all_delimiter = self.accept("ALL")
                delimiters.append(dict(value=self.expr(6), all=all_delimiter))
                if not self.accept("OR"):
                    break
            self.require("INTO")
            receivers = []
            while self.peek() and self.peek() not in VERBS | ENDS | {
                ".",
                "WITH",
                "POINTER",
                "TALLYING",
            }:
                target = self.reference()
                delimiter = count = None
                if self.accept("DELIMITER"):
                    self.require("IN")
                    delimiter = self.reference()
                if self.accept("COUNT"):
                    self.require("IN")
                    count = self.reference()
                receivers.append(
                    dict(target=target, delimiterIn=delimiter, countIn=count)
                )
            if not receivers:
                raise ParseError("UNSTRING requires receivers")
            pointer = tallying = None
            if self.accept("WITH") or self.peek() == "POINTER":
                self.require("POINTER")
                pointer = self.reference()
            if self.accept("TALLYING"):
                self.require("IN")
                tallying = self.reference()
            on, not_on, has = self.overflow_clauses("END-UNSTRING")
            return dict(
                kind="unstringTransfer",
                value=value,
                delimiters=delimiters,
                receivers=receivers,
                pointer=pointer,
                tallying=tallying,
                onOverflow=on,
                notOverflow=not_on,
                hasOverflow=has,
            )
        if verb == "INSPECT":
            value = self.reference()
            operations = []

            def bounds():
                before = after = None
                while self.peek() in ("BEFORE", "AFTER"):
                    key = self.take().upper
                    self.accept("INITIAL")
                    bound = self.expr(6)
                    if key == "BEFORE":
                        before = bound
                    else:
                        after = bound
                return dict(before=before, after=after)

            while self.peek() in ("TALLYING", "REPLACING", "CONVERTING"):
                clause = self.take().upper
                if clause == "CONVERTING":
                    pattern = self.expr(6)
                    self.require("TO")
                    replacement = self.expr(6)
                    operations.append(
                        dict(
                            verb="convert",
                            mode="all",
                            target=None,
                            pattern=pattern,
                            replacement=replacement,
                            **bounds(),
                        )
                    )
                else:
                    target = None
                    while self.peek() and self.peek() not in VERBS | ENDS | {
                        ".",
                        "TALLYING",
                        "REPLACING",
                        "CONVERTING",
                    }:
                        if clause == "TALLYING" and self.peek() not in (
                            "CHARACTERS",
                            "ALL",
                            "LEADING",
                        ):
                            target = self.reference()
                            self.require("FOR")
                        mode = self.take().upper
                        if mode not in ("CHARACTERS", "ALL", "LEADING", "FIRST") or (
                            clause == "TALLYING" and mode == "FIRST"
                        ):
                            raise ParseError("Invalid INSPECT mode")
                        pattern = None if mode == "CHARACTERS" else self.expr(6)
                        replacement = None
                        if clause == "REPLACING":
                            self.require("BY")
                            replacement = self.expr(6)
                        if clause == "TALLYING" and target is None:
                            raise ParseError("INSPECT requires tally target")
                        operations.append(
                            dict(
                                verb="tally" if clause == "TALLYING" else "replace",
                                mode=mode.lower(),
                                target=target,
                                pattern=pattern,
                                replacement=replacement,
                                **bounds(),
                            )
                        )
                        if self.peek() in VERBS | ENDS | {
                            ".",
                            "TALLYING",
                            "REPLACING",
                            "CONVERTING",
                        }:
                            break
            if not operations:
                raise ParseError("INSPECT requires operations")
            return dict(kind="inspectOperations", value=value, operations=operations)
        if verb == "ACCEPT":
            target = self.reference()
            source = self.take().upper if self.accept("FROM") else "CONSOLE"
            return dict(kind="acceptInput", target=target, inputSource=source)
        if verb == "DISPLAY":
            return dict(kind="display", values=self.expressions())
        if verb == "CONTINUE":
            return dict(kind="continue")
        if verb == "SET":
            targets = []
            while self.peek() not in ("TO", "UP", "DOWN", "", "."):
                targets.append(self.expr(6))
            if not targets:
                raise ParseError("SET requires target")
            if self.accept("TO"):
                if self.accept("TRUE"):
                    if len(targets) != 1 or targets[0]["kind"] != "condition88":
                        raise ParseError("SET TRUE requires condition-name")
                    return dict(
                        kind="set88",
                        symbolId=targets[0]["symbolId"],
                        subscripts=targets[0]["subscripts"],
                    )
                value = self.expr(6)
                return dict(
                    kind="setIndex", targets=targets, operation="to", value=value
                )
            operation = (
                "up" if self.accept("UP") else "down" if self.accept("DOWN") else None
            )
            if not operation:
                raise ParseError("Invalid SET operation")
            self.require("BY")
            return dict(
                kind="setIndex",
                targets=targets,
                operation=operation,
                value=self.expr(6),
            )
        if verb == "SEARCH":
            all_search = self.accept("ALL")
            table = self.reference()
            if table["subscripts"] or table["modification"]:
                raise ParseError("SEARCH requires whole table name")
            varying = self.reference() if self.accept("VARYING") else None
            at_end = []
            if self.accept("AT"):
                self.require("END")
                at_end = self.block({"WHEN", "END-SEARCH"})
            cases = []
            while self.accept("WHEN"):
                condition = self.expr()
                body = self.block({"WHEN", "END-SEARCH"})
                cases.append(dict(condition=condition, body=body))
            self.end("END-SEARCH")
            if not cases:
                raise ParseError("SEARCH requires WHEN")
            return dict(
                kind="search",
                tableId=table["symbolId"],
                all=all_search,
                varying=varying,
                atEnd=at_end,
                cases=cases,
            )
        if verb == "IF":
            cond = self.expr()
            self.accept("THEN")
            yes = self.block({"ELSE", "END-IF"})
            no = []
            if self.accept("ELSE"):
                no = self.block({"END-IF"})
            self.end("END-IF")
            return dict(kind="if", condition=cond, then=yes, **{"else": no})
        if verb == "EVALUATE":
            # Inspect subjects/selections, excluding paragraph THRU inside a body.
            compound = False
            selection = True
            nested = 0
            for token in self.ts[self.i:]:
                word = token.upper
                if word == ".":
                    break
                if word == "END-EVALUATE":
                    if nested == 0:
                        break
                    nested -= 1
                    continue
                if word == "EVALUATE":
                    nested += 1
                    selection = False
                if nested:
                    continue
                if word == "WHEN":
                    selection = True
                elif word in VERBS:
                    selection = False
                if selection and word in ("ALSO", "ANY", "THRU", "THROUGH"):
                    compound = True
                    break
            if self.f.config["controlFlowMode"] == "stub-control" or compound:
                return self.evaluate_stub()
        if verb == "EVALUATE":
            subject = self.expr()
            cases = []
            other = []
            while self.accept("WHEN"):
                if self.accept("OTHER"):
                    other = self.block({"WHEN", "END-EVALUATE"})
                    if self.peek() == "WHEN":
                        raise ParseError("WHEN OTHER must be last")
                else:
                    values = [self.expr()]
                    while self.accept("WHEN"):
                        values.append(self.expr())
                    cases.append(
                        dict(values=values, body=self.block({"WHEN", "END-EVALUATE"}))
                    )
            self.end("END-EVALUATE")
            return dict(kind="evaluate", subject=subject, cases=cases, other=other)
        if (
            verb == "PERFORM"
            and self.f.config["controlFlowMode"] == "stub-control"
            and self.out_of_line(self.i) is not None
        ):
            return self.perform_stub()
        if verb == "PERFORM":
            after = False
            times = None
            until = None
            varying = None
            varying_after = []
            if self.accept("WITH"):
                self.require("TEST")
                after = self.accept("AFTER")
                if not after:
                    self.require("BEFORE")
            if self.accept("VARYING"):
                reference = self.reference()
                self.require("FROM")
                start = self.expr()
                self.require("BY")
                by = self.expr()
                self.require("UNTIL")
                until = self.expr()
                varying = dict(reference=reference, by=by, **{"from": start})
                while self.accept("AFTER"):
                    ref = self.reference()
                    self.require("FROM")
                    frm = self.expr()
                    self.require("BY")
                    step = self.expr()
                    self.require("UNTIL")
                    condition = self.expr()
                    varying_after.append(
                        dict(reference=ref, by=step, until=condition, **{"from": frm})
                    )
            elif self.accept("UNTIL"):
                until = self.expr()
            elif self.peek() not in VERBS and self.peek() not in (".", "END-PERFORM"):
                if (
                    re.fullmatch(r"\d+(?:\.\d+)?", self.peek())
                    or self.peek() in ("-", "+")
                    or self.peek(1) == "TIMES"
                    or self.peek(1) == "("
                ):
                    times = self.expr()
                    self.require("TIMES")
                else:
                    start = self.take().text
                    end = start
                    if self.peek() in ("THRU", "THROUGH"):
                        self.take()
                        end = self.take().text
                    if self.peek() not in VERBS | ENDS | {".", ""}:
                        times = self.expr(6)
                        self.require("TIMES")
                        inner = dict(
                            kind="perform",
                            id=f"s{self.sid:06d}",
                            source=self.source(self.i - 1),
                            **{"from": start, "thru": end},
                        )
                        self.sid += 1
                        return dict(
                            kind="loop",
                            body=[inner],
                            times=times,
                            until=None,
                            varying=None,
                            testAfter=False,
                        )
                    return dict(kind="perform", **{"from": start, "thru": end})
            body = self.block({"END-PERFORM"})
            self.end("END-PERFORM")
            if times is None and until is None:
                times = lit("1")
            return dict(
                kind="loop",
                body=body,
                times=times,
                until=until,
                varying=varying,
                varyingAfter=varying_after,
                testAfter=after,
            )
        if verb == "GO":
            self.require("TO")
            targets = [self.take().text]
            while self.peek() and self.peek() not in VERBS | ENDS | {"DEPENDING", "."}:
                if (
                    self.peek(1) == "."
                    and self.ts[self.i].source["start"]["line"]
                    != self.ts[self.i - 1].source["start"]["line"]
                ):
                    break
                targets.append(self.take().text)
            if self.accept("DEPENDING"):
                self.require("ON")
                return dict(
                    kind="gotoDepending", targets=targets, depending=self.expr()
                )
            if len(targets) != 1:
                raise ParseError("GO TO requires one target or DEPENDING")
            return dict(kind="goto", target=targets[0])
        if verb == "NEXT":
            self.require("SENTENCE")
            return dict(kind="nextSentence", sentenceId="")
        if verb == "GOBACK":
            return dict(kind="halt", signal="goback")
        if verb == "STOP":
            self.require("RUN")
            return dict(kind="halt", signal="stopRun")
        if verb == "EXIT":
            if self.accept("PERFORM"):
                return dict(kind="exitPerform")
            if self.accept("PROGRAM"):
                return dict(kind="halt", signal="exitProgram")
            return dict(kind="continue")
        if verb in ("ADD", "SUBTRACT", "MULTIPLY", "DIVIDE", "COMPUTE"):
            return self.arithmetic(verb)
        if verb in ("SORT", "MERGE", "RELEASE", "RETURN"):
            return self.sort_statement(verb)
        if verb == "CANCEL":
            return dict(kind="cancel", targets=self.expressions())
        if verb == "CALL":
            a = self.i - 1
            target = self.expr(6)
            inputs = []
            modes = []
            mode = "reference"
            if self.accept("USING"):
                while self.peek() and self.peek() not in VERBS | ENDS | {
                    ".",
                    "RETURNING",
                }:
                    if self.accept("BY"):
                        word = self.take().upper
                        if word not in ("REFERENCE", "CONTENT", "VALUE"):
                            raise ParseError("Invalid CALL mode")
                        mode = word.lower()
                    else:
                        inputs.append(
                            self.reference() if mode == "reference" else self.expr(6)
                        )
                        modes.append(mode)
            returning = self.reference() if self.accept("RETURNING") else None
            on = []
            not_on = []
            if self.accept("ON"):
                self.require("EXCEPTION")
                on = self.block({"NOT", "END-CALL"})
            if self.accept("NOT"):
                self.require("ON")
                self.require("EXCEPTION")
                not_on = self.block({"END-CALL"})
            self.accept("END-CALL")
            name = (
                target["value"]
                if target["kind"] == "literal" and target["valueType"] == "text"
                else "$dynamic"
            )
            d = self.dependency(
                "call",
                name,
                self.source(a)["raw"],
                self.source(a),
                inputs=inputs,
                modes=modes,
            )
            d["call"] = dict(target=target, returning=returning)
            return dict(
                kind="external",
                dependencyId=d["id"],
                operation="call",
                atEnd=on,
                notEnd=not_on,
            )
        if verb == "EXEC":
            return self.sql_statement()
        if verb in ("OPEN", "READ", "WRITE", "CLOSE", "REWRITE", "START", "DELETE"):
            return self.file_statement(verb)
        raise ParseError("Unsupported verb " + verb)

    def arithmetic(self, verb):
        """算術命令の受け側・ROUNDED・GIVING・SIZE ERROR・REMAINDERをIRへまとめる。
        例: ADD 1 TO Aは旧Aへの加算、ADD 1 TO A GIVING Bは結果をBへ格納するという違いを保存する。
        中間値の精度や丸めはruntimeが扱うので、解析時にfloatで答えを計算しない。
        """
        update = False
        rem = None
        targets = []
        if verb in ("ADD", "SUBTRACT") and self.peek() in ("CORRESPONDING", "CORR"):
            self.take()
            value = self.reference()
            self.require("TO" if verb == "ADD" else "FROM")
            target = self.reference()
            error, normal, has = self.size_error(verb)
            return dict(
                kind="corresponding",
                verb=verb,
                value=value,
                target=target,
                onError=error,
                notError=normal,
                hasSizeError=has,
            )
        if verb == "COMPUTE":
            while self.peek() and self.peek() != "=":
                target = self.reference()
                targets.append(dict(reference=target, rounded=self.accept("ROUNDED")))
            self.require("=")
            value = self.expr()
        else:
            stop = {
                "ADD": "TO",
                "SUBTRACT": "FROM",
                "MULTIPLY": "BY",
                "DIVIDE": "INTO",
            }[verb]
            values = self.expressions({stop, "BY", "GIVING"})
            value = values[0]
            if verb in ("ADD", "SUBTRACT"):
                for x in values[1:]:
                    value = binary("+", value, x)
            elif len(values) != 1:
                raise ParseError("Expected one multiplicand/divisor")
            if verb == "DIVIDE" and self.accept("BY"):
                divisor = self.expr()
                value = binary("/", value, divisor)
                self.require("GIVING")
            else:
                self.require(stop)
                receivers = []
                while self.peek() and self.peek() not in VERBS | ENDS | {
                    "GIVING",
                    "ROUNDED",
                    "REMAINDER",
                    ".",
                }:
                    receivers.append(self.reference())
                    if self.accept("ROUNDED"):
                        receivers[-1] = {"ref": receivers[-1], "rounded": True}
                if not receivers:
                    raise ParseError("Missing arithmetic receiver")
                if self.accept("GIVING"):
                    if len(receivers) != 1:
                        raise ParseError("GIVING requires single base operand")
                    base = receivers[0]
                    if "ref" in base:
                        raise ParseError("ROUNDED belongs after GIVING")
                    value = binary(
                        {"ADD": "+", "SUBTRACT": "-", "MULTIPLY": "*", "DIVIDE": "/"}[
                            verb
                        ],
                        base,
                        value,
                    )
                else:
                    update = True
                    targets = [
                        dict(reference=x.get("ref", x), rounded=x.get("rounded", False))
                        for x in receivers
                    ]
            if not targets:
                while self.peek() and self.peek() not in VERBS | ENDS | {
                    "REMAINDER",
                    ".",
                }:
                    target = self.reference()
                    targets.append(
                        dict(reference=target, rounded=self.accept("ROUNDED"))
                    )
        if self.accept("REMAINDER"):
            if (
                verb != "DIVIDE"
                or update
                or len(targets) != 1
                or targets[0]["rounded"]
                or value["kind"] != "binary"
                or value["op"] != "/"
            ):
                raise ParseError("Unsupported DIVIDE REMAINDER combination")
            rem = self.reference()
        error, normal, has = self.size_error(verb)
        if not targets:
            raise ParseError("No arithmetic targets")
        return dict(
            kind="arithmetic",
            verb=verb,
            value=value,
            update=update,
            targets=targets,
            remainder=rem,
            onError=error,
            notError=normal,
            hasSizeError=has,
        )

    def size_error(self, verb):
        error = []
        normal = []
        has = False
        if self.accept("ON"):
            self.require("SIZE")
            self.require("ERROR")
            has = True
            error = self.block({"NOT", "END-" + verb})
        if self.accept("NOT"):
            self.require("ON")
            self.require("SIZE")
            self.require("ERROR")
            normal = self.block({"END-" + verb})
        self.accept("END-" + verb)
        return error, normal, has

    def overflow_clauses(self, end):
        on = []
        not_on = []
        has = False
        if self.accept("ON"):
            self.require("OVERFLOW")
            has = True
            on = self.block(
                {
                    "NOT",
                    end,
                    "ELSE",
                    "END-IF",
                    "END-PERFORM",
                    "END-EVALUATE",
                    "END-SEARCH",
                }
            )
        if self.accept("NOT"):
            self.require("ON")
            self.require("OVERFLOW")
            not_on = self.block(
                {end, "ELSE", "END-IF", "END-PERFORM", "END-EVALUATE", "END-SEARCH"}
            )
        if not self.accept(end) and self.peek() not in VERBS | ENDS | {".", ""}:
            raise ParseError("Missing " + end)
        return on, not_on, has
