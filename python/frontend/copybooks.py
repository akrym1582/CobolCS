"""位置を保ったCOPY/SQL INCLUDE探索、疑似テキスト置換と再帰展開。"""

import re
from .models import Token, PATTERN


def same(a, b):
    # 単語は大小文字を無視するがliteralの内容はそのまま比較する。
    return (
        a.text == b.text
        if a.text.startswith(('"', "'")) or b.text.startswith(('"', "'"))
        else a.upper == b.upper
    )


class Copybooks:
    def find_copy(self, book, path):
        book = book.replace("\\", "/")
        for directory in [path.parent, *self.copies]:
            requested = directory / book
            parent = requested.parent
            if not parent.is_dir():
                continue
            names = {
                (requested.name + suffix).upper() for suffix in ("", ".cpy", ".copy")
            }
            matches = sorted(
                p for p in parent.iterdir() if p.is_file() and p.name.upper() in names
            )
            if len(matches) > 1:
                raise ValueError("COPY_AMBIGUOUS: " + book)
            if matches:
                return matches[0]
        return None

    def replacement_pairs(self, tokens):
        pairs = []
        i = 0

        def operand():
            nonlocal i
            if i >= len(tokens):
                raise ValueError("COPY_REPLACING: missing operand")
            if tokens[i].text != "==":
                value = [tokens[i]]
                i += 1
                return value
            i += 1
            start = i
            while i < len(tokens) and tokens[i].text != "==":
                i += 1
            if i == len(tokens):
                raise ValueError("COPY_REPLACING: unclosed pseudo-text")
            value = tokens[start:i]
            i += 1
            return value

        while i < len(tokens):
            mode = "exact"
            if tokens[i].upper in ("LEADING", "TRAILING"):
                mode = tokens[i].upper.lower()
                i += 1
            old = operand()
            if not old or i >= len(tokens) or tokens[i].upper != "BY":
                raise ValueError("COPY_REPLACING: requires nonempty pattern and BY")
            i += 1
            new = operand()
            if mode != "exact":
                old_text = "".join(t.text for t in old)
                new_text = "".join(t.text for t in new)
                if not re.fullmatch(r"[\w-]+", old_text) or (
                    new_text and not re.fullmatch(r"[\w-]+", new_text)
                ):
                    raise ValueError("COPY_REPLACING: invalid partial-word replacement")
                old = [Token(old_text, old[0].source)]
                new = [Token(new_text, new[0].source)] if new else []
            pairs.append((mode, old, new))
        if not pairs:
            raise ValueError("COPY_REPLACING: missing pairs")
        return pairs

    def replace_copy(self, tokens, pairs, origin):
        output, i = [], 0
        while i < len(tokens):
            replaced = False
            for mode, old, new in pairs:
                selected = tokens[i : i + len(old)]
                if mode == "exact":
                    match = len(selected) == len(old) and all(
                        same(a, b) for a, b in zip(selected, old)
                    )
                    values = [t.text for t in new]
                    consumed = len(old)
                else:
                    word, fragment = tokens[i].text, old[0].text
                    match = bool(re.fullmatch(r"[^\W\d][\w-]*", word)) and (
                        word.upper().startswith(fragment.upper())
                        if mode == "leading"
                        else word.upper().endswith(fragment.upper())
                    )
                    replacement = new[0].text if new else ""
                    values = [
                        (
                            (replacement + word[len(fragment) :])
                            if mode == "leading"
                            else (word[: -len(fragment)] + replacement)
                        )
                    ]
                    consumed = 1
                if match:
                    for text in values:
                        source = dict(tokens[i].source)
                        last = tokens[i + consumed - 1].source
                        if source["file"] == last["file"]:
                            source["end"] = last["end"]
                            original = bytes.fromhex(
                                self.sources[source["file"]]["bytesHex"]
                            )
                            source["raw"] = original[
                                source["start"]["byteOffset"] : source["end"][
                                    "byteOffset"
                                ]
                            ].decode("utf-8")
                        source["replacementOrigins"] = [
                            *source.get("replacementOrigins", []),
                            origin,
                        ]
                        output.append(Token(text, source))
                    i += consumed
                    replaced = True
                    break
            if not replaced:
                output.append(tokens[i])
                i += 1
        return output

    def expand_copies(self, tokens, path, stack, active, name, raw):
        result, i = [], 0
        while i < len(tokens):
            t = tokens[i]
            sql_include = [x.upper for x in tokens[i : i + 3]] == [
                "EXEC",
                "SQL",
                "INCLUDE",
            ]
            if t.upper != "COPY" and not sql_include:
                result.append(t)
                i += 1
                continue
            j, pseudo = i + 1, False
            while j < len(tokens):
                if tokens[j].text == "==":
                    pseudo = not pseudo
                if (sql_include and tokens[j].upper == "END-EXEC") or (
                    not sql_include and tokens[j].text == "." and not pseudo
                ):
                    break
                j += 1
            source = dict(t.source)
            if j < len(tokens):
                source["end"] = tokens[j].source["end"]
                source["raw"] = raw[
                    source["start"]["byteOffset"] : source["end"]["byteOffset"]
                ].decode("utf-8")
            try:
                if j == len(tokens):
                    raise ValueError("COPY_TERMINATOR: unterminated include")
                book_index = i + (3 if sql_include else 1)
                if book_index >= j:
                    raise ValueError("COPY_REPLACING: missing book name")
                book = tokens[book_index].text.strip("'\"")
                rest = tokens[book_index + 1 : j]
                pairs = []
                if rest:
                    if sql_include or rest[0].upper != "REPLACING":
                        raise ValueError("COPY_REPLACING: unexpected clause")
                    pairs = self.replacement_pairs(rest[1:])
                candidate = self.find_copy(book, path)
                if (
                    candidate is None
                    and sql_include
                    and book.upper() == "SQLCA"
                    and self.config["sqlcaInclude"] == "portable-minimal"
                ):
                    builtin = {
                        **source,
                        "origin": "portable-minimal-sqlca",
                        "copyStack": [
                            *source["copyStack"],
                            "builtin:portable-minimal-sqlca",
                        ],
                    }
                    self.diagnostic(
                        "PORTABLE_SQLCA",
                        "Explicit portable SQLCODE/SQLSTATE declaration only; not full DB2 SQLCA",
                        builtin,
                        "review",
                    )
                    declaration = '01 SQLCODE PIC S9(9) VALUE 0. 01 SQLSTATE PIC X(5) VALUE "00000".'
                    result.extend(
                        Token(m.group(), builtin) for m in PATTERN.finditer(declaration)
                    )
                elif candidate is None:
                    self.missing_copies.append(book)
                    self.diagnostic(
                        (
                            "COPY_MISSING_STUB"
                            if self.allow_missing_copy
                            else "COPY_MISSING"
                        ),
                        book + " (missing COPY; no declarations or behavior inferred)",
                        source,
                        "review" if self.allow_missing_copy else "error",
                    )
                    if self.allow_missing_copy:
                        result.extend(
                            [
                                Token("COPY-STUB", source),
                                Token(book, tokens[book_index].source),
                                Token(".", tokens[j].source),
                            ]
                        )
                else:
                    expanded = self.load(
                        candidate,
                        (*stack, f'{name}:{source["start"]["line"]}'),
                        (*active, path),
                    )
                    result.extend(
                        self.replace_copy(expanded, pairs, source)
                        if pairs
                        else expanded
                    )
            except (ValueError, OSError, UnicodeError) as error:
                message = str(error)
                code = (
                    message.split(":", 1)[0]
                    if message.startswith(
                        ("COPY_REPLACING:", "COPY_AMBIGUOUS:", "COPY_TERMINATOR:")
                    )
                    else "COPY_ERROR"
                )
                self.diagnostic(code, message, source)
            i = j + 1
        return result
