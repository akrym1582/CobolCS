"""SELECT/FDとファイル命令をdescriptorへ接続する。ASSIGN名をOS pathとして実行しない。"""

from .models import ParseError, VERBS, ENDS, lit
import re


class FileParser:
    def file_record(self, name, kind, clause, source):
        dep = next(
            (
                d
                for d in self.dependencies
                if d["kind"] == "file" and d["name"].upper() == name.upper()
            ),
            None,
        )
        if kind == "SD":
            if clause:
                raise ParseError("Unsupported SD clause")
            if dep is None:
                dep = self.dependency("file", name, "SD " + name, source)
            dep["sortFile"] = True
        if dep is None:
            raise ParseError("FD has no SELECT " + name)
        text = " ".join(t.text for t in clause)
        if not text:
            return
        variable = re.fullmatch(
            r"RECORD (?:IS )?VARYING (?:IN )?SIZE (?:FROM )?(\d+) TO (\d+) (?:CHARACTERS )?DEPENDING (?:ON )?([\w-]+)",
            text,
            re.I,
        )
        fixed = re.fullmatch(r"RECORD CONTAINS (\d+) (?:CHARACTERS)?", text, re.I)
        if not variable and not fixed:
            raise ParseError("Unsupported FD record clause")
        dep["recordClause"] = dict(
            min=int(variable[1]) if variable else int(fixed[1]),
            max=int(variable[2]) if variable else int(fixed[1]),
            depending=variable[3] if variable else None,
        )

    def file_control(self, dep, ts, source):
        words = [t.upper for t in ts]
        j = 1
        optional = False
        if words[j] == "OPTIONAL":
            optional = True
            j += 1
        j += 1  # SELECT name
        organization = "sequential"
        access = "sequential"
        assign = ""
        keys = []
        relative = None
        while j < len(ts) and words[j] != ".":
            word = words[j]
            j += 1
            if word == "ASSIGN":
                if words[j] == "TO":
                    j += 1
                assign = ts[j].text
                j += 1
            elif word == "ORGANIZATION":
                if words[j] == "IS":
                    j += 1
                if words[j] == "LINE" and words[j + 1] == "SEQUENTIAL":
                    organization = "line-sequential"
                    j += 2
                else:
                    organization = words[j].lower()
                    j += 1
                if organization not in (
                    "sequential",
                    "indexed",
                    "relative",
                    "line-sequential",
                ):
                    raise ParseError("Unsupported file organization")
            elif word == "ACCESS":
                if words[j] != "MODE":
                    raise ParseError("ACCESS requires MODE")
                j += 1
                if words[j] == "IS":
                    j += 1
                access = words[j].lower()
                j += 1
                if access not in ("sequential", "random", "dynamic"):
                    raise ParseError("Invalid ACCESS MODE")
            elif word == "FILE":
                if words[j] != "STATUS":
                    raise ParseError("FILE requires STATUS")
                j += 1
                if words[j] == "IS":
                    j += 1
                j += 1  # status resolved by procedure
            elif word in ("RECORD", "ALTERNATE", "RELATIVE"):
                if word == "ALTERNATE":
                    if words[j] != "RECORD":
                        raise ParseError("ALTERNATE RECORD KEY required")
                    j += 1
                if words[j] != "KEY":
                    raise ParseError("File KEY required")
                j += 1
                if words[j] == "IS":
                    j += 1
                item = self.resolve(ts[j].text)
                j += 1
                if word == "RELATIVE":
                    relative = dict(
                        kind="reference",
                        symbolId=item["id"],
                        subscripts=[],
                        modification=None,
                    )
                else:
                    duplicates = False
                    if words[j] == "WITH":
                        j += 1
                        if words[j] != "DUPLICATES":
                            raise ParseError("Invalid alternate KEY option")
                        j += 1
                        duplicates = True
                    keys.append(dict(symbolId=item["id"], duplicates=duplicates))
            else:
                raise ParseError("Unsupported SELECT clause " + word)
        record = dep.pop("recordClause", None)
        depending = None
        if record and record["depending"]:
            item = self.resolve(record["depending"])
            depending = dict(
                kind="reference", symbolId=item["id"], subscripts=[], modification=None
            )
        dep["file"] = dict(
            organization=organization,
            access=access,
            optional=optional,
            assign=assign,
            keys=keys,
            relativeKey=relative,
            minLength=record["min"] if record else None,
            maxLength=record["max"] if record else None,
            dependingOn=depending,
        )

    def file_statement(self, verb):
        a = self.i - 1
        if verb in ("OPEN", "CLOSE"):
            nodes = []
            mode = ""
            while self.peek() and self.peek() not in VERBS | ENDS | {"."}:
                if verb == "OPEN" and self.peek() in (
                    "INPUT",
                    "OUTPUT",
                    "I-O",
                    "EXTEND",
                ):
                    mode = self.take().upper
                if verb == "OPEN" and not mode:
                    raise ParseError("OPEN requires mode")
                dep = self.file_dependency(self.take().text)
                node = dict(
                    kind="external",
                    id=f"s{self.sid:06d}",
                    source=self.source(a),
                    dependencyId=dep["id"],
                    operation=(verb + " " + mode).strip().lower(),
                    atEnd=[],
                    notEnd=[],
                )
                self.sid += 1
                nodes.append(node)
            if not nodes:
                raise ParseError("File operand required")
            return dict(kind="sequence", body=nodes)
        name = self.take().text
        if verb in ("WRITE", "REWRITE"):
            record = self.resolve(name)
            dep = next(
                (
                    d
                    for d in self.dependencies
                    if d["kind"] == "file" and d["recordId"] == record["id"]
                ),
                None,
            )
            if not dep:
                raise ParseError("Unknown file record")
        else:
            dep = self.file_dependency(name)
        transfer = key = None
        condition = None
        advancing = None
        if verb == "READ":
            next_record = self.accept("NEXT")
            self.accept("RECORD")
            if self.accept("INTO"):
                transfer = self.reference()
            if self.accept("KEY"):
                self.accept("IS")
                key = self.expr(6)
            operation = "read next" if next_record else "read"
        else:
            operation = verb.lower()
            self.accept("RECORD")
            if self.accept("FROM"):
                transfer = self.expr(6)
            if verb == "START":
                self.require("KEY")
                self.accept("IS")
                condition = self.take().upper
                if condition not in ("=", ">", ">=", "<", "<="):
                    raise ParseError("Unsupported START relation")
                key = self.expr(6)
            if verb == "WRITE" and self.peek() in ("BEFORE", "AFTER"):
                before = self.take().upper == "BEFORE"
                self.require("ADVANCING")
                lines = self.expr(6)
                self.accept("LINES") or self.accept("LINE")
                advancing = dict(before=before, lines=lines)
        at = []
        not_end = []
        invalid = []
        not_invalid = []
        has_end = has_invalid = False
        end = "END-" + verb
        if self.accept("AT"):
            self.require("END")
            has_end = True
            at = self.block({"NOT", end})
        elif self.accept("INVALID"):
            self.require("KEY")
            has_invalid = True
            invalid = self.block({"NOT", end})
        if self.accept("NOT"):
            if self.accept("AT"):
                self.require("END")
                not_end = self.block({end})
            else:
                self.require("INVALID")
                self.require("KEY")
                not_invalid = self.block({end})
        self.accept(end)
        return dict(
            kind="external",
            dependencyId=dep["id"],
            operation=operation,
            transfer=transfer,
            key=key,
            startCondition=condition,
            advancing=advancing,
            atEnd=at,
            notEnd=not_end,
            onInvalid=invalid,
            notInvalid=not_invalid,
            hasInvalid=has_invalid,
            hasEnd=has_end,
        )

    def file_dependency(self, name):
        dep = next(
            (
                d
                for d in self.dependencies
                if d["kind"] == "file" and d["name"].upper() == name.upper()
            ),
            None,
        )
        if not dep or not dep["recordId"]:
            raise ParseError("Unresolved file/record " + name)
        return dep

    def sort_statement(self, verb):
        if verb in ("RELEASE", "RETURN"):
            name = self.take().text
            if verb == "RELEASE":
                record = self.resolve(name)
                dep = next(
                    (
                        d
                        for d in self.dependencies
                        if d.get("sortFile") and d["recordId"] == record["id"]
                    ),
                    None,
                )
            else:
                dep = next(
                    (
                        d
                        for d in self.dependencies
                        if d.get("sortFile") and d["name"].upper() == name.upper()
                    ),
                    None,
                )
            if not dep:
                raise ParseError("RELEASE/RETURN requires SD")
            transfer = (
                self.expr(6)
                if self.accept("FROM" if verb == "RELEASE" else "INTO")
                else None
            )
            at = []
            not_end = []
            if self.accept("AT"):
                self.require("END")
                at = self.block({"NOT", "END-RETURN"})
            if self.accept("NOT"):
                self.require("AT")
                self.require("END")
                not_end = self.block({"END-RETURN"})
            self.accept("END-RETURN")
            return dict(
                kind="sortIo",
                operation=verb.lower(),
                fileId=dep["id"],
                transfer=transfer,
                atEnd=at,
                notEnd=not_end,
            )
        name = self.take().text
        dep = next(
            (
                d
                for d in self.dependencies
                if d.get("sortFile") and d["name"].upper() == name.upper()
            ),
            None,
        )
        if not dep:
            raise ParseError("SORT requires SD")
        self.resolve("SORT-RETURN")
        keys = []
        while self.peek() in ("ON", "ASCENDING", "DESCENDING"):
            self.accept("ON")
            direction = self.take().upper
            if direction not in ("ASCENDING", "DESCENDING"):
                raise ParseError("Invalid SORT key direction")
            self.require("KEY")
            while self.peek() and self.peek() not in VERBS | ENDS | {
                "ON",
                "ASCENDING",
                "DESCENDING",
                "USING",
                "GIVING",
                "INPUT",
                "OUTPUT",
                "WITH",
                ".",
            }:
                item = self.reference()
                if item["subscripts"] or item["modification"]:
                    raise ParseError("SORT key must be SD field")
                keys.append(
                    dict(symbolId=item["symbolId"], direction=direction.lower())
                )
        duplicates = False
        if self.accept("WITH"):
            self.require("DUPLICATES")
            self.require("IN")
            self.require("ORDER")
            duplicates = True
        using = []
        giving = []
        input_proc = output_proc = None

        def procedure():
            self.require("PROCEDURE")
            self.accept("IS")
            first = self.take().text
            last = first
            if self.peek() in ("THRU", "THROUGH"):
                self.take()
                last = self.take().text
            return dict(**{"from": first, "thru": last})

        if self.accept("USING"):
            while self.peek() and self.peek() not in VERBS | ENDS | {
                "GIVING",
                "OUTPUT",
                ".",
            }:
                using.append(self.file_dependency(self.take().text)["id"])
        elif self.accept("INPUT"):
            input_proc = procedure()
        if self.accept("GIVING"):
            while self.peek() and self.peek() not in VERBS | ENDS | {"."}:
                giving.append(self.file_dependency(self.take().text)["id"])
        elif self.accept("OUTPUT"):
            output_proc = procedure()
        if (
            not keys
            or (not using and input_proc is None)
            or (not giving and output_proc is None)
        ):
            raise ParseError("SORT requires keys/input/output")
        if verb == "MERGE" and input_proc:
            raise ParseError("MERGE requires USING")
        return dict(
            kind="sort",
            fileId=dep["id"],
            merge=verb == "MERGE",
            keys=keys,
            using=using,
            giving=giving,
            inputProcedure=input_proc,
            outputProcedure=output_proc,
            duplicatesInOrder=duplicates,
        )
