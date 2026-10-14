using System.Globalization;

namespace KuTax.Core.Claims;

/// <summary>
/// A small, side-effect-free formula language for rule packs, evaluated with decimal arithmetic.
/// Grammar: numbers, names (inputs or rule parameters), + - * /, comparisons (&lt; &lt;= &gt; &gt;= == !=),
/// and/or/not, parentheses, and the functions round2(x), min(a,b,…), max(a,b,…) and if(condition,a,b).
/// round2 rounds to cents with midpoint away from zero, matching the ATO-style calculation used everywhere else.
/// Formulas can't call code, read data or loop; size and depth are capped.
/// </summary>
public sealed class Formula
{
    public const int MaxLength = 500, MaxNodes = 120, MaxDepth = 24;
    private readonly Node root;
    public string Text { get; }
    public bool IsCondition { get; }
    /// <summary>Every name the formula reads.</summary>
    public IReadOnlySet<string> Names { get; }

    private Formula(string text, Node root, HashSet<string> names) { Text = text; this.root = root; Names = names; IsCondition = root.IsBool; }

    /// <summary>Parses and type-checks a formula. Throws <see cref="FormatException"/> with a readable reason.</summary>
    public static Formula Parse(string text, IReadOnlySet<string> knownNames)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) throw new FormatException($"A formula must be 1–{MaxLength} characters.");
        var parser = new Parser(text, knownNames);
        var node = parser.ParseAll();
        return new Formula(text.Trim(), node, parser.Names);
    }

    public decimal Number(IReadOnlyDictionary<string, decimal> values)
    {
        if (root.IsBool) throw new InvalidOperationException("This formula is a condition, not an amount.");
        return root.Number(values);
    }

    public bool Condition(IReadOnlyDictionary<string, decimal> values)
    {
        if (!root.IsBool) throw new InvalidOperationException("This formula is an amount, not a condition.");
        return root.Bool(values);
    }

    private abstract class Node
    {
        public abstract bool IsBool { get; }
        public virtual decimal Number(IReadOnlyDictionary<string, decimal> v) => throw new InvalidOperationException();
        public virtual bool Bool(IReadOnlyDictionary<string, decimal> v) => throw new InvalidOperationException();
    }
    private sealed class Constant(decimal value) : Node { public override bool IsBool => false; public override decimal Number(IReadOnlyDictionary<string, decimal> v) => value; }
    private sealed class Name(string name) : Node
    {
        public override bool IsBool => false;
        public override decimal Number(IReadOnlyDictionary<string, decimal> v) => v.TryGetValue(name, out var value) ? value : throw new KeyNotFoundException($"No value for '{name}'.");
    }
    private sealed class Arithmetic(char op, Node left, Node right) : Node
    {
        public override bool IsBool => false;
        public override decimal Number(IReadOnlyDictionary<string, decimal> v)
        {
            var a = left.Number(v); var b = right.Number(v);
            return op switch { '+' => a + b, '-' => a - b, '*' => a * b, '/' => b == 0 ? throw new DivideByZeroException("Division by zero in a rule formula.") : a / b, _ => throw new InvalidOperationException() };
        }
    }
    private sealed class Negate(Node inner) : Node { public override bool IsBool => false; public override decimal Number(IReadOnlyDictionary<string, decimal> v) => -inner.Number(v); }
    private sealed class Compare(string op, Node left, Node right) : Node
    {
        public override bool IsBool => true;
        public override bool Bool(IReadOnlyDictionary<string, decimal> v)
        {
            var a = left.Number(v); var b = right.Number(v);
            return op switch { "<" => a < b, "<=" => a <= b, ">" => a > b, ">=" => a >= b, "==" => a == b, "!=" => a != b, _ => throw new InvalidOperationException() };
        }
    }
    private sealed class Logic(string op, Node left, Node right) : Node
    {
        public override bool IsBool => true;
        public override bool Bool(IReadOnlyDictionary<string, decimal> v) => op == "and" ? left.Bool(v) && right.Bool(v) : left.Bool(v) || right.Bool(v);
    }
    private sealed class Not(Node inner) : Node { public override bool IsBool => true; public override bool Bool(IReadOnlyDictionary<string, decimal> v) => !inner.Bool(v); }
    private sealed class Function(string name, Node[] args) : Node
    {
        public override bool IsBool => false;
        public override decimal Number(IReadOnlyDictionary<string, decimal> v) => name switch
        {
            "round2" => decimal.Round(args[0].Number(v), 2, MidpointRounding.AwayFromZero),
            "min" => args.Min(a => a.Number(v)),
            "max" => args.Max(a => a.Number(v)),
            "if" => args[0].Bool(v) ? args[1].Number(v) : args[2].Number(v),
            _ => throw new InvalidOperationException(),
        };
    }

    private sealed class Parser(string text, IReadOnlySet<string> known)
    {
        private int position, nodes;
        public HashSet<string> Names { get; } = [];

        public Node ParseAll()
        {
            var node = Or(0);
            Skip();
            if (position < text.Length) throw Error($"Unexpected '{text[position]}'");
            return node;
        }

        private FormatException Error(string message) => new($"{message} at position {position + 1} in formula \"{text}\".");
        private Node Count(Node node) { if (++nodes > MaxNodes) throw Error("Formula is too long"); return node; }
        private void Skip() { while (position < text.Length && char.IsWhiteSpace(text[position])) position++; }
        private bool Accept(string token)
        {
            Skip();
            if (string.CompareOrdinal(text, position, token, 0, token.Length) != 0) return false;
            // Words must end at a word boundary ("and" is not the start of "android").
            if (char.IsLetter(token[0]) && position + token.Length < text.Length && (char.IsLetterOrDigit(text[position + token.Length]) || text[position + token.Length] == '_')) return false;
            position += token.Length;
            return true;
        }
        private void Expect(string token) { if (!Accept(token)) throw Error($"Expected '{token}'"); }
        private static void Need(Node node, bool condition, string what) { if (node.IsBool != condition) throw new FormatException($"Expected {(condition ? "a condition" : "an amount")} {what}."); }
        private void Depth(int depth) { if (depth > MaxDepth) throw Error("Formula is nested too deeply"); }

        private Node Or(int depth)
        {
            Depth(depth);
            var left = And(depth + 1);
            while (Accept("or")) { var right = And(depth + 1); Need(left, true, "before 'or'"); Need(right, true, "after 'or'"); left = Count(new Logic("or", left, right)); }
            return left;
        }
        private Node And(int depth)
        {
            var left = NotExpression(depth + 1);
            while (Accept("and")) { var right = NotExpression(depth + 1); Need(left, true, "before 'and'"); Need(right, true, "after 'and'"); left = Count(new Logic("and", left, right)); }
            return left;
        }
        private Node NotExpression(int depth)
        {
            Depth(depth);
            if (Accept("not")) { var inner = NotExpression(depth + 1); Need(inner, true, "after 'not'"); return Count(new Not(inner)); }
            return Comparison(depth + 1);
        }
        private Node Comparison(int depth)
        {
            var left = Sum(depth + 1);
            foreach (var op in new[] { "<=", ">=", "==", "!=", "<", ">" })
                if (Accept(op)) { var right = Sum(depth + 1); Need(left, false, $"before '{op}'"); Need(right, false, $"after '{op}'"); return Count(new Compare(op, left, right)); }
            return left;
        }
        private Node Sum(int depth)
        {
            Depth(depth);
            var left = Product(depth + 1);
            while (true)
            {
                char op;
                if (Accept("+")) op = '+'; else if (Accept("-")) op = '-'; else return left;
                var right = Product(depth + 1); Need(left, false, $"before '{op}'"); Need(right, false, $"after '{op}'");
                left = Count(new Arithmetic(op, left, right));
            }
        }
        private Node Product(int depth)
        {
            var left = Unary(depth + 1);
            while (true)
            {
                char op;
                if (Accept("*")) op = '*'; else if (Accept("/")) op = '/'; else return left;
                var right = Unary(depth + 1); Need(left, false, $"before '{op}'"); Need(right, false, $"after '{op}'");
                left = Count(new Arithmetic(op, left, right));
            }
        }
        private Node Unary(int depth)
        {
            Depth(depth);
            if (Accept("-")) { var inner = Unary(depth + 1); Need(inner, false, "after '-'"); return Count(new Negate(inner)); }
            return Primary(depth + 1);
        }
        private Node Primary(int depth)
        {
            Skip();
            if (Accept("(")) { var inner = Or(depth + 1); Expect(")"); return inner; }
            var start = position;
            if (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
            {
                while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.')) position++;
                if (!decimal.TryParse(text.AsSpan(start, position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) throw Error("Invalid number");
                return Count(new Constant(value));
            }
            if (position < text.Length && char.IsLetter(text[position]))
            {
                while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
                var word = text[start..position];
                if (word is "round2" or "min" or "max" or "if")
                {
                    Expect("(");
                    var args = new List<Node> { Or(depth + 1) };
                    while (Accept(",")) args.Add(Or(depth + 1));
                    Expect(")");
                    switch (word)
                    {
                        case "round2" when args.Count == 1: Need(args[0], false, "in round2"); break;
                        case "min" or "max" when args.Count >= 2: foreach (var a in args) Need(a, false, $"in {word}"); break;
                        case "if" when args.Count == 3: Need(args[0], true, "as the first part of if"); Need(args[1], false, "in if"); Need(args[2], false, "in if"); break;
                        default: throw Error($"Wrong number of values for {word}");
                    }
                    return Count(new Function(word, [.. args]));
                }
                if (word is "and" or "or" or "not") throw Error($"Unexpected '{word}'");
                if (!known.Contains(word)) throw Error($"Unknown name '{word}'");
                Names.Add(word);
                return Count(new Name(word));
            }
            throw Error(position < text.Length ? $"Unexpected '{text[position]}'" : "Formula ended early");
        }
    }
}
