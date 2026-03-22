// Copyright Warren Harding 2026
namespace SymRules.TextModel {
    public class TextRule {
        public string Left { get; set; } = string.Empty;
        public string Right { get; set; } = string.Empty;
        public override string ToString() => $"{Left} -> {Right}";
    }
}

