// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers.CSharpAnalysis
{
    internal static class CSharpGuardRuleLibrary
    {
        private static readonly string[] NumericSuffixes =
        {
            "i32",
            "u32",
            "i64",
            "u64",
            "f32",
            "f64",
            "dec",
            "gen",
            "nint",
            "nuint"
        };

        public static IReadOnlyList<Rule> GetRules()
        {
            var rules = new List<Rule>();
            var x = new Wild("x");
            var a = new Wild("a");
            var b = new Wild("b");
            var m = new Wild("m");
            var y = new Wild("y");
            var zero = new Number(0);
            var zeroSymbol = new Symbol("Zero");
            var one = new Number(1);
            var mask7F = new Number(2147483647m);
            var nullSymbol = new Symbol("null");

            foreach (var suffix in NumericSuffixes)
            {
                // x & 0x7FFFFFFF implies NonNegative(x & 0x7FFFFFFF)
                // NOTE: the non-negative property applies to the masked result, not necessarily the input.
                rules.Add(new Rule(
                    Fn($"cs_and_{suffix}", x, mask7F),
                    Fn("cs_guard_non_negative", Fn($"cs_and_{suffix}", x, mask7F)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_Mask_Result"
                });

                // 0x7FFFFFFF & x implies NonNegative(0x7FFFFFFF & x)
                rules.Add(new Rule(
                    Fn($"cs_and_{suffix}", mask7F, x),
                    Fn("cs_guard_non_negative", Fn($"cs_and_{suffix}", mask7F, x)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_Mask_Result_Commuted"
                });

                // x >= 0 implies NonNegative
                rules.Add(new Rule(
                    Fn($"cs_gte_{suffix}", x, zero),
                    Fn("cs_guard_non_negative", x))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_GteZero"
                });

                // x > 0 implies NonNegative
                rules.Add(new Rule(
                    Fn($"cs_gt_{suffix}", x, zero),
                    Fn("cs_guard_non_negative", x))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_GtZero"
                });

                // 0 <= x implies NonNegative
                rules.Add(new Rule(
                    Fn($"cs_lte_{suffix}", zero, x),
                    Fn("cs_guard_non_negative", x))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_ZeroLte"
                });

                // 0 < x implies NonNegative
                rules.Add(new Rule(
                    Fn($"cs_lt_{suffix}", zero, x),
                    Fn("cs_guard_non_negative", x))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_ZeroLt"
                });

                // Math.Max(x, 0) implies non-negative result
                rules.Add(new Rule(
                    Fn($"cs_math_Max_{suffix}", x, zero),
                    Fn("cs_guard_non_negative", Fn($"cs_math_Max_{suffix}", x, zero)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_MathMax_Zero_Result"
                });

                rules.Add(new Rule(
                    Fn($"cs_math_Max_{suffix}", zero, x),
                    Fn("cs_guard_non_negative", Fn($"cs_math_Max_{suffix}", zero, x)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_MathMax_Zero_Result_Commuted"
                });

                // Math.Abs(x) implies non-negative result
                rules.Add(new Rule(
                    Fn($"cs_math_Abs_{suffix}", x),
                    Fn("cs_guard_non_negative", Fn($"cs_math_Abs_{suffix}", x)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_MathAbs_Result"
                });

                // Shift amounts < 32 or & 31 are valid shift amounts
                var thirtyTwo = new Number(32);
                var thirtyOne = new Number(31);
                rules.Add(new Rule(
                    Fn($"cs_lt_{suffix}", x, thirtyTwo),
                    Fn("cs_guard_valid_shift_amount", x))
                {
                    Name = $"CSGUARD_ValidShiftAmount_{suffix}_Lt32"
                });
                rules.Add(new Rule(
                    Fn($"cs_and_{suffix}", x, thirtyOne),
                    Fn("cs_guard_valid_shift_amount", Fn($"cs_and_{suffix}", x, thirtyOne)))
                {
                    Name = $"CSGUARD_ValidShiftAmount_{suffix}_And31"
                });

                // a >= b implies NonNegative(a - b)
                rules.Add(new Rule(
                    Fn($"cs_gte_{suffix}", a, b),
                    Fn("cs_guard_non_negative", Fn($"cs_sub_{suffix}_unchecked", a, b)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_Sub_Gte"
                });

                // Bit counting pattern (Popcount): v - ((v >> 1) & 0x5555555555555555UL)
                // This is safe from underflow.
                var maskPop1 = new Number(6148914691236517205m); // 0x5555555555555555
                rules.Add(new Rule(
                    Fn($"cs_sub_{suffix}_unchecked", x, Fn($"cs_and_{suffix}", Fn($"cs_shr_{suffix}", x, one), maskPop1)),
                    Fn("cs_guard_non_negative", Fn($"cs_sub_{suffix}_unchecked", x, Fn($"cs_and_{suffix}", Fn($"cs_shr_{suffix}", x, one), maskPop1))))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_Popcount_Sub"
                });

                // Narrowing conversion guard: size < int.MaxValue
                var intMax = new Number(2147483647m);
                rules.Add(new Rule(
                    Fn($"cs_lt_{suffix}", x, intMax),
                    Fn("cs_guard_in_int32_range", x))
                {
                    Name = $"CSGUARD_InInt32Range_{suffix}_LtMax"
                });
                rules.Add(new Rule(
                    Fn($"cs_lte_{suffix}", x, intMax),
                    Fn("cs_guard_in_int32_range", x))
                {
                    Name = $"CSGUARD_InInt32Range_{suffix}_LteMax"
                });
                rules.Add(new Rule(
                    Fn($"cs_gt_{suffix}", intMax, x),
                    Fn("cs_guard_in_int32_range", x))
                {
                    Name = $"CSGUARD_InInt32Range_{suffix}_MaxGt"
                });
                rules.Add(new Rule(
                    Fn($"cs_gte_{suffix}", intMax, x),
                    Fn("cs_guard_in_int32_range", x))
                {
                    Name = $"CSGUARD_InInt32Range_{suffix}_MaxGte"
                });

                // NonNegative(x) implies NonNegative(x % y) (if x % y exists)
                // This covers cases where the dividend is proven non-negative.
                rules.Add(new Rule(
                    Fn("cs_guard_non_negative", x),
                    Fn("cs_guard_non_negative", Fn($"cs_mod_{suffix}", x, y)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_Mod_Prop",
                    Condition = bld => bld.ContainsKey("y")
                });

                // C - (x % C) is always positive/non-negative for positive C.
                // This handles cases like: 16 - (num % 16)
                rules.Add(new Rule(
                    Fn($"cs_sub_{suffix}_unchecked", m, Fn($"cs_mod_{suffix}", x, m)),
                    Fn("cs_guard_non_negative", Fn($"cs_sub_{suffix}_unchecked", m, Fn($"cs_mod_{suffix}", x, m))))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_Modulo_Boundary_Safe",
                    Condition = bld => bld.TryGetValue("m", out var e) && e is Number num && num.Value > 0
                });

                // Range-Check Idiom: (uint)(x - min) < range
                // This is used for status codes, HTTP methods, etc.
                // We map this to a specific guard kind to suppress wrap-around warnings on the subtraction.
                if (suffix == "i32")
                {
                    rules.Add(new Rule(
                        Fn("cs_lt_u32", Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", x, a)), b),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i32_unchecked", x, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i32_Sub_Lt"
                    });
                    rules.Add(new Rule(
                        Fn("cs_lte_u32", Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", x, a)), b),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i32_unchecked", x, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i32_Sub_Lte"
                    });

                    // Two-sided variant: (uint)(x - min) <= (uint)(max - min)
                    // Common pattern: (uint)(value - start) <= (uint)(end - start)
                    rules.Add(new Rule(
                        Fn("cs_lte_u32",
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", x, a)),
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", b, a))),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i32_unchecked", x, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i32_Sub_TwoSided_Lte_Left"
                    });

                    rules.Add(new Rule(
                        Fn("cs_lte_u32",
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", x, a)),
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", b, a))),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i32_unchecked", b, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i32_Sub_TwoSided_Lte_Right"
                    });

                    rules.Add(new Rule(
                        Fn("cs_lt_u32",
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", x, a)),
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", b, a))),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i32_unchecked", x, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i32_Sub_TwoSided_Lt_Left"
                    });

                    rules.Add(new Rule(
                        Fn("cs_lt_u32",
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", x, a)),
                            Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", b, a))),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i32_unchecked", b, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i32_Sub_TwoSided_Lt_Right"
                    });
                }
                else if (suffix == "i64")
                {
                    rules.Add(new Rule(
                        Fn("cs_lt_u64", Fn("cs_conv_i64_to_u64_unchecked", Fn("cs_sub_i64_unchecked", x, a)), b),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i64_unchecked", x, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i64_Sub_Lt"
                    });
                    rules.Add(new Rule(
                        Fn("cs_lte_u64", Fn("cs_conv_i64_to_u64_unchecked", Fn("cs_sub_i64_unchecked", x, a)), b),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i64_unchecked", x, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i64_Sub_Lte"
                    });

                    // Two-sided variant: (ulong)(x - min) <= (ulong)(max - min)
                    rules.Add(new Rule(
                        Fn("cs_lte_u64",
                            Fn("cs_conv_i64_to_u64_unchecked", Fn("cs_sub_i64_unchecked", x, a)),
                            Fn("cs_conv_i64_to_u64_unchecked", Fn("cs_sub_i64_unchecked", b, a))),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i64_unchecked", x, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i64_Sub_TwoSided_Lte_Left"
                    });

                    rules.Add(new Rule(
                        Fn("cs_lte_u64",
                            Fn("cs_conv_i64_to_u64_unchecked", Fn("cs_sub_i64_unchecked", x, a)),
                            Fn("cs_conv_i64_to_u64_unchecked", Fn("cs_sub_i64_unchecked", b, a))),
                        Fn("cs_guard_in_range_idiom", Fn("cs_sub_i64_unchecked", b, a)))
                    {
                        Name = "CSGUARD_InRangeIdiom_i64_Sub_TwoSided_Lte_Right"
                    });
                }

                // Generic fallback for any other suffix
                var uSuffixGen = "u" + suffix;
                if (suffix == "gen") uSuffixGen = "ugen";

                rules.Add(new Rule(
                    Fn($"cs_lt_{uSuffixGen}", Fn($"cs_conv_{suffix}_to_{uSuffixGen}_unchecked", Fn($"cs_sub_{suffix}_unchecked", x, a)), b),
                    Fn("cs_guard_in_range_idiom", Fn($"cs_sub_{suffix}_unchecked", x, a)))
                {
                    Name = $"CSGUARD_InRangeIdiom_{suffix}_Sub_Lt_Gen"
                });

                // NonNegative(x & 0x7FFFFFFF) - common hash masking pattern
                rules.Add(new Rule(
                    Fn($"cs_and_{suffix}", x, mask7F),
                    Fn("cs_guard_non_negative", Fn($"cs_and_{suffix}", x, mask7F)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_And_Mask7F"
                });
                rules.Add(new Rule(
                    Fn($"cs_and_{suffix}", mask7F, x),
                    Fn("cs_guard_non_negative", Fn($"cs_and_{suffix}", mask7F, x)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_And_Mask7F_Commuted"
                });

                // NonNegative(x) implies NonNegative(x & y) for any y (bitwise AND with non-negative is non-negative)
                // Actually, if x is non-negative (MSB 0), then x & y will also have MSB 0.
                rules.Add(new Rule(
                    Fn("cs_guard_non_negative", x),
                    Fn("cs_guard_non_negative", Fn($"cs_and_{suffix}", x, y)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_And_Prop",
                    Condition = bld => bld.ContainsKey("y")
                });
                rules.Add(new Rule(
                    Fn("cs_guard_non_negative", x),
                    Fn("cs_guard_non_negative", Fn($"cs_and_{suffix}", y, x)))
                {
                    Name = $"CSGUARD_NonNegative_{suffix}_And_Prop_Commuted",
                    Condition = bld => bld.ContainsKey("y")
                });

                // x != 0
                rules.Add(new Rule(
                    Fn($"cs_neq_{suffix}", x, zero),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Neq_Forward"
                });

                rules.Add(new Rule(
                    Fn($"cs_neq_{suffix}", zero, x),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Neq_Reverse"
                });

                rules.Add(new Rule(
                    Fn($"cs_neq_{suffix}", x, zeroSymbol),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Neq_Symbol_Forward"
                });

                rules.Add(new Rule(
                    Fn($"cs_neq_{suffix}", zeroSymbol, x),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Neq_Symbol_Reverse"
                });

                // x > 0 / 0 < x / x < 0 / 0 > x all imply x != 0.
                rules.Add(new Rule(
                    Fn($"cs_gt_{suffix}", x, zero),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Gt"
                });

                rules.Add(new Rule(
                    Fn($"cs_lt_{suffix}", zero, x),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Lt_Reverse"
                });

                rules.Add(new Rule(
                    Fn($"cs_lt_{suffix}", x, zero),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Lt"
                });

                rules.Add(new Rule(
                    Fn($"cs_gt_{suffix}", zero, x),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Gt_Reverse"
                });

                // x >= 1 implies x != 0.
                rules.Add(new Rule(
                    Fn($"cs_gte_{suffix}", x, one),
                    Fn("cs_guard_nonzero", x))
                {
                    Name = $"CSGUARD_NonZero_{suffix}_Gte_One"
                });

                // --- Safe unsigned-wrap idioms ---
                // Bit-twiddling patterns like: x & (x - 1), x ^ (x - 1)
                // These intentionally use unsigned wrap behavior; suppress CSSEC021 on the (x - 1) sub-expression.
                if (suffix is "u32" or "u64" or "nuint")
                {
                    rules.Add(new Rule(
                        Fn($"cs_and_{suffix}", x, Fn($"cs_sub_{suffix}_unchecked", x, one)),
                        Fn("cs_guard_safe_unsigned_wrap", Fn($"cs_sub_{suffix}_unchecked", x, one)))
                    {
                        Name = $"CSGUARD_SafeUnsignedWrap_{suffix}_And_X_Sub1"
                    });

                    rules.Add(new Rule(
                        Fn($"cs_and_{suffix}", Fn($"cs_sub_{suffix}_unchecked", x, one), x),
                        Fn("cs_guard_safe_unsigned_wrap", Fn($"cs_sub_{suffix}_unchecked", x, one)))
                    {
                        Name = $"CSGUARD_SafeUnsignedWrap_{suffix}_And_Sub1_X"
                    });

                    rules.Add(new Rule(
                        Fn($"cs_xor_{suffix}", x, Fn($"cs_sub_{suffix}_unchecked", x, one)),
                        Fn("cs_guard_safe_unsigned_wrap", Fn($"cs_sub_{suffix}_unchecked", x, one)))
                    {
                        Name = $"CSGUARD_SafeUnsignedWrap_{suffix}_Xor_X_Sub1"
                    });

                    rules.Add(new Rule(
                        Fn($"cs_xor_{suffix}", Fn($"cs_sub_{suffix}_unchecked", x, one), x),
                        Fn("cs_guard_safe_unsigned_wrap", Fn($"cs_sub_{suffix}_unchecked", x, one)))
                    {
                        Name = $"CSGUARD_SafeUnsignedWrap_{suffix}_Xor_Sub1_X"
                    });
                }

                // UTF-16 surrogate math: if (code > 0xFFFF) then ((code >> 16) - 1) is non-negative.
                // This suppresses false positives from safe codepoint-to-surrogate conversions.
                if (suffix == "u32")
                {
                    var u16Max = new Number(65535m);
                    rules.Add(new Rule(
                        Fn("cs_gt_u32", x, u16Max),
                        Fn("cs_guard_non_negative", Fn("cs_sub_u32_unchecked", Fn("cs_shr_u32", x, new Number(16m)), one)))
                    {
                        Name = "CSGUARD_NonNegative_u32_Ucs4_Shift16_Sub1"
                    });
                }
            }

            rules.Add(new Rule(
                Fn("cs_neq_obj", x, nullSymbol),
                Fn("cs_guard_not_null", x))
            {
                Name = "CSGUARD_NotNull_Obj_Forward"
            });
            rules.Add(new Rule(
                Fn("cs_neq_obj", nullSymbol, x),
                Fn("cs_guard_not_null", x))
            {
                Name = "CSGUARD_NotNull_Obj_Reverse"
            });
            rules.Add(new Rule(
                Fn("cs_neq_str", x, nullSymbol),
                Fn("cs_guard_not_null", x))
            {
                Name = "CSGUARD_NotNull_Str_Forward"
            });
            rules.Add(new Rule(
                Fn("cs_neq_str", nullSymbol, x),
                Fn("cs_guard_not_null", x))
            {
                Name = "CSGUARD_NotNull_Str_Reverse"
            });

            // Null checks are constant-time equivalent (the risk is timing of comparison, but null is a special case)
            // This suppresses CSSEC008 for "key == null", "nonce == null", etc.
            rules.Add(new Rule(
                Fn("cs_eq_str", x, nullSymbol),
                Fn("cs_guard_constant_time", x))
            {
                Name = "CSGUARD_IsConstantTimeEquivalent_NullCheck"
            });

            rules.Add(new Rule(
                Fn("cs_neq_str", x, nullSymbol),
                Fn("cs_guard_constant_time", x))
            {
                Name = "CSGUARD_IsConstantTimeEquivalent_NotNullCheck"
            });

            // Boolean validators used in branch guards.
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_path_valid",
                ruleNamePrefix: "CSGUARD_PathValidator",
                methodPredicate: IsPathValidatorMethodName);
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_command_allowlisted",
                ruleNamePrefix: "CSGUARD_CommandValidator",
                methodPredicate: IsCommandValidatorMethodName);
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_sql_validated",
                ruleNamePrefix: "CSGUARD_SqlValidator",
                methodPredicate: IsSqlValidatorMethodName);
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_ldap_filter_validated",
                ruleNamePrefix: "CSGUARD_LdapValidator",
                methodPredicate: IsLdapValidatorMethodName);
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_xpath_validated",
                ruleNamePrefix: "CSGUARD_XPathValidator",
                methodPredicate: IsXPathValidatorMethodName);
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_redirect_allowlisted",
                ruleNamePrefix: "CSGUARD_RedirectValidator",
                methodPredicate: IsRedirectValidatorMethodName);
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_header_validated",
                ruleNamePrefix: "CSGUARD_HeaderValidator",
                methodPredicate: IsHeaderValidatorMethodName,
                guardSecondArgumentInArity2: true);
            AddValidatorGuardRules(
                rules,
                guardHead: "cs_guard_template_trusted",
                ruleNamePrefix: "CSGUARD_TemplateValidator",
                methodPredicate: IsTemplateValidatorMethodName);

            // Literal-equality allowlisting and common validation idioms.
            // These reduce false positives when sinks are gated by simple constant checks
            // rather than dedicated validator helper methods.
            AddStringLiteralEqualitySinkGuardRules(rules);
            AddPathFileNameEqualityGuardRules(rules);

            AddTransformationGuardRules(rules, "cs_guard_path_valid", "CSGUARD_PathSafe", IsPathSafeTransformationMethod);
            AddTryParseValidatorGuardRules(rules);
            AddSafeToStringTransformationRules(rules);

            return rules;
        }

        private static void AddStringLiteralEqualitySinkGuardRules(ICollection<Rule> rules)
        {
            var x = new Wild("x");
            var c = new Wild("c");
            var m = new Wild("m");

            // If a string is proven equal to a string literal in a branch guard,
            // it is effectively allowlisted to a fixed value for that path.
            // We treat this as satisfying the various sink-specific guard families.
            string[] sinkGuards =
            {
                "cs_guard_sql_validated",
                "cs_guard_path_valid",
                "cs_guard_command_allowlisted",
                "cs_guard_ldap_filter_validated",
                "cs_guard_xpath_validated",
                "cs_guard_redirect_allowlisted",
                "cs_guard_header_validated",
                "cs_guard_template_trusted"
            };

            foreach (var guard in sinkGuards)
            {
                rules.Add(new Rule(
                    Fn("cs_eq_str", x, c),
                    Fn(guard, x),
                    condition: RuleBindingConditions.StringLiteral("c"))
                {
                    Name = $"CSGUARD_LiteralEq_{guard}_Forward"
                });

                rules.Add(new Rule(
                    Fn("cs_eq_str", c, x),
                    Fn(guard, x),
                    condition: RuleBindingConditions.StringLiteral("c"))
                {
                    Name = $"CSGUARD_LiteralEq_{guard}_Reverse"
                });

                // string.Equals(x, "literal") and string.Equals("literal", x)
                rules.Add(new Rule(
                    Fn("cs_call", m, x, c),
                    Fn(guard, x),
                    condition: bindings => IsSymbolMatching(bindings, "m", IsStringEqualsMethod) && IsStringLiteral(bindings, "c"))
                {
                    Name = $"CSGUARD_StringEquals_{guard}_Arity2_Arg0"
                });

                rules.Add(new Rule(
                    Fn("cs_call", m, c, x),
                    Fn(guard, x),
                    condition: bindings => IsSymbolMatching(bindings, "m", IsStringEqualsMethod) && IsStringLiteral(bindings, "c"))
                {
                    Name = $"CSGUARD_StringEquals_{guard}_Arity2_Arg1"
                });

                // string.Equals(x, "literal", comparison)
                rules.Add(new Rule(
                    Fn("cs_call", m, x, c, new Wild("cmp")),
                    Fn(guard, x),
                    condition: bindings => IsSymbolMatching(bindings, "m", IsStringEqualsMethod) && IsStringLiteral(bindings, "c"))
                {
                    Name = $"CSGUARD_StringEquals_{guard}_Arity3_Arg0"
                });

                rules.Add(new Rule(
                    Fn("cs_call", m, c, x, new Wild("cmp")),
                    Fn(guard, x),
                    condition: bindings => IsSymbolMatching(bindings, "m", IsStringEqualsMethod) && IsStringLiteral(bindings, "c"))
                {
                    Name = $"CSGUARD_StringEquals_{guard}_Arity3_Arg1"
                });
            }
        }

        private static void AddPathFileNameEqualityGuardRules(ICollection<Rule> rules)
        {
            var m = new Wild("m");
            var x = new Wild("x");

            // Idiom: Path.GetFileName(path) == path  => path contains no directory separators.
            // When used as a branch guard, this is a strong signal the caller intends to restrict
            // to a file name (reducing path traversal false positives).
            rules.Add(new Rule(
                Fn("cs_eq_str", Fn("cs_call", m, x), x),
                Fn("cs_guard_path_valid", x),
                condition: RuleBindingConditions.SymbolContainsAnyIgnoreCase(
                    "m",
                    "System.IO.Path.GetFileName",
                    "System.IO.Path.GetFileNameWithoutExtension"))
            {
                Name = "CSGUARD_PathValid_PathGetFileName_Equals_Input"
            });

            rules.Add(new Rule(
                Fn("cs_eq_str", x, Fn("cs_call", m, x)),
                Fn("cs_guard_path_valid", x),
                condition: RuleBindingConditions.SymbolContainsAnyIgnoreCase(
                    "m",
                    "System.IO.Path.GetFileName",
                    "System.IO.Path.GetFileNameWithoutExtension"))
            {
                Name = "CSGUARD_PathValid_Input_Equals_PathGetFileName"
            });
        }

        private static bool IsStringEqualsMethod(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            // Lowerer emits "{ContainingType}.{MethodName}" using Roslyn's default
            // symbol formatting. For special types that can be either fully-qualified
            // ("System.String") or keyword ("string").
            return methodDisplay.Contains("System.String.Equals", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("string.Equals", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathFileNameExtractionMethod(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("System.IO.Path.GetFileName", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("System.IO.Path.GetFileNameWithoutExtension", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStringLiteral(IReadOnlyDictionary<string, IExpression> bindings, string key)
        {
            if (!bindings.TryGetValue(key, out var expression))
            {
                return false;
            }

            if (expression is Symbol symbol)
            {
                return symbol.Name.StartsWith("str:", StringComparison.Ordinal);
            }

            return false;
        }

        private static bool IsPathSafeTransformationMethod(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay)) return false;
            return methodDisplay.Contains("Path.GetFileName", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Path.GetExtension", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddTransformationGuardRules(
            ICollection<Rule> rules,
            string guardHead,
            string ruleNamePrefix,
            Func<string, bool> methodPredicate)
        {
            var m = new Wild("m");
            var x = new Wild("x");

            rules.Add(new Rule(
                Fn("cs_call", m, x),
                Fn(guardHead, Fn("cs_call", m, x)),
                condition: bindings => IsSymbolMatching(bindings, "m", methodPredicate))
            {
                Name = $"{ruleNamePrefix}_Transform"
            });
        }

        private static void AddTryParseValidatorGuardRules(ICollection<Rule> rules)
        {
            var m = new Wild("m");
            var x = new Wild("x");
            var y = new Wild("y");

            string[] guards = {
                "cs_guard_sql_validated",
                "cs_guard_path_valid",
                "cs_guard_command_allowlisted",
                "cs_guard_ldap_filter_validated",
                "cs_guard_xpath_validated",
                "cs_guard_redirect_allowlisted",
                "cs_guard_header_validated",
                "cs_guard_template_trusted"
            };

            foreach (var guard in guards)
            {
                rules.Add(new Rule(
                    Fn("cs_call", m, x, y),
                    Fn(guard, x),
                    condition: RuleBindingConditions.SymbolContainsAnyIgnoreCase(
                        "m",
                        "Guid.TryParse",
                        "Int32.TryParse",
                        "Int64.TryParse",
                        "UInt32.TryParse",
                        "UInt64.TryParse",
                        "Int16.TryParse",
                        "UInt16.TryParse",
                        "Byte.TryParse",
                        "SByte.TryParse",
                        "Double.TryParse",
                        "Single.TryParse",
                        "Decimal.TryParse",
                        "Boolean.TryParse",
                        "DateTime.TryParse",
                        "DateTimeOffset.TryParse",
                        "TimeSpan.TryParse"))
                {
                    Name = $"CSGUARD_TryParseValidator_{guard}_Arity2"
                });
                
                rules.Add(new Rule(
                    Fn("cs_call", m, x, y, new Wild("z")),
                    Fn(guard, x),
                    condition: RuleBindingConditions.SymbolContainsAnyIgnoreCase(
                        "m",
                        "Guid.TryParse",
                        "Int32.TryParse",
                        "Int64.TryParse",
                        "UInt32.TryParse",
                        "UInt64.TryParse",
                        "Int16.TryParse",
                        "UInt16.TryParse",
                        "Byte.TryParse",
                        "SByte.TryParse",
                        "Double.TryParse",
                        "Single.TryParse",
                        "Decimal.TryParse",
                        "Boolean.TryParse",
                        "DateTime.TryParse",
                        "DateTimeOffset.TryParse",
                        "TimeSpan.TryParse"))
                {
                    Name = $"CSGUARD_TryParseValidator_{guard}_Arity3"
                });
                
                rules.Add(new Rule(
                    Fn("cs_call", m, x, y, new Wild("z"), new Wild("w")),
                    Fn(guard, x),
                    condition: RuleBindingConditions.SymbolContainsAnyIgnoreCase(
                        "m",
                        "Guid.TryParse",
                        "Int32.TryParse",
                        "Int64.TryParse",
                        "UInt32.TryParse",
                        "UInt64.TryParse",
                        "Int16.TryParse",
                        "UInt16.TryParse",
                        "Byte.TryParse",
                        "SByte.TryParse",
                        "Double.TryParse",
                        "Single.TryParse",
                        "Decimal.TryParse",
                        "Boolean.TryParse",
                        "DateTime.TryParse",
                        "DateTimeOffset.TryParse",
                        "TimeSpan.TryParse"))
                {
                    Name = $"CSGUARD_TryParseValidator_{guard}_Arity4"
                });
            }
        }

        private static bool IsSafeTryParseMethod(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay)) return false;
            return methodDisplay.Contains("Guid.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Int32.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Int64.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("UInt32.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("UInt64.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Int16.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("UInt16.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Byte.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("SByte.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Double.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Single.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Decimal.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Boolean.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("DateTime.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("DateTimeOffset.TryParse", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TimeSpan.TryParse", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddSafeToStringTransformationRules(ICollection<Rule> rules)
        {
            var m = new Wild("m");

            string[] guards = {
                "cs_guard_sql_validated",
                "cs_guard_path_valid",
                "cs_guard_command_allowlisted",
                "cs_guard_ldap_filter_validated",
                "cs_guard_xpath_validated",
                "cs_guard_redirect_allowlisted",
                "cs_guard_header_validated",
                "cs_guard_template_trusted"
            };

            foreach (var guard in guards)
            {
                rules.Add(new Rule(
                    Fn("cs_call", m),
                    Fn(guard, Fn("cs_call", m)),
                    condition: bindings => IsSymbolMatching(bindings, "m", IsSafeToStringMethod))
                {
                    Name = $"CSGUARD_SafeToString_{guard}_Arity0"
                });

                rules.Add(new Rule(
                    Fn("cs_call", m, new Wild("fmt")),
                    Fn(guard, Fn("cs_call", m, new Wild("fmt"))),
                    condition: bindings => IsSymbolMatching(bindings, "m", IsSafeToStringMethod))
                {
                    Name = $"CSGUARD_SafeToString_{guard}_Arity1"
                });
            }
        }

        private static bool IsSafeToStringMethod(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay)) return false;
            return methodDisplay.Contains("Guid.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Int32.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Int64.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("UInt32.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("UInt64.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Boolean.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Double.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Single.ToString", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("Decimal.ToString", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsSafePath", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidatePath", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("EnsureSafePath", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsPathAllowed", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsPathUnderRoot", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsUnderRootPath", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidatePath", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCommandValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsAllowedCommand", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsCommandAllowed", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidateCommand", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("AllowListCommand", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsSafeCommand", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidateCommand", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSqlValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsSafeSql", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidateSql", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidateSql", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsParameterizedSql", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsSqlAllowlisted", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsAllowedSql", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLdapValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsSafeLdap", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidateLdapFilter", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidateLdapFilter", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsAllowedLdapFilter", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsXPathValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsSafeXPath", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidateXPath", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidateXPath", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsAllowedXPath", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRedirectValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsLocalUrl", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsSafeRedirect", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidateRedirect", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidateRedirect", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsAllowedRedirect", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHeaderValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsValidHeaderName", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsValidHeaderValue", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidateHeader", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidateHeader", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTemplateValidatorMethodName(string methodDisplay)
        {
            if (string.IsNullOrWhiteSpace(methodDisplay))
            {
                return false;
            }

            return methodDisplay.Contains("IsTrustedTemplate", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("ValidateTemplate", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("TryValidateTemplate", StringComparison.OrdinalIgnoreCase) ||
                   methodDisplay.Contains("IsAllowedTemplate", StringComparison.OrdinalIgnoreCase);
        }

        // Memory note: keep this helper in sync with CSharpSecurityFlowCore.RequiredGuardBySinkKind
        // and CSharpGuardProver.TryMapGuardHead when adding/removing sink guard families.
        private static void AddValidatorGuardRules(
            ICollection<Rule> rules,
            string guardHead,
            string ruleNamePrefix,
            Func<string, bool> methodPredicate,
            bool guardSecondArgumentInArity2 = false)
        {
            var m = new Wild("m");
            var x = new Wild("x");
            var y = new Wild("y");

            rules.Add(new Rule(
                Fn("cs_call", m, x),
                Fn(guardHead, x),
                condition: bindings => IsSymbolMatching(bindings, "m", methodPredicate))
            {
                Name = $"{ruleNamePrefix}_Arity1"
            });

            rules.Add(new Rule(
                Fn("cs_call", m, x, y),
                Fn(guardHead, x),
                condition: bindings => IsSymbolMatching(bindings, "m", methodPredicate))
            {
                Name = $"{ruleNamePrefix}_Arity2_Arg0"
            });

            if (guardSecondArgumentInArity2)
            {
                rules.Add(new Rule(
                    Fn("cs_call", m, x, y),
                    Fn(guardHead, y),
                    condition: bindings => IsSymbolMatching(bindings, "m", methodPredicate))
                {
                    Name = $"{ruleNamePrefix}_Arity2_Arg1"
                });
            }
        }

        private static bool IsSymbolMatching(
            IReadOnlyDictionary<string, IExpression> bindings,
            string key,
            Func<string, bool> predicate)
        {
            if (!bindings.TryGetValue(key, out var expression))
            {
                return false;
            }

            if (expression is Symbol symbol)
            {
                return predicate(symbol.Name);
            }

            return predicate(expression.ToDisplayString());
        }

        private static IExpression Fn(string name, params IExpression[] args) => new Function(name, args);
    }
}
