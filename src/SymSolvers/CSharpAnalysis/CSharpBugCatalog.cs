// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;

namespace SymSolvers.CSharpAnalysis
{
    public static class CSharpBugCatalog
    {
        // Note: Bug node heads (e.g. "cs_bug_path_traversal") are produced by the rule library
        // and later mapped to stable IDs (e.g. "CSSEC004") for reporting.
        // Keep the mapping here so confidence + reporting cannot drift across files.
        internal static readonly Dictionary<string, string> BugNodeToId = new(StringComparer.Ordinal)
        {
            ["cs_bug_div_by_zero_i32"] = "CSMATH001",
            ["cs_bug_mod_by_zero_i32"] = "CSMATH002",
            ["cs_bug_integer_division"] = "CSMATH007",
            ["cs_bug_integer_division_intentional"] = "CSMATH007",
            ["cs_bug_xor_as_pow"] = "CSMATH009",
            ["cs_bug_xor_as_pow_medium"] = "CSMATH009",
            ["cs_bug_xor_as_pow_high"] = "CSMATH009",
            ["cs_bug_log1p"] = "CSMATH006",
            ["cs_bug_log1p_likely"] = "CSMATH006",
            ["cs_bug_int_div_truncation"] = "CSMATH005",
            ["cs_bug_int_div_truncation_confirmed"] = "CSMATH005",
            ["cs_bug_generic_div_truncation"] = "CSMATH010",
            ["cs_bug_generic_underflow"] = "CSMATH011",
            ["cs_bug_recursion"] = "CSMATH012",
            ["cs_bug_allocation_overflow"] = "CSMATH014",
            ["cs_bug_index_overflow"] = "CSMATH015",
            ["cs_bug_accumulator_overflow"] = "CSMATH016",
            ["cs_bug_abs_overflow"] = "CSMATH017",
            ["cs_bug_index_calculation_overflow"] = "CSMATH018",
            ["cs_bug_weak_rng"] = "CSSEC001",
            ["cs_bug_binary_formatter"] = "CSSEC002",
            ["cs_bug_command_injection"] = "CSSEC003",
            ["cs_bug_path_traversal"] = "CSSEC004",
            ["cs_bug_console_output"] = "CSSEC005",
            ["cs_bug_weak_hash"] = "CSSEC006",
            ["cs_bug_unsafe_code"] = "CSSEC007",
            ["cs_bug_float_equality"] = "CSMATH019",
            // Legacy alias: keep mapping in case older rules emit it.
            ["cs_bug_variable_division"] = "CSMATH023",
            ["cs_bug_potential_variable_division"] = "CSMATH023",
            ["cs_bug_sqrt_negative"] = "CSMATH024",
            ["cs_bug_insecure_comparison"] = "CSSEC008",
            ["cs_bug_static_mutable_state"] = "CSSEC009",
            ["cs_bug_negative_index_modulo"] = "CSSEC011",
            ["cs_bug_fp_infinity"] = "CSSEC012",
            ["cs_bug_insecure_random_seed"] = "CSSEC014",
            ["cs_bug_generic_addition_overflow"] = "CSMATH025",
            ["cs_bug_log_negative"] = "CSMATH029",
            ["cs_bug_asin_out_of_range"] = "CSMATH030",
            ["cs_bug_sensitive_precision_loss"] = "CSSEC018",
            ["cs_bug_expiration_overflow"] = "CSSEC020",
            ["cs_bug_unsigned_underflow"] = "CSSEC021",
            ["cs_bug_offset_overflow"] = "CSSEC023",
            ["cs_bug_signed_to_unsigned_wrap"] = "CSSEC024",
            ["cs_bug_narrowing_boundary_truncation"] = "CSSEC025",
            ["cs_bug_bounds_arithmetic_overflow"] = "CSSEC026",
            ["cs_bug_unsigned_cast_guard_bypass"] = "CSSEC027",
            ["cs_bug_floating_boundary_narrowing"] = "CSSEC028",
            ["cs_bug_shift_mask_allocation"] = "CSSEC029",
            ["cs_bug_negative_index_modulo_api"] = "CSSEC030",
        };

