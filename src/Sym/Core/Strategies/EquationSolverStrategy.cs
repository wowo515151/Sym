//Copyright Warren Harding 2025.
using System;
using System.Collections.Immutable;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Atoms;
using Sym.Operations;
using System.Linq;

namespace Sym.Core.Strategies
{
    /// <summary>
    /// **`EquationSolverStrategy`**:
    /// *   **Purpose:** To transform an `Equality` expression into the form `TargetVariable = solution`.
    /// *   **Implementation:** It will validate inputs, repeatedly apply general simplification rules, and then use **isolation tactics**. These tactics involve identifying the outermost operation on the side with the `TargetVariable` and applying inverse operations (e.g., subtracting a term from both sides, dividing by a coefficient). It will check for the goal after each transformation.
    /// </summary>
    public sealed class EquationSolverStrategy : ISolverStrategy
    {
        /// <summary>
        /// Solves a given problem expression (assumed to be an Equality) for a target variable.
        /// </summary>
        /// <param name="problem">The Equality expression to solve (e.g., SomeExpression = OtherExpression).</param>
        /// <param name="context">The context containing solver settings like rules, target variable, and tracing options.</param>
        /// <returns>A SolveResult indicating success/failure, the result equation, and a message.</returns>
        public SolveResult Solve(IExpression? problem, SolveContext context)
        {
            // If problem is null, problem is not Equality will be true.
            if (problem is not Equality currentEquation)
            {
                return SolveResult.Failure(problem, "EquationSolverStrategy requires an Equality expression as input.");
            }

            var targetVariable = context.TargetVariable;
            if (targetVariable is null)
            {
                return SolveResult.Failure(problem, "Target variable must be specified for EquationSolverStrategy.");
            }

            currentEquation = (Equality)currentEquation.Canonicalize(); // Canonicalize the initial problem
            if (context.EnableTracing) Console.WriteLine($"DEBUG: EquationSolverStrategy starting on: {currentEquation.ToDisplayString()}");
            
            ImmutableList<IExpression>.Builder? traceBuilder = context.EnableTracing ? ImmutableList.CreateBuilder<IExpression>() : null;
            traceBuilder?.Add(currentEquation);

            var seenExpressions = new System.Collections.Generic.HashSet<IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
            seenExpressions.Add(currentEquation);

            var polynomialSolve = TrySolvePolynomial(currentEquation, targetVariable, context.CancellationToken);
            if (polynomialSolve is not null)
            {
                if (context.EnableTracing) Console.WriteLine($"DEBUG: EquationSolverStrategy solved via polynomial: {polynomialSolve.ToDisplayString()}");
                traceBuilder?.Add(polynomialSolve);
                return SolveResult.Success(polynomialSolve, "Polynomial equation solved directly.", traceBuilder?.ToImmutable());
            }

            bool changedInIteration;

            if (context.EnableTracing || System.Environment.GetEnvironmentVariable("TAKEEXAM_VERBOSE") == "1") Console.Error.WriteLine($"            [debug] EquationSolverStrategy: Solve loop starting. Iterations: {context.MaxIterations}");
            for (int i = 0; i < context.MaxIterations; i++)
            {
                context.ThrowIfCancellationRequested();
                if (context.EnableTracing || System.Environment.GetEnvironmentVariable("TAKEEXAM_VERBOSE") == "1") Console.Error.WriteLine($"            [debug] EquationSolverStrategy: Iteration {i}");
                changedInIteration = false;
                Equality previousEquation = currentEquation;

                // Step 1: Apply one rewrite pass (bounded by MaxIterations via the outer loop)
                if (context.EnableTracing || System.Environment.GetEnvironmentVariable("TAKEEXAM_VERBOSE") == "1") Console.Error.WriteLine("            [debug] EquationSolverStrategy: Step 1: RewriteSinglePass");
                RewriterResult simplificationResult = Rewriter.RewriteSinglePass(currentEquation, context.Rules, context.Assumptions);
                if (simplificationResult.Changed)
                {
                    currentEquation = (Equality)simplificationResult.RewrittenExpression;
                    changedInIteration = true;
                    traceBuilder?.Add(currentEquation);
                }

                // Step 2: Check if goal is achieved after simplification
                if (CheckGoal(currentEquation, targetVariable))
                {
                    return SolveResult.Success(currentEquation, "Equation solved successfully.", traceBuilder?.ToImmutable());
                }

                // Step 3: Apply isolation tactics
                if (context.EnableTracing || System.Environment.GetEnvironmentVariable("TAKEEXAM_VERBOSE") == "1") Console.Error.WriteLine("            [debug] EquationSolverStrategy: Step 3: IsolateSide");
                bool isolationOccurred = false;

                if (currentEquation.LeftOperand.ContainsSymbol(targetVariable) && !currentEquation.RightOperand.ContainsSymbol(targetVariable))
                {
                    // Target variable is on the left side, try to isolate
                    Equality newEquation = IsolateSide(currentEquation.LeftOperand, currentEquation.RightOperand, targetVariable, out isolationOccurred);
                    if (isolationOccurred)
                    {
                        currentEquation = newEquation; // This newEquation is already canonicalized within IsolateSide
                        changedInIteration = true;
                    }
                }
                else if (!currentEquation.LeftOperand.ContainsSymbol(targetVariable) && currentEquation.RightOperand.ContainsSymbol(targetVariable))
                {
                    // Target variable is on the right side, swap operands and try to isolate
                    Equality newEquation = IsolateSide(currentEquation.RightOperand, currentEquation.LeftOperand, targetVariable, out isolationOccurred);
                    if (isolationOccurred)
                    {
                        currentEquation = newEquation; // This newEquation is already canonicalized within IsolateSide
                        changedInIteration = true;
                    }
                }
                else if (currentEquation.LeftOperand.ContainsSymbol(targetVariable) && currentEquation.RightOperand.ContainsSymbol(targetVariable))
                {
                    // Target variable is on both sides.
                    // First, attempt a direct solve for simple linear forms: a*x + b = c*x + d.
                    if (TrySolveSimpleLinear(currentEquation, targetVariable, out var solved, out _))
                    {
                        traceBuilder?.Add(solved);
                        return SolveResult.Success(solved, "Equation solved successfully.", traceBuilder?.ToImmutable());
                    }

                    // Fallback: rearrange to (Left - Right = 0) to give rewrite/simplification a chance.
                    IExpression rearrangedLeft = new Add(currentEquation.LeftOperand, new Multiply(new Number(-1m), currentEquation.RightOperand).Canonicalize()).Canonicalize();
                    IExpression zero = new Number(0m);

                    Equality rearrangedEquation = new Equality(rearrangedLeft, zero);
                    if (!rearrangedEquation.InternalEquals(currentEquation))
                    {
                        currentEquation = rearrangedEquation;
                        changedInIteration = true;
                        isolationOccurred = true; // Mark as changed by isolation (rearrangement)
                    }
                }
                else
                {
                    // Neither side contains the target variable.
                    // If the equation simplifies to a tautology (e.g., 5 = 5), it means X can be anything.
                    if (currentEquation.LeftOperand.InternalEquals(currentEquation.RightOperand))
                    {
                        return SolveResult.Success(currentEquation, "Equation is an identity.", traceBuilder?.ToImmutable());
                    }

                    // If it simplifies to a contradiction (e.g., 5 = 6), there's no solution.
                    // In both cases, if the target variable is gone, this strategy cannot 'isolate' it into `targetVariable = solution` form.
                    return SolveResult.Failure(currentEquation, $"Failed to solve. Target variable '{targetVariable.ToDisplayString()}' could not be isolated.", traceBuilder?.ToImmutable());
                }

                if (isolationOccurred)
                {
                    // Add trace if isolation occurred and it was not captured by simplification in previous iteration
                    if (!currentEquation.InternalEquals(previousEquation))
                    {
                        traceBuilder?.Add(currentEquation);
                    }
                }

                // Try polynomial solve again after potential transformations
                var polySolved = TrySolvePolynomial(currentEquation, targetVariable, context.CancellationToken);
                if (polySolved is not null)
                {
                    if (context.EnableTracing) Console.WriteLine($"DEBUG: EquationSolverStrategy solved via polynomial (in loop): {polySolved.ToDisplayString()}");
                    traceBuilder?.Add(polySolved);
                    return SolveResult.Success(polySolved, "Polynomial equation solved directly.", traceBuilder?.ToImmutable());
                }

                // Step 4: Check if goal is achieved after isolation
                if (CheckGoal(currentEquation, targetVariable))
                {
                    return SolveResult.Success(currentEquation, "Equation solved successfully.", traceBuilder?.ToImmutable());
                }

                if (!seenExpressions.Add(currentEquation))
                {
                    return SolveResult.Failure(currentEquation, "Equation solving stopped due to detected cycle.", traceBuilder?.ToImmutable());
                }

                // Step 5: Check for stagnation
                // If neither simplification nor isolation made progress in this iteration
                if (!changedInIteration)
                {
                    // Attempt aggressive expansion as a last resort
                    var expandedEq = ExpandSimple(currentEquation);
                    if (!expandedEq.InternalEquals(currentEquation))
                    {
                        currentEquation = expandedEq;
                        traceBuilder?.Add(currentEquation);
                        continue; // Retry loop with expanded form
                    }

                    return SolveResult.Failure(currentEquation, $"Failed to solve. No further progress achievable. Final expression: {currentEquation.ToDisplayString()}", traceBuilder?.ToImmutable());
                }
            }

            // Exited loop, max iterations reached
            return SolveResult.Failure(currentEquation, $"Max iterations ({context.MaxIterations}) reached before full solution.", traceBuilder?.ToImmutable());
        }

