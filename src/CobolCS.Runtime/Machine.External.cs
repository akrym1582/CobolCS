using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>固定応答による CALL、FILE、SQL の論理交換を実行します。</summary>
public sealed partial class Machine
{
    private readonly Dictionary<string, JsonNode> dependencies;
    private readonly FixedExternalResponses external;
    private string externalOutcome = "success";

    private static JsonNode Literal(int value) => new JsonObject { ["kind"] = "literal", ["valueType"] = "numeric", ["value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private void ExecuteExternal(JsonNode statement)
    {
        if (config.S("externalAdapter") != "fixed")
        {
            throw new NotSupportedException("External adapter is not configured at " + statement.S("id"));
        }

        var dependency = dependencies[statement.S("dependencyId")];
        var kind = dependency.S("kind");
        var arguments = new JsonArray();
        var references = new List<JsonNode?>();
        var modes = new List<string>();
        void Argument(JsonNode expression, string mode)
        {
            var reference = expression.S("kind") == "reference" ? Freeze(expression) : null;
            var value = reference != null ? storage.ReadLogical(reference, Eval) : JsonValue.Create(Text(Eval(expression)))!;
            references.Add(reference);
            modes.Add(mode);
            arguments.Add(new JsonObject { ["mode"] = mode, ["value"] = value });
        }

        if (kind == "call")
        {
            var inputs = dependency.A("inputs").ToArray();
            var passing = dependency.A("modes").Select(m => m.GetValue<string>()).ToArray();
            for (var i = 0; i < inputs.Length; i++)
            {
                Argument(inputs[i], passing[i]);
            }
        }
        else if (kind == "sql")
        {
            foreach (var binding in dependency["sql"]!.A("bindings"))
            {
                if (binding.S("direction") == "input")
                {
                    Argument(binding["host"]!, "input");
                    if (binding["indicator"] != null)
                    {
                        Argument(binding["indicator"]!, "indicator");
                    }
                }
            }
        }
        else if (kind == "file")
        {
            if (statement.S("operation") is "write" or "rewrite")
            {
                var recordReference = new JsonObject { ["kind"] = "reference", ["symbolId"] = dependency.S("recordId") };
                if (statement["transfer"] is JsonNode transfer)
                {
                    var value = transfer.S("kind") == "reference" ? storage.ReadLogical(Freeze(transfer), Eval) : JsonValue.Create(Text(Eval(transfer)))!;
                    if (storage.Symbols[dependency.S("recordId")]["pic"] == null)
                    {
                        throw new NotSupportedException("FILE FROM group transfer requires a physical record layout");
                    }

                    storage.Set(recordReference, value.GetValue<string>(), Eval);
                }

                Argument(recordReference, "record");
            }

            if (statement["key"] != null)
            {
                Argument(statement["key"]!, statement["startCondition"] == null ? "key" : "key " + statement.S("startCondition"));
            }

            if (statement["advancing"] is JsonNode advancing)
            {
                Argument(advancing["lines"]!, advancing.B("before") ? "advance-before" : "advance-after");
            }
        }
        else
        {
            throw new NotSupportedException("Unsupported external kind: " + kind);
        }

        var target = kind == "call" ? Text(Eval(dependency["call"]!["target"]!)) : dependency.S("name");
        var request = new JsonObject { ["statementId"] = statement.S("id"), ["kind"] = kind, ["target"] = target, ["operation"] = statement.S("operation"), ["arguments"] = arguments, ["query"] = kind == "sql" ? dependency["sql"]!.S("query") : string.Empty };
        var response = external.Invoke(request);
        externalOutcome = response.S("outcome");
        var actions = new List<Action>();
        void Set(JsonNode reference, JsonNode value) => actions.Add(storage.PrepareLogical(Freeze(reference), value, Eval));
        void Status(JsonNode? reference, string value)
        {
            if (reference != null)
            {
                Set(reference, JsonValue.Create(value)!);
            }
        }

        var success = externalOutcome == "success";
        if (kind == "call")
        {
            FixedExternalResponses.RequireKeys(response, "outcome status arguments returning bytes");
            if (externalOutcome is not ("success" or "error") || response.S("status") != (success ? "0" : "1"))
            {
                throw new InvalidDataException("Invalid CALL outcome/status");
            }

            var updates = response["arguments"]?.AsArray() ?? throw new InvalidDataException("Missing CALL response arguments");
            if (updates.Count != arguments.Count)
            {
                throw new InvalidDataException("CALL response argument count mismatch");
            }

            for (var i = 0; i < updates.Count; i++)
            {
                if (success && modes[i] == "reference" && references[i] != null)
                {
                    actions.Add(storage.PrepareLogical(references[i]!, updates[i]!, Eval));
                }
                else if (!JsonNode.DeepEquals(updates[i], arguments[i]!["value"]))
                {
                    throw new InvalidDataException("BY CONTENT/VALUE or failed CALL cannot update arguments");
                }
            }

            if (success && dependency["call"]!["returning"] is JsonNode returning)
            {
                Set(returning, response["returning"] ?? throw new InvalidDataException("Missing CALL RETURNING"));
            }
            else if (response["returning"] != null)
            {
                throw new InvalidDataException("Unexpected CALL RETURNING");
            }

            if (!success && !statement.A("atEnd").Any())
            {
                throw new InvalidOperationException("Unhandled CALL exception at " + statement.S("id"));
            }
        }
        else if (kind == "file")
        {
            FixedExternalResponses.RequireKeys(response, "outcome status record bytes");
            var status = response.S("status");
            if (status.Length != 2 || !status.All(char.IsAsciiDigit) || (success && status != "00") || (externalOutcome == "eof" && status != "10") || (!success && externalOutcome != "eof" && status is "00" or "10"))
            {
                throw new InvalidDataException("Inconsistent FILE STATUS/outcome");
            }

            if (externalOutcome == "no-data" || (externalOutcome == "eof" && !statement.S("operation").StartsWith("read", StringComparison.Ordinal)))
            {
                throw new InvalidDataException("Unexpected file outcome");
            }

            if (success && statement.S("operation").StartsWith("read", StringComparison.Ordinal))
            {
                var record = response["record"] ?? throw new InvalidDataException("Missing READ record");
                Set(new JsonObject { ["symbolId"] = dependency.S("recordId") }, record);
                if (statement["transfer"] != null)
                {
                    Set(statement["transfer"]!, record);
                }
            }
            else if (response["record"] != null)
            {
                throw new InvalidDataException("Unexpected record response");
            }

            Status(dependency["status"], status);
            if (!success && externalOutcome != "eof" && externalOutcome != "invalid" && dependency["status"] == null)
            {
                throw new InvalidOperationException("Unhandled file failure");
            }
        }
        else
        {
            FixedExternalResponses.RequireKeys(response, "outcome status outputs sqlstate bytes");
            var status = Number.Parse(response.S("status"));
            if (status.Denominator != System.Numerics.BigInteger.One || externalOutcome is not ("success" or "no-data" or "error") || (success && status != new Number(0)) || (externalOutcome == "no-data" && status != new Number(100)) || (externalOutcome == "error" && status.CompareTo(new Number(0)) >= 0))
            {
                throw new InvalidDataException("Inconsistent SQLCODE/outcome");
            }

            var outputBindings = dependency["sql"]!.A("bindings").Where(b => b.S("direction") == "output").ToArray();
            var outputs = outputBindings.SelectMany(b => b["indicator"] == null ? new[] { b["host"]! } : new[] { b["host"]!, b["indicator"]! }).ToArray();
            var values = response["outputs"]?.AsArray() ?? throw new InvalidDataException("Missing SQL output list");
            if (values.Count != (success ? outputs.Length : 0))
            {
                throw new InvalidDataException("SQL output count mismatch");
            }

            var valueIndex = 0;
            if (success)
            {
                foreach (var binding in outputBindings)
                {
                    if (binding["indicator"] != null)
                    {
                        var indicator = Number.Parse(values[valueIndex + 1]!.GetValue<string>());
                        if (indicator.Denominator != System.Numerics.BigInteger.One)
                        {
                            throw new InvalidDataException("SQL indicator must be an integer");
                        }

                        if (indicator.Numerator.Sign < 0)
                        {
                            throw new NotSupportedException("SQL NULL output host value is undefined; logical substitution is refused");
                        }
                    }

                    valueIndex += binding["indicator"] == null ? 1 : 2;
                }
            }

            for (var i = 0; i < values.Count; i++)
            {
                Set(outputs[i], values[i]!);
            }

            Status(dependency["status"], response.S("status"));
            if (dependency["sql"]!["sqlstate"] != null)
            {
                Status(dependency["sql"]!["sqlstate"], response["sqlstate"]?.GetValue<string>() ?? throw new InvalidDataException("Missing SQLSTATE"));
            }
        }

        actions.ForEach(a => a());
    }

    private JsonNode Freeze(JsonNode reference)
    {
        var fixedReference = reference.DeepClone();
        if (reference["subscripts"] is JsonArray subscripts)
        {
            fixedReference["subscripts"] = new JsonArray(subscripts.Select(s => (JsonNode?)Literal(Integer(Eval(s!)))).ToArray());
        }

        if (reference["modification"] is JsonNode modification)
        {
            fixedReference["modification"] = new JsonObject { ["start"] = Literal(Integer(Eval(modification["start"]!))), ["length"] = Literal(Integer(Eval(modification["length"]!))) };
        }

        return fixedReference;
    }
}
