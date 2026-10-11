"""declarationsの位置付き解析。実行の意味はTS runtimeへ渡す。"""

from __future__ import annotations
import re
from .models import ParseError, Token, VERBS, ENDS, lit, binary

from .models import parse_pic


class DeclarationParser:
    def declarations(self):
        """DATA DIVISIONを読み、レベル番号から親子関係を作る。

        stackには現在の祖先項目を置く。新しい05項目なら同じ05やそれ以下をpopし、上位の01等を親にする。
        PICは意味上の型、occursは最大件数、REDEFINESは共有先のIDとして保存する。
        実際のoffsetやbyte長はTS compilerが明示プロファイルから計算する。
        """
        scope = "WORKING-STORAGE"
        stack = []
        last = None
        current_file = None
        pending_keys = []
        while self.peek() and self.peek() != "PROCEDURE":
            a = self.i
            t = self.take()
            if (
                t.upper in ("WORKING-STORAGE", "LOCAL-STORAGE", "LINKAGE", "FILE")
                and self.peek() == "SECTION"
            ):
                scope = t.upper
                self.take()
                self.require(".")
                stack = []
                continue
            if t.upper in ("FD", "SD"):
                name = self.take().text
                clause = []
                while self.peek() and self.peek() != ".":
                    clause.append(self.take())
                self.accept(".")
                self.file_record(name, t.upper, clause, self.source(a))
                current_file = name
                stack = []
                continue
            if t.text == ".":
                continue
            if t.upper == "COPY-STUB":
                self.take()
                self.require(".")
                for token_index in range(a, self.i):
                    self.owners[token_index] = ("declaration", "$copyStub")
                continue
            if not t.text.isdigit():
                while self.peek() and self.peek() != ".":
                    self.take()
                self.accept(".")
                self.diag(
                    "UNKNOWN_DECLARATION", "Unsupported data syntax", self.source(a)
                )
                continue
            level = int(t.text)
            name = self.take().text
            pic = None
            usage = "DISPLAY"
            occurs = 1
            occurs_declared = False
            redefine = None
            initial = None
            conditions = []
            depending = None
            indexes = []
            keys = []
            occurs_min = 1
            sign_clause = None
            justified = synchronized = blank_zero = False
            try:
                if level not in (*range(1, 50), 77, 88):
                    raise ParseError("Unsupported level " + str(level))
                while self.peek() and self.peek() != ".":
                    clause = self.take().upper
                    if clause in ("PIC", "PICTURE"):
                        self.accept("IS")
                        parts = []
                        stops = set(
                            "VALUE VALUES USAGE COMP COMP-1 COMP-2 COMP-3 COMP-5 COMPUTATIONAL COMPUTATIONAL-1 COMPUTATIONAL-2 COMPUTATIONAL-3 COMPUTATIONAL-5 BINARY PACKED-DECIMAL DISPLAY INDEX OCCURS INDEXED ASCENDING DESCENDING REDEFINES SIGN JUSTIFIED JUST SYNC SYNCHRONIZED BLANK".split()
                        )
                        while self.peek() and self.peek() not in stops:
                            if self.peek() == ".":
                                previous = self.ts[self.i - 1].source
                                following = (
                                    self.ts[self.i + 1]
                                    if self.i + 1 < len(self.ts)
                                    else None
                                )
                                if (
                                    not following
                                    or previous["end"]["byteOffset"]
                                    != self.ts[self.i].source["start"]["byteOffset"]
                                    or self.ts[self.i].source["end"]["byteOffset"]
                                    != following.source["start"]["byteOffset"]
                                    or not re.fullmatch(r"[9Z*]+", following.text)
                                ):
                                    break
                            parts.append(self.take().text)
                        raw = "".join(parts).upper()
                        if len(raw) > 100 or any(
                            int(n) > 10000 or int(n) < 1
                            for n in re.findall(r"\((\d+)\)", raw)
                        ):
                            raise ParseError("PIC size limit")
                        expanded = re.sub(
                            r"(.)\((\d+)\)", lambda m: m.group(1) * int(m.group(2)), raw
                        )
                        if len(expanded) > 10000 or not expanded:
                            raise ParseError("PIC size limit")
                        tokens = re.findall(r"CR|DB|.", expanded)
                        if re.fullmatch(r"N+", expanded):
                            pic = dict(
                                raw=raw,
                                category="national",
                                digits=len(expanded),
                                scale=0,
                                signed=False,
                                tokens=tokens,
                            )
                        elif re.fullmatch(r"X+", expanded):
                            pic = dict(
                                raw=raw,
                                category="text",
                                digits=len(expanded),
                                scale=0,
                                signed=False,
                                tokens=tokens,
                            )
                        elif re.fullmatch(r"A+", expanded):
                            pic = dict(
                                raw=raw,
                                category="alphabetic",
                                digits=len(expanded),
                                scale=0,
                                signed=False,
                                tokens=tokens,
                            )
                        elif re.fullmatch(r"S?9+(V9+)?", expanded):
                            parsed = parse_pic(raw)
                            pic = dict(
                                raw=raw,
                                category="numeric",
                                digits=parsed.size,
                                scale=parsed.decimals,
                                signed=parsed.signed,
                                tokens=tokens,
                            )
                        elif "X" in expanded or "A" in expanded:
                            if not re.fullmatch(r"[AX9B0/]+", expanded):
                                raise ParseError(
                                    "Unsupported alphanumeric edited PIC " + raw
                                )
                            pic = dict(
                                raw=raw,
                                category="alphanumeric-edited",
                                digits=sum(t in "AX9" for t in tokens),
                                scale=0,
                                signed=False,
                                tokens=tokens,
                            )
                        else:
                            currency = self.f.config["currencySymbol"]
                            if any(
                                t
                                not in (
                                    "9",
                                    "Z",
                                    "*",
                                    "B",
                                    "0",
                                    "/",
                                    ".",
                                    ",",
                                    "V",
                                    "+",
                                    "-",
                                    "CR",
                                    "DB",
                                    currency,
                                )
                                for t in tokens
                            ) or not any(t in ("9", "Z", "*") for t in tokens):
                                raise ParseError("Unsupported edited PIC " + raw)
                            point = (
                                "," if self.f.config["decimalPoint"] == "comma" else "."
                            )
                            position = next(
                                (i for i, t in enumerate(tokens) if t in (point, "V")),
                                len(tokens),
                            )
                            numeric = sum(t in ("9", "Z", "*") for t in tokens)
                            floating = max(
                                (tokens.count(t) - 1 for t in ("+", "-", currency)),
                                default=0,
                            )
                            pic = dict(
                                raw=raw,
                                category="numeric-edited",
                                digits=numeric + max(0, floating),
                                scale=sum(
                                    t in ("9", "Z", "*") for t in tokens[position + 1 :]
                                ),
                                signed=any(t in ("+", "-", "CR", "DB") for t in tokens),
                                tokens=tokens,
                            )
                    elif clause in (
                        "USAGE",
                        "DISPLAY",
                        "COMP",
                        "COMP-3",
                        "COMP-1",
                        "COMP-2",
                        "COMP-5",
                        "INDEX",
                        "COMPUTATIONAL-1",
                        "COMPUTATIONAL-2",
                        "COMPUTATIONAL-5",
                        "COMPUTATIONAL",
                        "COMPUTATIONAL-3",
                        "BINARY",
                        "PACKED-DECIMAL",
                    ):
                        if clause == "USAGE":
                            self.accept("IS")
                            clause = self.take().upper
                        usage = {
                            "COMPUTATIONAL": "COMP",
                            "BINARY": "COMP",
                            "COMPUTATIONAL-1": "COMP-1",
                            "COMPUTATIONAL-2": "COMP-2",
                            "COMPUTATIONAL-5": "COMP-5",
                            "COMPUTATIONAL-3": "COMP-3",
                            "PACKED-DECIMAL": "COMP-3",
                        }.get(clause, clause)
                        if usage not in (
                            "DISPLAY",
                            "COMP",
                            "COMP-3",
                            "COMP-1",
                            "COMP-2",
                            "COMP-5",
                            "INDEX",
                        ):
                            raise ParseError("Unsupported usage " + usage)
                    elif clause == "OCCURS":
                        occurs_declared = True
                        occurs = int(self.take().text)
                        occurs_min = occurs
                        if self.accept("TO"):
                            occurs = int(self.take().text)
                            self.accept("TIMES")
                            self.require("DEPENDING")
                            self.require("ON")
                            depending = self.resolve(self.take().text)["id"]
                            if occurs_min > occurs or occurs_min < 0:
                                raise ParseError("Invalid OCCURS range")
                        else:
                            self.accept("TIMES")
                        if not 1 <= occurs <= 10000:
                            raise ParseError("OCCURS must be 1..10000")
                    elif clause == "INDEXED":
                        if not occurs_declared:
                            raise ParseError("INDEXED BY requires OCCURS")
                        self.require("BY")
                        while self.peek() and self.peek() not in set(
                            ". ASCENDING DESCENDING PIC PICTURE VALUE VALUES REDEFINES USAGE SIGN SYNC SYNCHRONIZED JUSTIFIED BLANK".split()
                        ):
                            indexes.append(self.take().text)
                        if not indexes:
                            raise ParseError("INDEXED BY requires names")
                    elif clause in ("ASCENDING", "DESCENDING"):
                        if not occurs_declared:
                            raise ParseError("Table key requires OCCURS")
                        self.require("KEY")
                        self.accept("IS")
                        while self.peek() and self.peek() not in set(
                            ". ASCENDING DESCENDING INDEXED PIC PICTURE VALUE VALUES USAGE SIGN SYNC SYNCHRONIZED JUSTIFIED BLANK".split()
                        ):
                            keys.append((self.take().text, clause.lower()))
                    elif clause == "SIGN":
                        self.accept("IS")
                        position = self.take().upper
                        if position not in ("LEADING", "TRAILING"):
                            raise ParseError("Invalid SIGN position")
                        separate = self.accept("SEPARATE")
                        if separate:
                            self.accept("CHARACTER")
                        sign_clause = dict(position=position.lower(), separate=separate)
                    elif clause in ("JUSTIFIED", "JUST"):
                        self.accept("RIGHT")
                        justified = True
                    elif clause in ("SYNCHRONIZED", "SYNC"):
                        self.accept("LEFT") or self.accept("RIGHT")
                        synchronized = True
                    elif clause == "BLANK":
                        self.require("WHEN")
                        self.require("ZERO")
                        blank_zero = True
                    elif clause == "REDEFINES":
                        redefine = self.take().text
                    elif clause in ("VALUE", "VALUES"):
                        self.accept("IS")
                        self.accept("ARE")
                        while self.peek() and self.peek() != ".":
                            value = self.expr(6)
                            high = value
                            if self.peek() in ("THRU", "THROUGH"):
                                self.take()
                                high = self.expr(6)
                            conditions.append(dict(low=value, high=high))
                            self.accept(",")
                            if level != 88:
                                break
                        initial = conditions[0]["low"] if conditions else None
                    else:
                        raise ParseError("Unsupported declaration clause " + clause)
                self.require(".")
                if usage in ("COMP-1", "COMP-2", "INDEX"):
                    if pic:
                        raise ParseError("PIC not permitted for " + usage)
                    pic = dict(
                        raw="",
                        category="numeric",
                        digits=0 if usage != "INDEX" else 18,
                        scale=0,
                        signed=True,
                    )
            except (ParseError, ValueError) as e:
                while self.peek() and self.peek() not in (".", "PROCEDURE"):
                    self.take()
                self.accept(".")
                self.diag("UNSUPPORTED_DECLARATION", str(e), self.source(a))
            if level == 88:
                parent = last
                if not parent:
                    self.diag("INVALID_88", "88 has no parent", self.source(a))
            else:
                if level in (1, 77):
                    stack = []
                while stack and stack[-1]["level"] >= level:
                    stack.pop()
                parent = stack[-1] if stack else None
            sid = f"f{len(self.symbols)+1:06d}"
            s = dict(
                id=sid,
                name=name,
                stub=False,
                qualifiedName=name
                + (" OF " + parent["qualifiedName"] if parent else ""),
                scope=scope,
                level=level,
                pic=pic,
                usage=usage,
                signClause=sign_clause,
                justified=justified,
                synchronized=synchronized,
                blankWhenZero=blank_zero,
                children=[],
                parentId=parent["id"] if parent else None,
                occurs=occurs,
                occursClause=(
                    dict(
                        min=occurs_min,
                        max=occurs,
                        dependingOn=depending,
                        indexIds=[],
                        keys=[],
                    )
                    if occurs_declared
                    else None
                ),
                occursMin=occurs_min,
                dependingOn=depending,
                redefines=None,
                initial=None if level == 88 else initial,
                conditions=conditions if level == 88 else [],
                source=self.source(a),
            )
            if redefine:
                try:
                    s["redefines"] = self.resolve(redefine)["id"]
                except ParseError as e:
                    self.diag("INVALID_REDEFINES", str(e), s["source"])
            for token_index in range(a, self.i):
                self.owners[token_index] = ("declaration", sid)
            if parent and level != 88:
                parent["children"].append(sid)
            self.symbols.append(s)
            if keys:
                pending_keys.append((s, keys))
            for index_name in indexes:
                index_id = f"f{len(self.symbols)+1:06d}"
                index = dict(
                    s,
                    id=index_id,
                    name=index_name,
                    qualifiedName=index_name,
                    level=77,
                    parentId=None,
                    pic=dict(
                        raw="", category="numeric", digits=18, scale=0, signed=True
                    ),
                    usage="INDEX",
                    occurs=1,
                    occursClause=None,
                    dependingOn=None,
                    occursMin=1,
                    indexFor=sid,
                    initial=None,
                    conditions=[],
                    children=[],
                    redefines=None,
                )
                self.symbols.append(index)
                s["occursClause"]["indexIds"].append(index_id)
            if level != 88:
                stack.append(s)
                last = s
            if scope == "FILE" and current_file and level == 1:
                dep = next(
                    (
                        d
                        for d in self.dependencies
                        if d["kind"] == "file"
                        and d["name"].upper() == current_file.upper()
                    ),
                    None,
                )
                if dep:
                    dep["recordId"] = sid

        for owner, keys in pending_keys:
            for name, direction in keys:
                candidates = [
                    item
                    for item in self.symbols
                    if item["name"].upper() == name.upper()
                ]

                def inside(item):
                    p = item["parentId"]
                    while p:
                        if p == owner["id"]:
                            return True
                        p = next(x for x in self.symbols if x["id"] == p)["parentId"]
                    return item["id"] == owner["id"]

                candidates = [item for item in candidates if inside(item)]
                if len(candidates) != 1:
                    self.diag(
                        "INVALID_TABLE_KEY",
                        "Unresolved/ambiguous table key " + name,
                        owner["source"],
                    )
                else:
                    owner["occursClause"]["keys"].append(
                        dict(symbolId=candidates[0]["id"], direction=direction)
                    )
