"""expressionsの位置付き解析。実行の意味はTS runtimeへ渡す。"""

from __future__ import annotations
import re
from .models import ParseError, Token, VERBS, ENDS, lit, binary


class ExpressionParser:
    def expr(self, min_bp=0):
        """式を演算子の優先順位で木へ変換する（Pratt解析）。

        min_bpは、今の部分式が取り込んでよい演算子の最低優先度。
        例えばA + B * Cでは*が+より強いため、右辺をB * Cとして先にまとめる。
        結果はbinary/reference/literal等のIRで、PythonやTSのコード文字列ではない。
        """
        t = self.take()
        u = t.upper
        if u in ("NOT", "-", "+"):
            operand = self.expr(7 if u != "NOT" else 3)
            left = (
                operand
                if u == "+"
                else (
                    lit(
                        operand["value"][1:]
                        if operand["value"].startswith("-")
                        else "-" + operand["value"]
                    )
                    if u == "-"
                    and operand["kind"] == "literal"
                    and operand["valueType"] == "numeric"
                    else dict(kind="unary", op=u, operand=operand)
                )
            )
        elif u == "(":
            left = self.expr()
            self.require(")")
        elif (
            u in ("NC", "N")
            and self.peek().startswith(('"', "'"))
            and t.source["file"] == self.ts[self.i].source["file"]
            and t.source["end"]["byteOffset"]
            == self.ts[self.i].source["start"]["byteOffset"]
        ):
            quoted = self.take().text
            left = lit(quoted[1:-1].replace(quoted[0] * 2, quoted[0]), "national")
        elif t.text.startswith(('"', "'")):
            left = lit(t.text[1:-1].replace(t.text[0] * 2, t.text[0]), "text")
        elif re.fullmatch(r"\d+(?:\.\d+)?", t.text):
            number = t.text
            if self.f.config["decimalPoint"] == "comma":
                if "." in number:
                    raise ParseError("Decimal literal requires comma")
                if (
                    self.peek() == ","
                    and self.peek(1).isdigit()
                    and t.source["end"]["byteOffset"]
                    == self.ts[self.i].source["start"]["byteOffset"]
                    and self.ts[self.i].source["end"]["byteOffset"]
                    == self.ts[self.i + 1].source["start"]["byteOffset"]
                ):
                    self.take()
                    number += "." + self.take().text
            left = lit(number)
        elif u in ("TRUE", "FALSE"):
            left = lit(u.lower(), "boolean")
        elif u == "FUNCTION":
            name = self.take().upper
            args = []
            if self.accept("("):
                while self.peek() and self.peek() != ")":
                    if name == "TRIM" and self.peek() in (
                        "LEADING",
                        "TRAILING",
                        "BOTH",
                    ):
                        args.append(lit(self.take().upper, "text"))
                    else:
                        args.append(self.expr())
                    self.accept(",")
                self.require(")")
            left = dict(kind="function", name=name, arguments=args)
        elif u == "ALL":
            value = self.expr(6)
            if value["kind"] != "literal":
                raise ParseError("ALL requires literal")
            left = dict(kind="allLiteral", value=value)
        elif u in (
            "HIGH-VALUE",
            "HIGH-VALUES",
            "LOW-VALUE",
            "LOW-VALUES",
            "QUOTE",
            "QUOTES",
        ):
            left = dict(
                kind="figurative",
                value=(
                    "high"
                    if u.startswith("HIGH")
                    else "low" if u.startswith("LOW") else "quote"
                ),
            )
        elif u in ("ZERO", "ZEROS", "ZEROES"):
            left = dict(kind="figurative", value="zero")
        elif u in ("SPACE", "SPACES"):
            left = dict(kind="figurative", value="space")
        else:
            subs = []
            mod = None
            quals = []
            if self.accept("("):
                first = self.expr()
                if self.accept(":"):
                    length = self.expr()
                    mod = dict(start=first, length=length)
                else:
                    subs.append(first)
                    while self.peek() != ")":
                        self.accept(",")
                        subs.append(self.expr())
                self.require(")")
            while self.peek() in ("OF", "IN"):
                self.take()
                quals.append(self.take().text)
            symbol = self.resolve(t.text, quals)
            if symbol["level"] == 88 and mod:
                raise ParseError("88 reference modification is invalid")
            left = (
                dict(kind="condition88", symbolId=symbol["id"], subscripts=subs)
                if symbol["level"] == 88
                else dict(
                    kind="reference",
                    symbolId=symbol["id"],
                    subscripts=subs,
                    modification=mod,
                )
            )
        # 値が大きい演算子ほど先にまとまる。比較の後にAND、さらにORを組み立てる。
        powers = {
            "OR": 1,
            "AND": 2,
            "=": 3,
            "<>": 3,
            "<": 3,
            ">": 3,
            "<=": 3,
            ">=": 3,
            "+": 4,
            "-": 4,
            "*": 5,
            "/": 5,
            "**": 8,
        }
        while True:
            # IS [NOT] class/signは比較とは別のbooleanノードにする。
            if min_bp <= 3:
                j = 0
                if self.peek(j) == "IS":
                    j += 1
                negated = self.peek(j) == "NOT"
                if negated:
                    j += 1
                word = self.peek(j)
                classes = {
                    "NUMERIC": "numeric",
                    "ALPHABETIC": "alphabetic",
                    "ALPHABETIC-UPPER": "upper",
                    "ALPHABETIC-LOWER": "lower",
                }
                signs = {"POSITIVE": "positive", "NEGATIVE": "negative", "ZERO": "zero"}
                if word in classes or word in signs:
                    for _ in range(j + 1):
                        self.take()
                    left = (
                        dict(
                            kind="classCondition",
                            operand=left,
                            category=classes[word],
                            negated=negated,
                        )
                        if word in classes
                        else dict(
                            kind="signCondition",
                            operand=left,
                            sign=signs[word],
                            negated=negated,
                        )
                    )
                    continue
            op = self.peek()
            bp = powers.get(op, 0)
            count = 1
            negate = False
            if min_bp <= 3 and (
                op in ("IS", "EQUAL", "GREATER", "LESS")
                or op == "NOT"
                and self.peek(1)
                in ("=", "<>", "<", ">", "<=", ">=", "EQUAL", "GREATER", "LESS")
            ):
                j = 0
                if self.peek(j) == "IS":
                    j += 1
                if self.peek(j) == "NOT":
                    negate = True
                    j += 1
                word = self.peek(j)
                j += 1
                if word in ("EQUAL", "GREATER", "LESS"):
                    op = {"EQUAL": "=", "GREATER": ">", "LESS": "<"}[word]
                    if self.peek(j) in ("TO", "THAN"):
                        j += 1
                    if (
                        word != "EQUAL"
                        and self.peek(j) == "OR"
                        and self.peek(j + 1) == "EQUAL"
                    ):
                        op += "="
                        j += 2
                        if self.peek(j) == "TO":
                            j += 1
                elif word in ("=", "<>", "<", ">", "<=", ">="):
                    op = word
                else:
                    raise ParseError("Unsupported comparison wording")
                count = j
                bp = 3
            if not bp or bp < min_bp:
                break
            for _ in range(count):
                self.take()

            def relation(node):
                if node["kind"] == "binary" and node["op"] in (
                    "=",
                    "<>",
                    "<",
                    ">",
                    "<=",
                    ">=",
                ):
                    return node["left"], node["op"]
                if node["kind"] == "binary" and node["op"] in ("AND", "OR"):
                    return relation(node["right"])
                return None

            inherited = relation(left) if op in ("AND", "OR") and u != "(" else None
            if inherited and self.peek() in ("=", "<>", "<", ">", "<=", ">="):
                comparator = self.take().upper
                right = binary(comparator, inherited[0], self.expr(4))
            else:
                parenthesized = self.peek() == "("
                right = self.expr(bp if op == "**" else bp + 1)
                if (
                    inherited
                    and not parenthesized
                    and right["kind"] == "literal"
                    and right["valueType"] != "boolean"
                ):
                    right = binary(inherited[1], inherited[0], right)
            left = binary(op, left, right)
            if negate:
                left = dict(kind="unary", op="NOT", operand=left)
        return left

    def reference(self):
        """式のうち、書込み可能な項目参照だけを受け入れる。MOVEの受け側へ定数を書けないようにする。"""
        e = self.expr(6)
        if e["kind"] != "reference":
            raise ParseError("Expected writable item reference")
        return e

    def expressions(self, stops=()):
        """次の命令・終端語・ピリオドに達するまで複数operandを読む。DISPLAY A Bのような列を扱う。"""
        result = []
        while self.peek() and self.peek() not in set(stops) | VERBS | ENDS | {"."}:
            self.accept(",")
            result.append(self.expr())
        if not result:
            raise ParseError("Expected operand")
        return result
