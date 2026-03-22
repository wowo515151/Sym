using System;
using System.Collections.Generic;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers.CSharpAnalysis
{
    public static class CSharpBugRuleLibrary
    {
        public static List<Rule> GetRules()
        {
            var rules = new List<Rule>();

            // Helpers for common symbols
            var x = new Wild("x");
            var y = new Wild("y");
            var a = new Wild("a");
            var b = new Wild("b");
            var m = new Wild("m");
            var start = new Wild("start");
            var len = new Wild("len");
            var mask = new Wild("mask");
            var zero = new Number(0);
            var one = new Number(1);

            // CSMATH001: Divide by Zero
            rules.Add(new Rule(Fn("cs_div_i32", x, zero), Fn("cs_bug_div_by_zero_i32", x)) { Name = "CSMATH001_DivByZero_i32" });

            // CSMATH002: Modulo by Zero
            rules.Add(new Rule(Fn("cs_mod_i32", x, zero), Fn("cs_bug_mod_by_zero_i32", x)) { Name = "CSMATH002_ModByZero_i32" });

                        // CSMATH006: Log1p
                        rules.Add(new Rule(Fn("cs_math_Log_f64", Fn("cs_add_f64_unchecked", one, x)), Fn("cs_bug_log1p_likely", x)) { Name = "CSMATH006_Log1p_1_x_likely", Condition = bld => IsSmallConstant(bld, "x") });
                        rules.Add(new Rule(Fn("cs_math_Log_f64", Fn("cs_add_f64_unchecked", x, one)), Fn("cs_bug_log1p_likely", x)) { Name = "CSMATH006_Log1p_x_1_likely", Condition = bld => IsSmallConstant(bld, "x") });
                        rules.Add(new Rule(Fn("cs_math_Log_f64", Fn("cs_add_f64_unchecked", one, x)), Fn("cs_bug_log1p", x)) { Name = "CSMATH006_Log1p_1_x", Condition = bld => !IsSmallConstant(bld, "x") });
                        rules.Add(new Rule(Fn("cs_math_Log_f64", Fn("cs_add_f64_unchecked", x, one)), Fn("cs_bug_log1p", x)) { Name = "CSMATH006_Log1p_x_1", Condition = bld => !IsSmallConstant(bld, "x") });
            
                        // CSMATH005: Integer division truncation
                        rules.Add(new Rule(Fn("cs_conv_i32_to_f32_unchecked", Fn("cs_div_i32", a, b)), Fn("cs_bug_int_div_truncation_confirmed", a, b)) { Name = "CSMATH005_IntDivTruncation_Unchecked_Confirmed", Condition = bld => IsTruncatedConstantDiv(bld, "a", "b") });
                        rules.Add(new Rule(Fn("cs_conv_i32_to_f32_unchecked", Fn("cs_div_i32", a, b)), Fn("cs_bug_int_div_truncation", a, b)) { Name = "CSMATH005_IntDivTruncation_Unchecked", Condition = bld => !IsTruncatedConstantDiv(bld, "a", "b") });
                        rules.Add(new Rule(Fn("cs_conv_i32_to_f32_checked", Fn("cs_div_i32", a, b)), Fn("cs_bug_int_div_truncation_confirmed", a, b)) { Name = "CSMATH005_IntDivTruncation_Checked_Confirmed", Condition = bld => IsTruncatedConstantDiv(bld, "a", "b") });
                        rules.Add(new Rule(Fn("cs_conv_i32_to_f32_checked", Fn("cs_div_i32", a, b)), Fn("cs_bug_int_div_truncation", a, b)) { Name = "CSMATH005_IntDivTruncation_Checked", Condition = bld => !IsTruncatedConstantDiv(bld, "a", "b") });
            // CSMATH007: Plain Integer Division
            rules.Add(new Rule(Fn("cs_div_i32", a, b), Fn("cs_bug_integer_division_intentional", a, b)) { Name = "CSMATH007_IntDiv_Intentional", Condition = bld => IsIntentionalHalving(bld, "a", "b") });
            rules.Add(new Rule(Fn("cs_div_i32", a, b), Fn("cs_bug_integer_division", a, b)) { Name = "CSMATH007_IntDiv", Condition = bld => !IsIntentionalHalving(bld, "a", "b") });

            // CSMATH009: XOR as Power
            rules.Add(new Rule(Fn("cs_xor_i32", a, b), Fn("cs_bug_xor_as_pow_high", a, b)) { Name = "CSMATH009_XorAsPow_High", Condition = bld => AreBothConstants(bld, "a", "b") });
            rules.Add(new Rule(Fn("cs_xor_i32", a, b), Fn("cs_bug_xor_as_pow_medium", a, b)) { Name = "CSMATH009_XorAsPow_Medium", Condition = bld => !AreBothConstants(bld, "a", "b") && IsPowerLikeExponent(bld, "b") });
            rules.Add(new Rule(Fn("cs_xor_i32", a, b), Fn("cs_bug_xor_as_pow", a, b)) { Name = "CSMATH009_XorAsPow", Condition = bld => !AreBothConstants(bld, "a", "b") && !IsPowerLikeExponent(bld, "b") });

            // CSMATH010: Generic Integer Division
            rules.Add(new Rule(Fn("cs_div_gen", new Symbol("One"), x), Fn("cs_bug_generic_div_truncation", x)) { Name = "CSMATH010_GenericIntDiv_One" });

            // CSMATH023: Variable Division
            rules.Add(new Rule(Fn("cs_div_gen", a, b), Fn("cs_bug_potential_variable_division", a, b)) { Name = "CSMATH023_VariableDiv" });
            
            // CSSEC012: Infinity/NaN propagation
            rules.Add(new Rule(Fn("cs_div_gen", a, new Symbol("Zero")), Fn("cs_bug_fp_infinity", a, new Symbol("Zero"))) { Name = "CSSEC012_InfinityPropagation_Zero" });

            // CSMATH011: Generic Underflow
            rules.Add(new Rule(Fn("cs_sub_gen_unchecked", new Symbol("Zero"), x), Fn("cs_bug_generic_underflow", x)) { Name = "CSMATH011_GenericUnderflow_Zero_Unchecked" });
            rules.Add(new Rule(Fn("cs_sub_gen_checked", new Symbol("Zero"), x), Fn("cs_bug_generic_underflow", x)) { Name = "CSMATH011_GenericUnderflow_Zero_Checked" });

            // CSMATH012: Recursion
            rules.Add(new Rule(Fn("cs_recursion_detected", x), Fn("cs_bug_recursion", x)) { Name = "CSMATH012_Recursion" });

            // CSMATH014: Allocation Overflow
            rules.Add(new Rule(Fn("cs_new_array", new Wild("t"), Fn("cs_mul_i32_unchecked", a, b)), Fn("cs_bug_allocation_overflow", a, b)) { Name = "CSMATH014_AllocOverflow_i32_Unchecked" });
            rules.Add(new Rule(Fn("cs_new_array", new Wild("t"), Fn("cs_mul_i32_checked", a, b)), Fn("cs_bug_allocation_overflow", a, b)) { Name = "CSMATH014_AllocOverflow_i32_Checked" });

            // CSMATH015: Index Overflow Hazard
            rules.Add(new Rule(Fn("cs_array_get", new Wild("arr"), Fn("cs_add_i32_unchecked", Fn("cs_mul_i32_unchecked", a, b), new Wild("c"))),
                Fn("cs_bug_index_overflow", a, b, new Wild("c"))) { Name = "CSMATH015_IndexOverflow_i32_Unchecked" });
            
            rules.Add(new Rule(Fn("cs_array_get", new Wild("arr"), Fn("cs_mul_i32_unchecked", a, b)),
                Fn("cs_bug_index_overflow", a, b, new Number(0))) { Name = "CSMATH015_IndexOverflow_Simple_i32_Unchecked" });

            // CSSEC011: Negative Index Modulo
            rules.Add(new Rule(Fn("cs_array_get", new Wild("arr"), Fn("cs_mod_i32", a, b)), Fn("cs_bug_negative_index_modulo", a, b)) { Name = "CSSEC011_NegativeIndexModulo_i32" });
            rules.Add(new Rule(Fn("cs_array_get", new Wild("arr"), Fn("cs_mod_gen", a, b)), Fn("cs_bug_negative_index_modulo", a, b)) { Name = "CSSEC011_NegativeIndexModulo_gen" });

            // CSMATH016: Accumulator Overflow
            rules.Add(new Rule(Fn("cs_add_gen_unchecked", new Wild("s"), Fn("cs_mul_gen_unchecked", a, b)),
                Fn("cs_bug_accumulator_overflow", new Wild("s"), a, b)) { Name = "CSMATH016_AccumulatorOverflow_gen_Unchecked" });

            // CSMATH025: Generic Addition Overflow
            rules.Add(new Rule(Fn("cs_add_gen_unchecked", a, b), Fn("cs_bug_generic_addition_overflow", a, b)) { Name = "CSMATH025_GenericAdditionOverflow_gen_Unchecked" });

            // CSMATH017: Math.Abs Overflow
            rules.Add(new Rule(Fn("cs_math_Abs_i32", x), Fn("cs_bug_abs_overflow", x)) { Name = "CSMATH017_AbsOverflow_i32" });
            rules.Add(new Rule(Fn("cs_math_Abs_i64", x), Fn("cs_bug_abs_overflow", x)) { Name = "CSMATH017_AbsOverflow_i64" });

            // CSMATH024: Sqrt of negative
            rules.Add(new Rule(Fn("cs_math_Sqrt_f64", x), Fn("cs_bug_sqrt_negative", x))
            {
                Name = "CSMATH024_SqrtNegative_f64",
                Condition = bld => IsNegative(bld, "x")
            });

            // CSMATH029: Log of non-positive
            rules.Add(new Rule(Fn("cs_math_Log_f64", x), Fn("cs_bug_log_negative", x))
            {
                Name = "CSMATH029_LogNegative_f64",
                Condition = bld => IsNonPositive(bld, "x")
            });

            // CSMATH030: Inverse Trig out of range
            rules.Add(new Rule(Fn("cs_math_Asin_f64", x), Fn("cs_bug_asin_out_of_range", x))
            {
                Name = "CSMATH030_AsinRange_f64",
                Condition = bld => IsOutOfTrigRange(bld, "x")
            });

            // CSSEC018: Sensitive Precision Loss
            rules.Add(new Rule(Fn("cs_conv_i64_to_f32_unchecked", x), Fn("cs_bug_sensitive_precision_loss", x))
            {
                Name = "CSSEC018_PrecisionLoss_Ticks_Unchecked",
                Condition = bld => IsTicks(bld, "x")
            });
            rules.Add(new Rule(Fn("cs_conv_i64_to_f32_checked", x), Fn("cs_bug_sensitive_precision_loss", x))
            {
                Name = "CSSEC018_PrecisionLoss_Ticks_Checked",
                Condition = bld => IsTicks(bld, "x")
            });

            // CSMATH018: Index Calculation Overflow
            rules.Add(new Rule(Fn("cs_add_i32_unchecked", Fn("cs_mul_i32_unchecked", a, b), x),
                Fn("cs_bug_index_calculation_overflow", a, b, x)) { Name = "CSMATH018_IndexCalcOverflow_i32_Unchecked" });

            // CSSEC001: Weak RNG
            rules.Add(new Rule(Fn("cs_new", new Symbol("System.Random")), Fn("cs_bug_weak_rng", new Symbol("Random"))) { Name = "CSSEC001_WeakRNG_0" });
            rules.Add(new Rule(Fn("cs_new", new Symbol("System.Random"), x), Fn("cs_bug_weak_rng", x)) { Name = "CSSEC001_WeakRNG_1" });

            // CSSEC014: Insecure Random Seed
            rules.Add(new Rule(Fn("cs_new", new Symbol("System.Random"), x), Fn("cs_bug_insecure_random_seed", x))
            {
                Name = "CSSEC014_InsecureSeed",
                Condition = bld => IsSymbolMatching(bld, "x", n => n.Contains("DateTime") || n.Contains("Ticks"))
            });

            // CSSEC002: BinaryFormatter
            rules.Add(new Rule(Fn("cs_new", new Symbol("System.Runtime.Serialization.Formatters.Binary.BinaryFormatter")), Fn("cs_bug_binary_formatter", new Symbol("BinaryFormatter"))) { Name = "CSSEC002_BinaryFormatter" });

            // CSSEC003: Command Injection
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.Diagnostics.Process.Start"), x), Fn("cs_bug_command_injection", x)) { Name = "CSSEC003_ProcessStart_1" });
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.Diagnostics.Process.Start"), x, new Wild("y")), Fn("cs_bug_command_injection", x)) { Name = "CSSEC003_ProcessStart_2" });

            // CSSEC004: Path Traversal
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.IO.File.ReadAllText"), x), Fn("cs_bug_path_traversal", x)) { Name = "CSSEC004_FileRead" });
            rules.Add(new Rule(Fn("cs_new", new Symbol("System.IO.FileStream"), x, new Wild("y")), Fn("cs_bug_path_traversal", x)) { Name = "CSSEC004_FileStream" });

            // CSSEC005: Console Output
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.Console.WriteLine"), x), Fn("cs_bug_console_output", x)) { Name = "CSSEC005_ConsoleWriteLine" });
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.Console.Write"), x), Fn("cs_bug_console_output", x)) { Name = "CSSEC005_ConsoleWrite" });

            // CSSEC006: Weak Hashing
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.Security.Cryptography.MD5.Create")), Fn("cs_bug_weak_hash", new Symbol("MD5"))) { Name = "CSSEC006_MD5Create" });

            // CSSEC007: Unsafe Code handled by special block symbol
            rules.Add(new Rule(new Symbol("cs_unsafe_block"), Fn("cs_bug_unsafe_code", new Symbol("unsafe"))) { Name = "CSSEC007_Unsafe" });

            // CSMATH019: Float Equality
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.Double.Equals"), x, new Wild("y")), Fn("cs_bug_float_equality", x)) { Name = "CSMATH019_FloatEquality" });
            rules.Add(new Rule(Fn("cs_eq_gen", x, new Symbol("Zero")), Fn("cs_bug_float_equality", x)) { Name = "CSMATH019_FloatEquality_GenZero" });
            rules.Add(new Rule(Fn("cs_eq_f64", x, a), Fn("cs_bug_float_equality", x)) { Name = "CSMATH019_FloatEquality_f64" });
            rules.Add(new Rule(Fn("cs_eq_f32", x, a), Fn("cs_bug_float_equality", x)) { Name = "CSMATH019_FloatEquality_f32" });

            // CSSEC008: Insecure Comparison
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.String.Equals"), x, y), Fn("cs_bug_insecure_comparison", x))
            {
                Name = "CSSEC008_InsecureComparison_Call",
                Condition = bld => !IsNull(bld, "x") && !IsNull(bld, "y") && (IsSensitive(bld, "x") || IsSensitive(bld, "y"))
            });
            rules.Add(new Rule(Fn("cs_eq_str", x, y), Fn("cs_bug_insecure_comparison", x))
            {
                Name = "CSSEC008_InsecureComparison_OpStr",
                Condition = bld => !IsNull(bld, "x") && !IsNull(bld, "y") && (IsSensitive(bld, "x") || IsSensitive(bld, "y"))
            });

            // CSSEC009: Static Mutable State
            rules.Add(new Rule(Fn("cs_static_assignment", x), Fn("cs_bug_static_mutable_state", x)) { Name = "CSSEC009_StaticMutableState" });

            // CSSEC020: Expiration/TTL Overflow
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.DateTime.AddSeconds"), x), Fn("cs_bug_expiration_overflow", x)) { Name = "CSSEC020_AddSeconds_Overflow", Condition = bld => IsSuspiciousMath(bld, "x") });
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.DateTime.AddMilliseconds"), x), Fn("cs_bug_expiration_overflow", x)) { Name = "CSSEC020_AddMilliseconds_Overflow", Condition = bld => IsSuspiciousMath(bld, "x") });

            // CSSEC021: Unsigned Length Underflow
            rules.Add(new Rule(Fn("cs_sub_u32_unchecked", a, b), Fn("cs_bug_unsigned_underflow", a, b)) { Name = "CSSEC021_UnsignedUnderflow_u32" });
            rules.Add(new Rule(Fn("cs_sub_u64_unchecked", a, b), Fn("cs_bug_unsigned_underflow", a, b)) { Name = "CSSEC021_UnsignedUnderflow_u64" });

            // CSSEC023: Seek/Offset Overflow
            rules.Add(new Rule(Fn("cs_call", new Symbol("System.IO.Stream.Seek"), x, new Wild("origin")), Fn("cs_bug_offset_overflow", x)) { Name = "CSSEC023_StreamSeek_Overflow", Condition = bld => IsSuspiciousMath(bld, "x") });

            // CSSEC024: Signed-to-unsigned wrap after subtraction.
            // Keep this scoped to subtraction->cast so it does not overlap generic numeric diagnostics.
            rules.Add(new Rule(Fn("cs_conv_i32_to_u32_unchecked", Fn("cs_sub_i32_unchecked", a, b)), Fn("cs_bug_signed_to_unsigned_wrap", a, b)) { Name = "CSSEC024_SignedToUnsignedWrap_i32_Sub" });
            rules.Add(new Rule(Fn("cs_conv_i64_to_u64_unchecked", Fn("cs_sub_i64_unchecked", a, b)), Fn("cs_bug_signed_to_unsigned_wrap", a, b)) { Name = "CSSEC024_SignedToUnsignedWrap_i64_Sub" });

            // CSSEC025: Narrowing 64-bit values to 32-bit for boundary-like variables.
            rules.Add(new Rule(Fn("cs_conv_i64_to_i32_unchecked", x), Fn("cs_bug_narrowing_boundary_truncation", x))
            {
                Name = "CSSEC025_Narrowing_i64_to_i32_Unchecked",
                Condition = bld => IsBoundaryLikeSymbol(bld, "x")
            });
            rules.Add(new Rule(Fn("cs_conv_u64_to_u32_unchecked", x), Fn("cs_bug_narrowing_boundary_truncation", x))
            {
                Name = "CSSEC025_Narrowing_u64_to_u32_Unchecked",
                Condition = bld => IsBoundaryLikeSymbol(bld, "x")
            });

            // CSSEC026: Bounds arithmetic hazards in common range APIs.
            rules.Add(new Rule(Fn("cs_call", m, new Wild("src"), new Wild("srcIndex"), new Wild("dst"), new Wild("dstIndex"), len),
                Fn("cs_bug_bounds_arithmetic_overflow", m, len))
            {
                Name = "CSSEC026_BoundsArithmetic_CopyLike",
                Condition = bld => IsBoundsSinkSymbol(bld, "m") && IsSuspiciousBoundsMath(bld, "len")
            });
            rules.Add(new Rule(Fn("cs_call", m, start, len), Fn("cs_bug_bounds_arithmetic_overflow", m, start, len))
            {
                Name = "CSSEC026_BoundsArithmetic_Range2",
                Condition = bld => IsBoundsSinkSymbol(bld, "m") && (IsSuspiciousBoundsMath(bld, "start") || IsSuspiciousBoundsMath(bld, "len"))
            });
            rules.Add(new Rule(Fn("cs_call", m, start), Fn("cs_bug_bounds_arithmetic_overflow", m, start))
            {
                Name = "CSSEC026_BoundsArithmetic_Range1",
                Condition = bld => IsBoundsSinkSymbol(bld, "m") && IsSuspiciousBoundsMath(bld, "start")
            });

            // CSSEC027: Signed-to-unsigned cast used in zero-bound comparisons.
            rules.Add(new Rule(Fn("cs_lt_u32", Fn("cs_conv_i32_to_u32_unchecked", x), zero), Fn("cs_bug_unsigned_cast_guard_bypass", x))
            {
                Name = "CSSEC027_UnsignedCastNegativeCheck_i32_Lt"
            });
            rules.Add(new Rule(Fn("cs_lt_u32", Fn("cs_conv_i32_to_u32_checked", x), zero), Fn("cs_bug_unsigned_cast_guard_bypass", x))
            {
                Name = "CSSEC027_UnsignedCastNegativeCheck_i32_Lt_Checked"
            });
            rules.Add(new Rule(Fn("cs_lt_u64", Fn("cs_conv_i64_to_u64_unchecked", x), zero), Fn("cs_bug_unsigned_cast_guard_bypass", x))
            {
                Name = "CSSEC027_UnsignedCastNegativeCheck_i64_Lt"
            });
            rules.Add(new Rule(Fn("cs_lt_u64", Fn("cs_conv_i64_to_u64_checked", x), zero), Fn("cs_bug_unsigned_cast_guard_bypass", x))
            {
                Name = "CSSEC027_UnsignedCastNegativeCheck_i64_Lt_Checked"
            });
            rules.Add(new Rule(Fn("cs_gte_u32", Fn("cs_conv_i32_to_u32_unchecked", x), zero), Fn("cs_bug_unsigned_cast_guard_bypass", x))
            {
                Name = "CSSEC027_UnsignedCastNonNegativeGuard_i32_Gte"
            });
            rules.Add(new Rule(Fn("cs_gte_u64", Fn("cs_conv_i64_to_u64_unchecked", x), zero), Fn("cs_bug_unsigned_cast_guard_bypass", x))
            {
                Name = "CSSEC027_UnsignedCastNonNegativeGuard_i64_Gte"
            });

            // CSSEC028: Floating/decimal boundary values narrowed to integers.
            rules.Add(new Rule(Fn("cs_conv_f64_to_i32_unchecked", x), Fn("cs_bug_floating_boundary_narrowing", x))
            {
                Name = "CSSEC028_Narrowing_f64_to_i32_Unchecked",
                Condition = bld => IsBoundaryLikeSymbol(bld, "x")
            });
            rules.Add(new Rule(Fn("cs_conv_f64_to_u32_unchecked", x), Fn("cs_bug_floating_boundary_narrowing", x))
            {
                Name = "CSSEC028_Narrowing_f64_to_u32_Unchecked",
                Condition = bld => IsBoundaryLikeSymbol(bld, "x")
            });
            rules.Add(new Rule(Fn("cs_conv_dec_to_i32_unchecked", x), Fn("cs_bug_floating_boundary_narrowing", x))
            {
                Name = "CSSEC028_Narrowing_dec_to_i32_Unchecked",
                Condition = bld => IsBoundaryLikeSymbol(bld, "x")
            });
            rules.Add(new Rule(Fn("cs_conv_dec_to_u32_unchecked", x), Fn("cs_bug_floating_boundary_narrowing", x))
            {
                Name = "CSSEC028_Narrowing_dec_to_u32_Unchecked",
                Condition = bld => IsBoundaryLikeSymbol(bld, "x")
            });

            // CSSEC029: Shift/mask arithmetic used as allocation size.
            rules.Add(new Rule(Fn("cs_new_array", new Wild("t"), Fn("cs_shl_i32", a, b)), Fn("cs_bug_shift_mask_allocation", a, b))
            {
                Name = "CSSEC029_ShiftSizeAllocation_i32"
            });
            rules.Add(new Rule(Fn("cs_new_array", new Wild("t"), Fn("cs_shl_u32", a, b)), Fn("cs_bug_shift_mask_allocation", a, b))
            {
                Name = "CSSEC029_ShiftSizeAllocation_u32"
            });
            rules.Add(new Rule(Fn("cs_new_array", new Wild("t"), Fn("cs_and_i32", Fn("cs_add_i32_unchecked", a, b), mask)), Fn("cs_bug_shift_mask_allocation", a, b, mask))
            {
                Name = "CSSEC029_AddMaskAllocation_i32_Unchecked"
            });
            rules.Add(new Rule(Fn("cs_new_array", new Wild("t"), Fn("cs_and_i32", Fn("cs_add_i32_checked", a, b), mask)), Fn("cs_bug_shift_mask_allocation", a, b, mask))
            {
                Name = "CSSEC029_AddMaskAllocation_i32_Checked"
            });

            // CSSEC030: Negative modulo index used in non-array indexing APIs.
            rules.Add(new Rule(Fn("cs_call", m, Fn("cs_mod_i32", a, b)), Fn("cs_bug_negative_index_modulo_api", m, a, b))
            {
                Name = "CSSEC030_NegativeModuloApiIndex_1Arg_i32",
                Condition = bld => IsIndexLikeApiSymbol(bld, "m")
            });
            rules.Add(new Rule(Fn("cs_call", m, Fn("cs_mod_gen", a, b)), Fn("cs_bug_negative_index_modulo_api", m, a, b))
            {
                Name = "CSSEC030_NegativeModuloApiIndex_1Arg_gen",
                Condition = bld => IsIndexLikeApiSymbol(bld, "m")
            });
            rules.Add(new Rule(Fn("cs_call", m, Fn("cs_mod_i32", a, b), x), Fn("cs_bug_negative_index_modulo_api", m, a, b))
            {
                Name = "CSSEC030_NegativeModuloApiIndex_FirstArg_i32",
                Condition = bld => IsIndexLikeApiSymbol(bld, "m")
            });
            rules.Add(new Rule(Fn("cs_call", m, x, Fn("cs_mod_i32", a, b)), Fn("cs_bug_negative_index_modulo_api", m, a, b))
            {
                Name = "CSSEC030_NegativeModuloApiIndex_SecondArg_i32",
                Condition = bld => IsIndexLikeApiSymbol(bld, "m")
            });
            rules.Add(new Rule(Fn("cs_call", m, Fn("cs_mod_gen", a, b), x), Fn("cs_bug_negative_index_modulo_api", m, a, b))
            {
                Name = "CSSEC030_NegativeModuloApiIndex_FirstArg_gen",
                Condition = bld => IsIndexLikeApiSymbol(bld, "m")
            });
            rules.Add(new Rule(Fn("cs_call", m, x, Fn("cs_mod_gen", a, b)), Fn("cs_bug_negative_index_modulo_api", m, a, b))
            {
                Name = "CSSEC030_NegativeModuloApiIndex_SecondArg_gen",
                Condition = bld => IsIndexLikeApiSymbol(bld, "m")
            });

            // Include Guard Rules
            rules.AddRange(CSharpGuardRuleLibrary.GetRules());

            return rules;
        }

        private static bool IsTicks(IReadOnlyDictionary<string, IExpression> b, string k) => IsSymbolMatching(b, k, n => n.Contains("Ticks", StringComparison.OrdinalIgnoreCase));
        private static bool IsBoundaryLikeSymbol(IReadOnlyDictionary<string, IExpression> b, string k) =>
            IsSymbolMatching(b, k, n =>
                n.Contains("length", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("len", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("size", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("capacity", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("ttl", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("offset", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("index", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("position", StringComparison.OrdinalIgnoreCase));
        private static bool IsBoundsSinkSymbol(IReadOnlyDictionary<string, IExpression> b, string k) =>
            IsSymbolMatching(b, k, n =>
                n.Equals("System.Array.Copy", StringComparison.Ordinal) ||
                n.Equals("System.Buffer.BlockCopy", StringComparison.Ordinal) ||
                n.Contains(".Substring", StringComparison.Ordinal) ||
                n.Contains(".Slice", StringComparison.Ordinal));
                        private static bool IsIndexLikeApiSymbol(IReadOnlyDictionary<string, IExpression> b, string k) =>
                            IsSymbolMatching(b, k, n =>
                                n.Contains(".Substring", StringComparison.Ordinal) ||
                                n.Contains(".Slice", StringComparison.Ordinal) ||
                                n.Contains(".ElementAt", StringComparison.Ordinal) ||
                                n.Contains(".ElementAtOrDefault", StringComparison.Ordinal) ||
                                n.Contains(".RemoveAt", StringComparison.Ordinal) ||
                                n.Contains(".Insert", StringComparison.Ordinal) ||
                                n.Contains(".GetRange", StringComparison.Ordinal));
                
                        private static bool IsSmallConstant(IReadOnlyDictionary<string, IExpression> b, string k)
                        {
                            if (b.TryGetValue(k, out var e) && e is Number n)
                            {
                                return Math.Abs(n.Value) < 0.1m;
                            }
                            return false;
                        }
                
                        private static bool IsTruncatedConstantDiv(IReadOnlyDictionary<string, IExpression> b, string aKey, string bKey)
                        {
                            if (b.TryGetValue(aKey, out var aExpr) && aExpr is Number nA &&
                                b.TryGetValue(bKey, out var bExpr) && bExpr is Number nB)
                            {
                                try {
                                    long va = Convert.ToInt64(nA.Value);
                                    long vb = Convert.ToInt64(nB.Value);
                                    if (vb != 0 && va % vb != 0) return true;
                                } catch { }
                            }
                            return false;
                        }
                
                        private static bool AreBothConstants(IReadOnlyDictionary<string, IExpression> b, string aKey, string bKey)
                        {
                            return b.TryGetValue(aKey, out var aExpr) && aExpr is Number &&
                                   b.TryGetValue(bKey, out var bExpr) && bExpr is Number;
                        }
                
                        private static bool IsPowerLikeExponent(IReadOnlyDictionary<string, IExpression> b, string k)
                        {
                            if (b.TryGetValue(k, out var e) && e is Number n)
                            {
                                try {
                                    long exp = Convert.ToInt64(n.Value);
                                    return exp >= 2 && exp <= 10;
                                } catch { }
                            }
                            return false;
                        }
                
                        private static bool IsIntentionalHalving(IReadOnlyDictionary<string, IExpression> b, string numKey, string denKey)                {
                    if (b.TryGetValue(denKey, out var denExpr) && denExpr is Number n && n.Value == 2)
                    {
                        if (b.TryGetValue(numKey, out var numExpr))
                        {
                            var name = numExpr.ToDisplayString();
                            return name.Contains("length", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("size", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("capacity", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    return false;
                }
        
                private static bool IsNull(IReadOnlyDictionary<string, IExpression> b, string k) => b.TryGetValue(k, out var e) && e is Symbol s && s.Name == "null";
        
                private static bool IsSensitive(IReadOnlyDictionary<string, IExpression> b, string k)
                {
                    if (!b.TryGetValue(k, out var e)) return false;
                    var text = e.ToDisplayString();
                    return text.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("passwd", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("pwd", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("privatekey", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("private_key", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("hmac", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("digest", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("salt", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("nonce", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("bearer", StringComparison.OrdinalIgnoreCase);
                }
        
                private static bool IsNegative(IReadOnlyDictionary<string, IExpression> b, string k) => b.TryGetValue(k, out var e) && e is Number n && n.Value < 0;        private static bool IsNonPositive(IReadOnlyDictionary<string, IExpression> b, string k) => b.TryGetValue(k, out var e) && e is Number n && n.Value <= 0;
        private static bool IsOutOfTrigRange(IReadOnlyDictionary<string, IExpression> b, string k) => b.TryGetValue(k, out var e) && e is Number n && Math.Abs(n.Value) > 1.0m;

        private static bool IsSymbolMatching(IReadOnlyDictionary<string, IExpression> bindings, string key, Func<string, bool> predicate)
        {
            if (bindings.TryGetValue(key, out var expr))
            {
                return predicate(expr.ToDisplayString());
            }
            return false;
        }

        private static bool IsSuspiciousMath(IReadOnlyDictionary<string, IExpression> bindings, string key)
        {
            if (bindings.TryGetValue(key, out var expr))
            {
                var str = expr.ToDisplayString();
                return str.Contains("_mul_") || str.Contains("*");
            }
            return false;
        }

        private static bool IsSuspiciousBoundsMath(IReadOnlyDictionary<string, IExpression> bindings, string key)
        {
            if (bindings.TryGetValue(key, out var expr))
            {
                var str = expr.ToDisplayString();
                return str.Contains("_add_", StringComparison.Ordinal) ||
                       str.Contains("_sub_", StringComparison.Ordinal) ||
                       str.Contains("_mul_", StringComparison.Ordinal) ||
                       str.Contains("_shl_", StringComparison.Ordinal) ||
                       str.Contains("_shr_", StringComparison.Ordinal) ||
                       str.Contains("_and_", StringComparison.Ordinal) ||
                       str.Contains("_or_", StringComparison.Ordinal) ||
                       str.Contains("_xor_", StringComparison.Ordinal) ||
                       str.Contains("+", StringComparison.Ordinal) ||
                       str.Contains("-", StringComparison.Ordinal) ||
                       str.Contains("*", StringComparison.Ordinal) ||
                       str.Contains("<<", StringComparison.Ordinal) ||
                       str.Contains(">>", StringComparison.Ordinal) ||
                       str.Contains("&", StringComparison.Ordinal);
            }
            return false;
        }

        private static IExpression Fn(string name, params IExpression[] args) => new Function(name, args);
    }
}