        internal static readonly HashSet<string> AlwaysConfirmedBugNodes = new(StringComparer.Ordinal)
        {
            // Deterministic bug markers: rules already enforce the precondition.
            "cs_bug_div_by_zero_i32",
            "cs_bug_mod_by_zero_i32",
            "cs_bug_asin_out_of_range",
            "cs_bug_weak_rng",
            "cs_bug_insecure_random_seed",
            "cs_bug_negative_index_modulo",
            "cs_bug_sensitive_precision_loss",
            "cs_bug_expiration_overflow",
            "cs_bug_unsigned_underflow",
            "cs_bug_offset_overflow",
            "cs_bug_signed_to_unsigned_wrap",
            "cs_bug_narrowing_boundary_truncation",
            "cs_bug_unsigned_cast_guard_bypass",
            "cs_bug_floating_boundary_narrowing",
            "cs_bug_negative_index_modulo_api",
            "cs_bug_binary_formatter",
            "cs_bug_command_injection",
            "cs_bug_path_traversal",
            "cs_bug_weak_hash",
            "cs_bug_unsafe_code",
            "cs_bug_insecure_comparison",
            "cs_bug_static_mutable_state",
        };

        internal static readonly HashSet<string> DefaultHighConfidenceBugNodes = new(StringComparer.Ordinal)
        {
            "cs_bug_abs_overflow",
            "cs_bug_allocation_overflow",
            "cs_bug_index_overflow",
            "cs_bug_index_calculation_overflow",
            "cs_bug_accumulator_overflow",
            "cs_bug_generic_addition_overflow",
            "cs_bug_float_equality",
            "cs_bug_fp_infinity",
            "cs_bug_sqrt_negative",
            "cs_bug_log_negative",
            "cs_bug_bounds_arithmetic_overflow",
            "cs_bug_shift_mask_allocation",
        };

