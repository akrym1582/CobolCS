"""SQLのhost/indicator組、カーソル、静的WHENEVERを原文付き契約へ保存する。"""

from .models import ParseError


class SqlParser:
    def sql_statement(self):
        a = self.i - 1
        self.require("SQL")
        original = []
        while self.peek() and self.peek() != "END-EXEC":
            original.append(self.take())
        self.require("END-EXEC")
        # SQLコメント中のhost markerを入力として拾わない。原文はsourceへ残す。
        ts = []
        j = 0
        while j < len(original):
            if (
                j + 1 < len(original)
                and original[j].text == "-"
                and original[j + 1].text == "-"
            ):
                line = original[j].source["start"]["line"]
                while j < len(original) and original[j].source["start"]["line"] == line:
                    j += 1
            elif (
                j + 1 < len(original)
                and original[j].text == "/"
                and original[j + 1].text == "*"
            ):
                j += 2
                while j + 1 < len(original) and not (
                    original[j].text == "*" and original[j + 1].text == "/"
                ):
                    j += 1
                if j + 1 >= len(original):
                    raise ParseError("Unterminated SQL comment")
                j += 2
            else:
                ts.append(original[j])
                j += 1
        words = [t.upper for t in ts]
        if not words:
            raise ParseError("Empty SQL")
        handlers = getattr(
            self, "sql_handlers", dict(error=None, warning=None, notFound=None)
        )
        if words[0] == "WHENEVER":
            j = 1
            if words[j] == "NOT" and words[j + 1] == "FOUND":
                key = "notFound"
                j += 2
            elif words[j] in ("SQLERROR", "SQLWARNING"):
                key = "error" if words[j] == "SQLERROR" else "warning"
                j += 1
            else:
                raise ParseError("Invalid WHENEVER condition")
            if words[j:] == ["CONTINUE"]:
                target = None
            elif words[j : j + 2] == ["GO", "TO"] and len(words) == j + 3:
                target = ts[j + 2].text
            else:
                raise ParseError("Unsupported WHENEVER action")
            self.sql_handlers = dict(handlers, **{key: target})
            return dict(kind="continue")
        kind = words[0].lower()
        if kind not in (
            "select",
            "insert",
            "update",
            "delete",
            "commit",
            "rollback",
            "declare",
            "open",
            "fetch",
            "close",
        ):
            raise ParseError("Unsupported SQL " + words[0])
        if any(
            w in words
            for w in (
                "WITH",
                "HOLD",
                "SCROLL",
                "PREPARE",
                "EXECUTE",
                "FORWARD",
                "ROWSET",
            )
        ):
            raise ParseError("Unsupported SQL option")
        cursor = None
        start = 0
        if kind == "declare":
            if (
                len(words) < 5
                or words[2:4] != ["CURSOR", "FOR"]
                or words[4] != "SELECT"
            ):
                raise ParseError("Invalid DECLARE cursor")
            cursor = ts[1].text
            start = 4
        elif kind in ("open", "close"):
            if len(words) != 2:
                raise ParseError("Unsupported cursor option")
            cursor = ts[1].text
        elif kind == "fetch":
            if len(words) < 4 or words[2] != "INTO":
                raise ParseError("FETCH requires single row INTO")
            cursor = ts[1].text
        inputs = []
        outputs = []
        bindings = []
        query = []
        output = False
        j = start

        def host(index):
            if index >= len(ts):
                raise ParseError("Missing SQL host")
            name = ts[index].text
            index += 1
            qualifiers = []
            while index + 1 < len(ts) and ts[index].upper in ("OF", "IN"):
                qualifiers.append(ts[index + 1].text)
                index += 2
            symbol = self.resolve(name, qualifiers)
            return (
                dict(
                    kind="reference",
                    symbolId=symbol["id"],
                    subscripts=[],
                    modification=None,
                ),
                index,
            )

        while j < len(ts):
            t = ts[j]
            if t.upper == "INTO" and kind in ("select", "fetch"):
                output = True
                j += 1
                continue
            if t.upper == "FROM":
                output = False
            if t.text == ":":
                expression, j = host(j + 1)
                indicator = None
                if j < len(ts) and ts[j].upper == "INDICATOR":
                    j += 1
                    if j >= len(ts) or ts[j].text != ":":
                        raise ParseError("INDICATOR requires host marker")
                    indicator, j = host(j + 1)
                elif j < len(ts) and ts[j].text == ":":
                    indicator, j = host(j + 1)
                direction = "output" if output else "input"
                bindings.append(
                    dict(host=expression, indicator=indicator, direction=direction)
                )
                (outputs if output else inputs).append(expression)
                if not output:
                    query.append("?")
                continue
            if not output:
                query.append(t.text)
            elif t.text != ",":
                raise ParseError("SQL INTO requires only host/indicator operands")
            j += 1

        def reference(name, optional=False):
            if optional and not any(s["name"].upper() == name for s in self.symbols):
                return None
            s = self.resolve(name)
            return dict(
                kind="reference", symbolId=s["id"], subscripts=[], modification=None
            )

        d = self.dependency(
            "sql",
            words[0],
            " ".join(t.text for t in original),
            self.source(a),
            inputs=inputs,
            outputs=outputs,
            status=reference("SQLCODE"),
        )
        d["sql"] = dict(
            kind=kind,
            cursor=cursor,
            query=" ".join(query),
            bindings=bindings,
            sqlstate=reference("SQLSTATE", True),
            whenever=dict(handlers),
        )
        return dict(
            kind="external", dependencyId=d["id"], operation=kind, atEnd=[], notEnd=[]
        )
