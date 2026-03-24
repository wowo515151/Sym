---
name: symcli-skill
description: Execute SymCLI to solve math equations, optimize tensor graphs, or analyze C# code for vulnerabilities. Use when you need a deterministic 'System 2' math engine to prevent hallucination.
---
# SymCLI Skill

SymCLI is a practical AI math tool that provides a deterministic "System 2" mathematical reasoning and C# analysis engine for autonomous agents.

<activated_skill>
<instructions>
You have activated the **SymCLI** skill. SymCLI is a pure C# symbolic computation framework designed to act as an exact mathematical engine and code analyzer.

When given a mathematical task, equation to solve, tensor optimization problem, or C# code to analyze, you MUST use the SymCLI wrappers to execute the work rather than relying solely on your internal LLM reasoning, as SymCLI prevents hallucinations and provides provably correct results.

**Primary Workflows:**
1. **Solving ProblemScript (`.ps`):** Create a `.ps` file with equations/rules and use SymCLI to compute the exact answer.
2. **Analyzing C# Code:** Scan C# files for mathematical correctness hazards (`CSMATH...`) and security-oriented patterns (`CSSEC...`).

**Usage Guidelines:**
- **OS Compatibility:** Use `symcli.bat` on Windows or `symcli.sh` on Unix-like systems.
- **ProblemScript:** Wrap configuration in `<Options>...</Options>`. Include constraints like `x^2 + 2*x + 1 = 0` or rules like `Rule(a + a, 2 * a)`.
- **C# Analysis:** Provide a specific `.cs` file or a directory to analyze.

**Exit Codes:**
- `0`: Success
- `1`: Configuration/Argument Error
- `2`: Solving failed (diagnostics written)
- `3`: Unexpected runtime exception
- `4`: Findings present (if `--fail-on-findings` used)

**Agent Workflow:**
1. Interpret the user's mathematical/coding task.
2. Formulate the required input (e.g., write a `.ps` file).
3. Execute the appropriate `symcli` wrapper.
4. Read the output file and interpret the exact symbolic results back to the user.
</instructions>

<available_resources>
<resource>
  <name>Windows Wrapper</name>
  <path>Skills/symcli-skill/symcli.bat</path>
  <description>Batch script to execute SymCLI. Usage: `symcli.bat <input.ps> <output.txt>` or `symcli.bat analyze csharp-math <input> <output> [options]`</description>
</resource>
<resource>
  <name>Unix Wrapper</name>
  <path>Skills/symcli-skill/symcli.sh</path>
  <description>Shell script to execute SymCLI. Usage: `./symcli.sh <input.ps> <output.txt>` or `./symcli.sh analyze csharp-math <input> <output> [options]`</description>
</resource>
</available_resources>

<examples>
<example>
  <description>Solving an algebraic equation using ProblemScript</description>
  <steps>
    1. Agent writes `problem.ps` with content:
       ```xml
       <Options>
         Target: x
         RulePacks: Algebraic
       </Options>
       x^2 - 4 = 0
       ```
    2. Agent executes: `Skills/symcli-skill/symcli.bat problem.ps result.txt`
    3. Agent reads `result.txt` to find `x = 2, x = -2`.
  </steps>
</example>
<example>
  <description>Analyzing C# code for math vulnerabilities</description>
  <steps>
    1. Agent executes: `Skills/symcli-skill/symcli.bat analyze csharp-math src/MathCore/Calculator.cs report.json --json`
    2. Agent reads `report.json` to review any `CSMATH` or `CSSEC` findings.
  </steps>
</example>
</examples>
</activated_skill>