        public static readonly Dictionary<string, (string Severity, CSharpSecurityRisk SecurityRisk, string Template)> Bugs = new()
        {
            { "CSMATH001", ("Error", CSharpSecurityRisk.Low, "Potential divide by zero in '{0}'.") },
            { "CSMATH002", ("Error", CSharpSecurityRisk.Low, "Potential modulo by zero in '{0}'.") },
            { "CSMATH005", ("Warning", CSharpSecurityRisk.Medium, "Integer division truncation hazard in '{0}'. cast to double/float or use floating point literals if precise result needed.") },
            { "CSMATH006", ("Info", CSharpSecurityRisk.None, "Numerically unstable form involving '{0}'. Consider rewriting to Log1p.") },
            { "CSMATH007", ("Warning", CSharpSecurityRisk.Low, "Potential integer division truncation in '{0}'. Verify if precision loss is acceptable.") },
            { "CSMATH009", ("Warning", CSharpSecurityRisk.Low, "Potential bitwise XOR used as Power involving '{0} ^ {1}'. Bitwise XOR '^' is often mistaken for exponentiation in C#.") },
            { "CSMATH010", ("Error", CSharpSecurityRisk.High, "Generic integer division truncation hazard involving '{0}'. 'T.One / x' may be zero for integer types.") },
            { "CSMATH011", ("Warning", CSharpSecurityRisk.High, "Potential unsigned integer underflow involving '{0}'. Negation 'T.Zero - x' on unsigned types wraps around.") },
            { "CSMATH012", ("Info", CSharpSecurityRisk.None, "Potential performance limitation: Recursive call detected in '{0}'. Recursion may be an intentional algorithmic choice (e.g., cofactor expansion) but can be expensive for large inputs.") },
            { "CSSEC001", ("Warning", CSharpSecurityRisk.High, "Weak Random Number Generator '{0}'. Use 'System.Security.Cryptography.RandomNumberGenerator' for cryptographic purposes.") },
            { "CSSEC002", ("Error", CSharpSecurityRisk.Critical, "Insecure Deserialization '{0}'. BinaryFormatter is insecure and should not be used.") },
            { "CSSEC003", ("Error", CSharpSecurityRisk.Critical, "Potential Command Injection in '{0}'. Ensure arguments are sanitized.") },
            { "CSSEC004", ("Warning", CSharpSecurityRisk.High, "Potential Path Traversal in '{0}'. Ensure paths are validated.") },
            { "CSSEC005", ("Info", CSharpSecurityRisk.Low, "Console output in library code involving '{0}'. Libraries should typically return values or throw exceptions, not print to Console.") },
            { "CSSEC006", ("Error", CSharpSecurityRisk.High, "Weak Cryptographic Hashing '{0}'. MD5 is broken and should not be used for security purposes. Use SHA256 or better.") },
            { "CSMATH014", ("Info", CSharpSecurityRisk.Low, "Potential integer overflow in array allocation size involving '{0} * {1}'. In managed C#, this typically manifests as an exception/DoS for extreme sizes rather than silent memory corruption.") },
            { "CSMATH015", ("Warning", CSharpSecurityRisk.Low, "Potential index overflow hazard in array access involving '{0} * {1} + {2}'. In managed C#, this typically throws rather than corrupting memory, but it can still cause incorrect indexing or DoS.") },
            { "CSMATH016", ("Warning", CSharpSecurityRisk.High, "Potential overflow in generic math accumulation involving '{0} + {1} * {2}'. Result may wrap for integer types.") },
            { "CSMATH017", ("Warning", CSharpSecurityRisk.Low, "Potential overflow in 'Math.Abs({0})'. If the argument is 'MinValue', the result will remain negative and may overflow in checked contexts.") },
            { "CSMATH018", ("Info", CSharpSecurityRisk.Low, "Potential overflow in index calculation '{0} * {1} + {2}'. In managed C#, this is typically a robustness concern (exceptions/incorrect indexing) rather than memory corruption.") },
            { "CSMATH019", ("Info", CSharpSecurityRisk.None, "Potential precision issue in floating point equality involving '{0}'. Consider using a tolerance check.") },
            { "CSMATH023", ("Warning", CSharpSecurityRisk.None, "Potential division by zero involving variable divisor '{0} / {1}'. Ensure the divisor is non-zero (or explicitly guarded).") },
            { "CSMATH024", ("Warning", CSharpSecurityRisk.Low, "Potential NaN result from square root of negative number in '{0}'. Ensure the argument is non-negative.") },
            { "CSSEC007", ("Warning", CSharpSecurityRisk.Medium, "Unsafe code block detected. Ensure memory access is validated.") },
            { "CSSEC008", ("Warning", CSharpSecurityRisk.Medium, "Insecure comparison detected involving '{0}'. Consider using ConstantTimeAreEqual for sensitive data.") },
            { "CSSEC009", ("Warning", CSharpSecurityRisk.High, "Potential thread safety hazard involving static mutable state '{0}'. Modification of shared state should be synchronized.") },
            { "CSSEC011", ("Error", CSharpSecurityRisk.Critical, "Potential out-of-bounds access via negative index. The result of modulo operator '%' in C# can be negative; using it directly as an array index is a critical security risk.") },
            { "CSSEC012", ("Info", CSharpSecurityRisk.None, "Potential NaN/Infinity propagation involving '{0}'. For floating-point math this may be expected numerical behavior; treat as a correctness/stability concern, not a security issue.") },
            { "CSSEC014", ("Warning", CSharpSecurityRisk.Medium, "Insecure random seed detected involving '{0}'. Using time-based seeds like DateTime.Now.Ticks is predictable and insecure for cryptographic or security-sensitive applications.") },
            { "CSMATH025", ("Warning", CSharpSecurityRisk.High, "Potential overflow in generic math addition involving '{0} + {1}'. Result may wrap for integer types, leading to incorrect calculations or security vulnerabilities.") },
            { "CSMATH029", ("Warning", CSharpSecurityRisk.Low, "Potential NaN result from Log of non-positive number in '{0}'. Ensure the argument is positive.") },
            { "CSMATH030", ("Warning", CSharpSecurityRisk.Low, "Potential NaN result from inverse trig function in '{0}'. Ensure the argument is in range [-1, 1].") },
            { "CSSEC018", ("Warning", CSharpSecurityRisk.Medium, "Potential loss of precision in security-sensitive value involving '{0}'. Casting time-based or hash values to float can reduce entropy or cause collisions.") },
            { "CSSEC020", ("Warning", CSharpSecurityRisk.Medium, "Potential overflow in expiration/TTL calculation involving '{0} * {1}'. Result may wrap, leading to incorrect expiration logic or security bypass.") },
            { "CSSEC021", ("Error", CSharpSecurityRisk.High, "Potential unsigned integer wrap-around involving '{0} - {1}'. If {0} < {1}, the result will be a very large value, potentially causing buffer overflows or DoS.") },
            { "CSSEC023", ("Warning", CSharpSecurityRisk.Medium, "Potential overflow in stream seek/offset calculation involving '{0} * {1}'. Can lead to incorrect data access or DoS.") },
            { "CSSEC024", ("Error", CSharpSecurityRisk.High, "Potential signed-to-unsigned wrap involving '{0} - {1}'. Casting a negative result to unsigned can produce a very large value and bypass bounds checks.") },
            { "CSSEC025", ("Warning", CSharpSecurityRisk.High, "Potential narrowing conversion truncation involving '{0}'. Converting 64-bit boundary values to 32-bit can truncate and bypass validation.") },
            { "CSSEC026", ("Warning", CSharpSecurityRisk.Medium, "Potential bounds arithmetic overflow in '{0}' involving '{1}'. Arithmetic-derived ranges can wrap and bypass boundary assumptions.") },
            { "CSSEC027", ("Warning", CSharpSecurityRisk.Medium, "Potential signed/unsigned guard bypass involving '{0}'. Casting to unsigned before a zero-bound comparison can make validation ineffective.") },
            { "CSSEC028", ("Warning", CSharpSecurityRisk.Medium, "Potential floating/decimal narrowing in boundary value '{0}'. Converting non-integer boundary values to integer can truncate and bypass checks.") },
            { "CSSEC029", ("Warning", CSharpSecurityRisk.High, "Potential shift/mask size hazard in allocation involving '{0}'. Shift/mask arithmetic can overflow or under-allocate buffers.") },
            { "CSSEC030", ("Error", CSharpSecurityRisk.High, "Potential negative-modulo index in API call '{0}'. The '%' operator can produce negative values and cause invalid indexing.") },
            { "CSSEC031", ("Error", CSharpSecurityRisk.Critical, "Potential SQL Injection in '{0}'. Use parameterized queries and avoid string-concatenated SQL.") },
            { "CSSEC032", ("Error", CSharpSecurityRisk.High, "Potential LDAP Injection in '{0}'. Validate input and prefer safe filter construction APIs.") },
            { "CSSEC033", ("Error", CSharpSecurityRisk.High, "Potential XPath Injection in '{0}'. Avoid untrusted XPath fragments and use safe selection patterns.") },
            { "CSSEC034", ("Warning", CSharpSecurityRisk.High, "Potential Open Redirect in '{0}'. Restrict redirect targets to trusted allowlisted destinations.") },
            { "CSSEC035", ("Warning", CSharpSecurityRisk.Medium, "Potential Header Injection in '{0}'. Reject CRLF and validate header name/value input.") },
            { "CSSEC036", ("Error", CSharpSecurityRisk.High, "Potential Template Injection in '{0}'. Treat template text as code and avoid untrusted templates.") },
            { "CSSEC037", ("Warning", CSharpSecurityRisk.Medium, "Potential weak-randomness sink flow in '{0}'. Ensure randomness-critical sinks use cryptographically strong input.") },
            { "CSSEC038", ("Error", CSharpSecurityRisk.High, "Potential hardcoded-secret sink flow in '{0}'. Avoid embedding secrets and use secret-management facilities.") }
        };

