"""Explicit P64 selection, or bounded opaque conditional regions."""
from .models import Token


class Conditionals:
    def conditional_tokens(self, tokens):
        index = 0
        directive_indices = set()

        def directive():
            if index >= len(tokens) or tokens[index].text != "$" or tokens[index].source.get("documentary"):
                return None
            if index and tokens[index - 1].source["start"]["line"] == tokens[index].source["start"]["line"]:
                return None
            return tokens[index + 1].upper if index + 1 < len(tokens) else ""

        def line():
            nonlocal index
            start = index
            location = tokens[index].source
            while index < len(tokens) and tokens[index].source["start"]["line"] == location["start"]["line"]:
                index += 1
            directive_indices.update(range(start, index))
            return [t.upper for t in tokens[start:index]]

        def region(nested=False):
            nonlocal index
            result = []
            while index < len(tokens):
                word = directive()
                if word in ("ELSE", "END"):
                    if not nested:
                        raise ValueError("CONDITIONAL_SYNTAX: unmatched $" + word)
                    break
                if word is None:
                    result.append(tokens[index])
                    index += 1
                    continue
                start = index
                if line() != ["$", "IF", "P64", "SET"]:
                    raise ValueError("CONDITIONAL_SYNTAX: expected $IF P64 SET")
                yes = region(True)
                no = []
                if directive() == "ELSE":
                    if line() != ["$", "ELSE"]:
                        raise ValueError("CONDITIONAL_SYNTAX: malformed $ELSE")
                    no = region(True)
                if directive() != "END" or line() != ["$", "END"]:
                    raise ValueError("CONDITIONAL_SYNTAX: missing $END or duplicate $ELSE")
                first, last = tokens[start].source, tokens[index - 1].source
                raw = self.sources[first["file"]]["text"].encode("utf-8")
                source = dict(first, end=last["end"], raw=raw[first["start"]["byteOffset"]:last["end"]["byteOffset"]].decode("utf-8"))
                value = self.config["p64"]
                info = dict(condition="P64", value=value, source=source,
                            branches=[[dict(text=t.text, source=t.source) for t in branch] for branch in (yes, no)],
                            tokens=[dict(text=t.text, source=t.source, ownerKind="conditional", reason="P64 selection or opaque region") for t in tokens[start:index]])
                self.conditionals.append(info)
                if value is None:
                    result.append(Token("CONDITIONAL-STUB", dict(source, conditional=info)))
                else:
                    selected, excluded = (yes, no) if value else (no, yes)
                    result.extend(selected)
                    for token in excluded:
                        self.excluded.append(dict(reason="conditional-P64-branch-not-selected", source=token.source))
            return result

        try:
            result = region()
            if self.config["p64"] is not None:
                self.excluded.extend(dict(reason="conditional-directive-evaluated", source=tokens[i].source) for i in sorted(directive_indices))
            return result
        except ValueError as error:
            source = tokens[min(index, len(tokens) - 1)].source
            self.diagnostic("CONDITIONAL_SYNTAX", str(error), source)
            self.rejected.extend(dict(text=t.text, source=t.source, ownerKind="unknown", ownerId="$conditionalRejected") for t in tokens)
            return [Token("CONDITIONAL-ERROR", dict(source, error=str(error)))]