        private static Equality ExpandSimple(Equality eq)
        {
            // Simple distribution: (A+B)*C -> A*C + B*C
            // Apply to LHS and RHS if they are Multiply containing Add
            var lhs = ExpandExpression(eq.LeftOperand);
            var rhs = ExpandExpression(eq.RightOperand);
            return new Equality(lhs, rhs).Canonicalize() as Equality ?? new Equality(lhs, rhs);
        }

        private static IExpression ExpandExpression(IExpression expr)
        {
            if (expr is Multiply mul)
            {
                // Check if any arg is Add
                var addArg = mul.Arguments.FirstOrDefault(a => a is Add) as Add;
                if (addArg != null)
                {
                    var others = mul.Arguments.Remove(addArg);
                    var factor = others.Count == 1 ? others[0] : new Multiply(others).Canonicalize();
                    
                    var newTerms = addArg.Arguments.Select(term => new Multiply(term, factor).Canonicalize()).ToImmutableList();
                    return new Add(newTerms).Canonicalize();
                }
            }
            return expr;
        }

        /// <summary>
        /// Checks if the equation is in the desired solved form: TargetVariable = Solution (where solution does not contain TargetVariable).
        /// </summary>
        private static bool CheckGoal(Equality equation, Symbol targetVariable)
        {
            // Case 1: LeftOperand is target, RightOperand does not contain target
            if (equation.LeftOperand.InternalEquals(targetVariable) && !equation.RightOperand.ContainsSymbol(targetVariable))
            {
                return true;
            }
            // Case 2: RightOperand is target, LeftOperand does not contain target
            if (equation.RightOperand.InternalEquals(targetVariable) && !equation.LeftOperand.ContainsSymbol(targetVariable))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempts to perform one step of isolation on the 'targetSide' expression, assuming it contains the target variable
        /// and 'otherSide' does not. It applies the inverse of the outermost operation on 'targetSide'.
        /// </summary>
        /// <param name="targetSideExpression">The expression on the side of the equation containing the target variable.</param>
        /// <param name="otherSideExpression">The expression on the other side of the equation (assumed not to contain the target variable).</param>
        /// <param name="targetVariable">The symbol being isolated.</param>
        /// <param name="changed">Outputs true if an isolation step was performed, false otherwise.</param>
        /// <returns>A new Equality expression after an isolation step (target_expr = other_expr), or the original (target_expr = other_expr) if no step was taken.</returns>
        private static Equality IsolateSide(IExpression targetSideExpression, IExpression otherSideExpression, Symbol targetVariable, out bool changed)
        {
            changed = false;
            IExpression newTargetSide = targetSideExpression;
            IExpression newOtherSide = otherSideExpression;

            // Ordered by PEMDAS-reverse to peel outermost operations first (Add/Subtract, Multiply/Divide, Power, Function)

            // Isolation for Addition/Subtraction (A + B = C => A = C - B)
            if (newTargetSide is Add addOp)
            {
                IExpression? termWithTarget = null;
                IExpression? otherTermsSum = null;
                foreach (IExpression arg in addOp.Arguments)
                {
                    if (arg.ContainsSymbol(targetVariable))
                    {
                        // Multiple terms containing the target variable are not handled by simple isolation - keep the whole sum
                        if (termWithTarget is not null && !termWithTarget.InternalEquals(addOp))
                        {
                            termWithTarget = addOp; // Mark the whole operation as the 'term with target' if multiple exist.
                            break;
                        }
                        termWithTarget = arg;
                    }
                    else
                    {
                        if (otherTermsSum is null) { otherTermsSum = arg; }
                        else { otherTermsSum = new Add(otherTermsSum, arg).Canonicalize(); }
                    }
                }

                if (termWithTarget is not null && !termWithTarget.InternalEquals(addOp) && otherTermsSum is not null)
                {
                    newTargetSide = termWithTarget;
                    // Move otherTermsSum to the other side by subtracting it
                    newOtherSide = new Add(otherSideExpression.Canonicalize(), new Multiply(new Number(-1m), otherTermsSum.Canonicalize()).Canonicalize()).Canonicalize();
                    changed = true;
                }
                else
                {
                    // Check for denominators containing target (e.g. x + 1/x = C)
                    // If found, multiply entire equation by target variable to clear denominator.
                    // This transforms rational equations into polynomials (potentially).
                    
                    bool hasInverseTarget = false;
                    foreach (var arg in addOp.Arguments)
                    {
                        // Check for x^-1 or k*x^-1
                        if (arg is Power p && p.Exponent is Number en && en.Value < 0 && p.Base.InternalEquals(targetVariable))
                        {
                            hasInverseTarget = true;
                            break;
                        }
                        if (arg is Multiply mul)
                        {
                            foreach (var marg in mul.Arguments)
                            {
                                if (marg is Power mp && mp.Exponent is Number men && men.Value < 0 && mp.Base.InternalEquals(targetVariable))
                                {
                                    hasInverseTarget = true;
                                    break;
                                }
                            }
                            if (hasInverseTarget) break;
                        }
                    }
                    
                    if (hasInverseTarget)
                    {
                        // Multiply both sides by targetVariable
                        newTargetSide = ExpandExpression(new Multiply(newTargetSide, targetVariable).Canonicalize());
                        newOtherSide = ExpandExpression(new Multiply(newOtherSide, targetVariable).Canonicalize());
                        changed = true;
                    }
                }
            }
            // Isolation for Multiplication/Division (A * B = C => A = C / B)
            else if (newTargetSide is Multiply multiplyOp)
            {
                IExpression? factorWithTarget = null;
                IExpression? otherFactorsProduct = null;
                var targetFactors = multiplyOp.Arguments.Where(arg => arg.ContainsSymbol(targetVariable)).ToList();

                if (targetFactors.Count == 1)
                {
                    factorWithTarget = targetFactors[0];
                    var others = multiplyOp.Arguments.Remove(factorWithTarget);
                    otherFactorsProduct = others.Count == 1 ? others[0] : new Multiply(others).Canonicalize();
                }
                else if (targetFactors.Count > 1)
                {
                    // HEURISTIC: If we have multiple target factors, and one is a 'simple' power or exponential, 
                    // try moving it to the other side anyway. This enables cancellation in equations like 2^n * (n-1) = 2^(n+10).
                    // We prioritize Power/Function terms that are easier to 'invert' or cancel on the RHS.
                    IExpression? simplePower = targetFactors.OfType<Power>().FirstOrDefault(p => !p.Base.ContainsSymbol(targetVariable));
                    if (simplePower == null)
                    {
                        simplePower = targetFactors.OfType<Function>().FirstOrDefault(f => f.Name.Equals("exp", StringComparison.OrdinalIgnoreCase));
                    }

                    if (simplePower != null)
                    {
                        factorWithTarget = multiplyOp.WithArguments(multiplyOp.Arguments.Remove(simplePower)).Canonicalize();
                        otherFactorsProduct = simplePower;
                    }
                }

                if (factorWithTarget is not null && otherFactorsProduct is not null)
                {
                    newTargetSide = factorWithTarget;
                    // Move otherFactorsProduct to the other side by dividing by it
                    if (otherFactorsProduct is Number factorNum && factorNum.Value == 0m)
                    {
                        changed = false; // Cannot divide by zero
                        return new Equality(targetSideExpression, otherSideExpression);
                    }
                    IExpression inverseFactor = new Power(otherFactorsProduct.Canonicalize(), new Number(-1m)).Canonicalize();
                    newOtherSide = new Multiply(otherSideExpression.Canonicalize(), inverseFactor).Canonicalize();
                    changed = true;
                }
                else
                {
                    // Check for common exponent distributed across factors (e.g. A^n * B^n = K -> (AB)^n = K)
                    // Useful for sqrt(A)*sqrt(B) = K where both A and B contain target.
                    IExpression? commonExponent = null;
                    var bases = new System.Collections.Generic.List<IExpression>();
                    bool allMatch = true;
                    
                    foreach (var arg in multiplyOp.Arguments)
                    {
                        if (arg.ContainsSymbol(targetVariable))
                        {
                            if (arg is Power p)
                            {
                                if (commonExponent is null) commonExponent = p.Exponent;
                                else if (!commonExponent.InternalEquals(p.Exponent))
                                {
                                    allMatch = false;
                                    break;
                                }
                                bases.Add(p.Base);
                            }
                            else
                            {
                                allMatch = false; // Non-power term contains target
                                break;
                            }
                        }
                    }
                    
                    if (allMatch && commonExponent is not null && bases.Count > 1)
                    {
                        // Found factors like A^n * B^n ... with target.
                        // Isolate (A*B...)^n = K / (non-target-factors)
                        // Then A*B... = (K / non-target-factors)^(1/n)
                        
                        IExpression nonTargetFactors = new Number(1m);
                        foreach (var arg in multiplyOp.Arguments)
                        {
                            if (!arg.ContainsSymbol(targetVariable))
                            {
                                nonTargetFactors = new Multiply(nonTargetFactors, arg).Canonicalize();
                            }
                        }
                        
                        if (nonTargetFactors is Number ntf && ntf.Value == 0m)
                        {
                             // 0 = K case, handled elsewhere or impossible here
                        }
                        else
                        {
                            var rhs = otherSideExpression.Canonicalize();
                            var invNonTarget = new Power(nonTargetFactors, new Number(-1m)).Canonicalize();
                            var rhsIsolated = new Multiply(rhs, invNonTarget).Canonicalize();
                            
                            var newBase = new Multiply(bases.ToImmutableList()).Canonicalize();
                            var invExponent = new Power(commonExponent, new Number(-1m)).Canonicalize();
                            
                            newTargetSide = newBase;
                            newOtherSide = new Power(rhsIsolated, invExponent).Canonicalize();
                            changed = true;
                        }
                    }
                    else if (otherSideExpression is Number numZero && numZero.Value == 0m)
                    {
                        // Special case: Product = 0.
                        // If we have A * B^-1 * C ... = 0.
                        // We can discard B^-1 (denominators) assuming valid domain.
                        // Effectively A * C = 0.
                        
                        var numerators = new System.Collections.Generic.List<IExpression>();
                        bool droppedDenominator = false;
                        
                        foreach (var arg in multiplyOp.Arguments)
                        {
                            if (arg is Power p && p.Exponent is Number en && en.Value < 0)
                            {
                                // Drop denominator
                                droppedDenominator = true;
                            }
                            else
                            {
                                numerators.Add(arg);
                            }
                        }
                        
                        if (droppedDenominator)
                        {
                            if (numerators.Count == 0) 
                            {
                                // 1/x = 0 -> 1 = 0
                                newTargetSide = new Number(1m);
                            }
                            else if (numerators.Count == 1)
                            {
                                newTargetSide = numerators[0];
                            }
                            else
                            {
                                newTargetSide = new Multiply(numerators.ToImmutableList()).Canonicalize();
                            }
                            changed = true;
                        }
                    }
                }
            }
            // Isolation for Power operation (A^B = C)
            else if (newTargetSide is Power powerOp)
            {
                IExpression @base = powerOp.Base;
                IExpression exponent = powerOp.Exponent;

                if (@base.ContainsSymbol(targetVariable) && !exponent.ContainsSymbol(targetVariable))
                {
                    // Case: Base contains target (X^N = Y => X = Y^(1/N))
                    newTargetSide = @base;
                    if (exponent is Number expNum && expNum.Value == 0m)
                    {
                        changed = false; // Cannot take 1/0 exponent
                        return new Equality(targetSideExpression, otherSideExpression);
                    }
                    IExpression inverseExponent = new Power(exponent.Canonicalize(), new Number(-1m)).Canonicalize();
                    newOtherSide = new Power(otherSideExpression.Canonicalize(), inverseExponent).Canonicalize();
                    changed = true;
                }
                else if (exponent.ContainsSymbol(targetVariable) && !@base.ContainsSymbol(targetVariable))
                {
                    // Case: Exponent contains target (N^X = Y => X = Log(N, Y))
                    newTargetSide = exponent;
                    newOtherSide = new Function("log", ImmutableList.Create(@base.Canonicalize(), otherSideExpression.Canonicalize())).Canonicalize();
                    changed = true;
                }
            }
            // Isolation for Function operation (fun(X) = Y)
            else if (newTargetSide is Function funcOp)
            {
                string funcName = funcOp.Name.ToLowerInvariant();

                if (funcOp.Arguments.Count == 1 && funcOp.Arguments[0].ContainsSymbol(targetVariable))
                {
                    string inverseFuncName = string.Empty;
                    bool handledBySpecialCase = false;

                    // Standard trigonometric and exponential function inverses
                    if (funcName == "sin") inverseFuncName = "asin";
                    else if (funcName == "cos") inverseFuncName = "acos";
                    else if (funcName == "tan") inverseFuncName = "atan";
                    else if (funcName == "asin") inverseFuncName = "sin";
                    else if (funcName == "acos") inverseFuncName = "cos";
                    else if (funcName == "atan") inverseFuncName = "tan";
                    else if (funcName == "sinh") inverseFuncName = "asinh";
                    else if (funcName == "cosh") inverseFuncName = "acosh";
                    else if (funcName == "tanh") inverseFuncName = "atanh";
                    else if (funcName == "asinh") inverseFuncName = "sinh";
                    else if (funcName == "acosh") inverseFuncName = "cosh";
                    else if (funcName == "atanh") inverseFuncName = "tanh";
                    else if (funcName == "exp") inverseFuncName = "log"; // Natural logarithm
                    else if (funcName == "sqrt")
                    {
                        newTargetSide = funcOp.Arguments[0];
                        newOtherSide = new Power(otherSideExpression.Canonicalize(), new Number(2m)).Canonicalize();
                        changed = true;
                        handledBySpecialCase = true;
                    }
                    else if (funcName == "log") // Assuming natural log
                    {
                        // For log(A) = B => A = exp(B)
                        newTargetSide = funcOp.Arguments[0];
                        newOtherSide = new Function("exp", ImmutableList.Create(otherSideExpression.Canonicalize())).Canonicalize();
                        changed = true;
                        handledBySpecialCase = true;
                    }

                    if (changed == false && handledBySpecialCase == false && !string.IsNullOrEmpty(inverseFuncName)) // Only if not handled by a special case above
                    {
                        newTargetSide = funcOp.Arguments[0];
                        newOtherSide = new Function(inverseFuncName, ImmutableList.Create(otherSideExpression.Canonicalize())).Canonicalize();
                        changed = true;
                    }
                }
                else if (funcName == "log" && funcOp.Arguments.Count == 2) // Log(base, value) case
                {
                    // If Log(base without target, value with target) = result => value = base^result
                    if (!funcOp.Arguments[0].ContainsSymbol(targetVariable) && funcOp.Arguments[1].ContainsSymbol(targetVariable))
                    {
                        newTargetSide = funcOp.Arguments[1];
                        newOtherSide = new Power(funcOp.Arguments[0].Canonicalize(), otherSideExpression.Canonicalize()).Canonicalize();
                        changed = true;
                    }
                    // If Log(base with target, value without target) = result => value = base^result => base = value^(1/result)
                    else if (funcOp.Arguments[0].ContainsSymbol(targetVariable) && !funcOp.Arguments[1].ContainsSymbol(targetVariable))
                    {
                        newTargetSide = funcOp.Arguments[0];
                        newOtherSide = new Power(funcOp.Arguments[1].Canonicalize(), new Power(otherSideExpression.Canonicalize(), new Number(-1m)).Canonicalize()).Canonicalize();
                        changed = true;
                    }
                }
            }


            // If no specific isolation rule applied, or if the target is already `targetVariable`
            if (!changed)
            {
                // No isolation performed, return the original equation
                return new Equality(targetSideExpression, otherSideExpression);
            }

            // Return the new equation (target_variable_isolated = other_side_transformed_and_canonicalized)
            return new Equality(newTargetSide, newOtherSide);
        }

        private static bool TrySolveSimpleLinear(Equality equation, Symbol targetVariable, out Equality solved, out string? message)
        {
            solved = equation;
            message = null;

            if (!TryExtractLinear(equation.LeftOperand, targetVariable, out var a, out var b))
            {
                message = "Left side is not a simple linear form.";
                return false;
            }

            if (!TryExtractLinear(equation.RightOperand, targetVariable, out var c, out var d))
            {
                message = "Right side is not a simple linear form.";
                return false;
            }

            var denom = a - c;
            if (denom == 0m)
            {
                message = "Coefficient difference is zero; cannot isolate target.";
                return false;
            }

            var value = (d - b) / denom;
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: TrySolveSimpleLinear value = {value} (d={d}, b={b}, denom={denom})");
            solved = new Equality(targetVariable, new Number(value)).Canonicalize() as Equality ?? new Equality(targetVariable, new Number(value));
            return true;
        }

        private static bool TryExtractLinear(IExpression expr, Symbol targetVariable, out decimal coefficient, out decimal constant)
        {
            coefficient = 0m;
            constant = 0m;
            var coeffs = new decimal[1];
            if (ExpressionHelpers.TryExtractLinearStruct(expr, new[] { targetVariable }, ref coeffs, ref constant))
            {
                coefficient = coeffs[0];
                return true;
            }
            return false;
        }

        private static Symbol? FindFirstSymbol(IExpression expression)
        {
            if (expression is Symbol s) return s;
            if (expression is Operation op)
            {
                foreach (var arg in op.Arguments)
                {
                    var found = FindFirstSymbol(arg);
                    if (found is not null) return found;
                }
            }
            return null;
        }

        private static IExpression? TrySolvePolynomial(Equality equation, Symbol target, System.Threading.CancellationToken ct = default)
        {
            var diff = new Subtract(equation.LeftOperand, equation.RightOperand).Canonicalize();
            if (!Polynomial.TryCreate(diff, target, out var poly))
            {
                return null;
            }

            if (poly.IsZero)
            {
                return null; // Tautology
            }

            if (poly.Degree == 0)
            {
                return null; // Contradiction or no target variable
            }

            if (poly.Degree == 1)
            {
                // ax + b = 0 => x = -b/a
                var a = poly.Coefficients[1];
                var b = poly.Coefficients[0];
                var solution = (b.IsZero ? Rational.Zero : (Rational.FromInteger(-1) * b / a)).ToExpression();
                return new Equality(target, solution).Canonicalize();
            }

            if (poly.Degree == 2)
            {
                // Quadratic: ax^2 + bx + c = 0 => x = (-b +/- Sqrt(b^2 - 4ac)) / (2a)
                var a = poly.Coefficients[2];
                var b = poly.Coefficients[1];
                var c = poly.Coefficients[0];

                var disc = b * b - Rational.FromInteger(4) * a * c;
                if (disc.IsZero)
                {
                    var root = (Rational.FromInteger(-1) * b / (Rational.FromInteger(2) * a)).ToExpression();
                    return new Equality(target, root).Canonicalize();
                }

                if (disc.Numerator > 0 && disc.TrySqrt(out var sqrtDisc))
                {
                    var r1 = (Rational.FromInteger(-1) * b + sqrtDisc) / (Rational.FromInteger(2) * a);
                    var r2 = (Rational.FromInteger(-1) * b - sqrtDisc) / (Rational.FromInteger(2) * a);
                    
                    var solutions = ImmutableList.Create<IExpression>(
                        new Equality(target, r1.ToExpression()).Canonicalize(),
                        new Equality(target, r2.ToExpression()).Canonicalize()
                    );
                    return new Vector(solutions).Canonicalize();
                }
                
                // Fallback to symbolic sqrt if disc is not a perfect square
                var discExpr = disc.ToExpression();
                var sqrtDiscExpr = new Power(discExpr, new Number(0.5m)).Canonicalize();
                var twoAExpr = (Rational.FromInteger(2) * a).ToExpression();
                var negBExpr = (Rational.FromInteger(-1) * b).ToExpression();

                var sol1 = new Multiply(new Add(negBExpr, sqrtDiscExpr), new Power(twoAExpr, new Number(-1m))).Canonicalize();
                var sol2 = new Multiply(new Subtract(negBExpr, sqrtDiscExpr), new Power(twoAExpr, new Number(-1m))).Canonicalize();

                return new Vector(new Equality(target, sol1).Canonicalize(), new Equality(target, sol2).Canonicalize()).Canonicalize();
            }

            // For degree > 2, try rational root theorem via FactorLinear
            var factorization = poly.FactorLinear(ct);
            if (factorization.LinearRoots.Count > 0)
            {
                var uniqueRoots = factorization.LinearRoots.Distinct().ToList();
                if (uniqueRoots.Count == 1 && factorization.Residual.Degree == 0)
                {
                    return new Equality(target, uniqueRoots[0].ToExpression()).Canonicalize();
                }

                var solList = new List<IExpression>();
                foreach (var root in uniqueRoots)
                {
                    solList.Add(new Equality(target, root.ToExpression()).Canonicalize());
                }

                if (factorization.Residual.Degree > 0)
                {
                    // For now, we don't have a good way to represent roots of higher degree irreducibles symbolically
                    // just return the roots we found.
                }

                return solList.Count == 1 ? solList[0] : new Vector(solList.ToImmutableList()).Canonicalize();
            }

            return null;
        }

        private static bool IsZero(IExpression expr)
        {
            if (expr is Number n && n.Value == 0m) return true;
            return false;
        }

        // Keep this for now as a fallback or reference, but Polynomial.TryCreate is more robust.
        private static bool TryExtractQuadraticSymbolic(IExpression expr, Symbol target, out IExpression a, out IExpression b, out IExpression c)
        {
            a = new Number(0m); b = new Number(0m); c = new Number(0m);
            return false; // Deprecated by Polynomial.TryCreate in this strategy
        }
    }
}