        public static string GetMessage(string id, params object[] args)
        {
            if (Bugs.TryGetValue(id, out var info))
            {
                // Safety check for args count vs placeholders
                // But simplified: just format. If args missing, Format throws.
                // We should ensure templates match rule outputs.
                try {
                    return string.Format(info.Template, args);
                } catch {
                    return info.Template; // Fallback
                }
            }
            return $"Unknown bug {id}";
        }

        public static string GetSeverity(string id)
        {
            if (Bugs.TryGetValue(id, out var info))
            {
                return info.Severity;
            }
            return "Warning";
        }

        public static CSharpSecurityRisk GetSecurityRisk(string id)
        {
            if (Bugs.TryGetValue(id, out var info))
            {
                return info.SecurityRisk;
            }
            return CSharpSecurityRisk.None;
        }

        internal static bool TryMapBugNodeToId(string nodeHead, out string bugId)
        {
            nodeHead = NormalizeNodeHead(nodeHead);
            return BugNodeToId.TryGetValue(nodeHead, out bugId!);
        }

        internal static bool IsAlwaysConfirmedBugNode(string nodeHead)
        {
            nodeHead = NormalizeNodeHead(nodeHead);
            return AlwaysConfirmedBugNodes.Contains(nodeHead);
        }

        internal static bool IsDefaultHighConfidenceBugNode(string nodeHead)
        {
            nodeHead = NormalizeNodeHead(nodeHead);
            return DefaultHighConfidenceBugNodes.Contains(nodeHead);
        }

        internal static string NormalizeNodeHead(string nodeHead)
        {
            if (nodeHead.StartsWith("Func:", StringComparison.Ordinal))
            {
                return nodeHead.Substring(5);
            }

            return nodeHead;
        }
    }
}
