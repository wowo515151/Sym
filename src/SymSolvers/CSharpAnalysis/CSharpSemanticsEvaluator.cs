using System;
using System.Collections.Generic;
using System.Globalization;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using Sym.Core.EGraph;

namespace SymSolvers.CSharpAnalysis
{
    public static class CSharpSemanticsEvaluator
    {
        public static CSharpMathBugConfidence EvaluateConfidence(EGraph egraph, ENode bugNode)
        {
            string head = bugNode.Head;
            if (head.StartsWith("Func:")) head = head.Substring(5);

            if (head == "cs_bug_div_by_zero_i32" || head == "cs_bug_mod_by_zero_i32")
                return CSharpMathBugConfidence.Confirmed;
            
            if (head == "cs_bug_log1p_likely") return CSharpMathBugConfidence.High;
            if (head == "cs_bug_log1p") return CSharpMathBugConfidence.Medium;

            if (head == "cs_bug_int_div_truncation_confirmed") return CSharpMathBugConfidence.Confirmed;
            if (head == "cs_bug_int_div_truncation") return CSharpMathBugConfidence.High;

            if (head == "cs_bug_xor_as_pow_high") return CSharpMathBugConfidence.High;
            if (head == "cs_bug_xor_as_pow_medium") return CSharpMathBugConfidence.Medium;
            if (head == "cs_bug_xor_as_pow") return CSharpMathBugConfidence.Low;

            if (head == "cs_bug_integer_division_intentional") return CSharpMathBugConfidence.Low;
            if (head == "cs_bug_integer_division") return CSharpMathBugConfidence.High;

            if (CSharpBugCatalog.IsAlwaysConfirmedBugNode(head))
            {
                return CSharpMathBugConfidence.Confirmed;
            }

            // Console output is highly context dependent; treat it as low-confidence by default
            // so it doesn't dominate large-repo reports.
            if (head == "cs_bug_console_output")
            {
                return CSharpMathBugConfidence.Low;
            }

            if (CSharpBugCatalog.IsDefaultHighConfidenceBugNode(head))
            {
                return CSharpMathBugConfidence.High;
            }

            if (head == "cs_bug_generic_div_truncation" ||
                head == "cs_bug_generic_underflow")
            {
                // Generic arithmetic over type parameters is frequently intentional; keep signal available but low-confidence.
                return CSharpMathBugConfidence.Low;
            }

            if (head == "cs_bug_potential_variable_division")
            {
                return CSharpMathBugConfidence.Low;
            }

            if (head == "cs_bug_recursion")
            {
                // Recursion is often intentional (divide-and-conquer/tree traversal).
                // Keep this available for low-threshold audits but suppress by default.
                return CSharpMathBugConfidence.Low;
            }

            return CSharpMathBugConfidence.Low;
        }
    }
}
