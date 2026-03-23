# Library JavaScript Tools And Demos

## Purpose

This file is the maintained shortlist of JavaScript-enabled tools, visualizers, and demos that could strengthen the first version of the library.

The goal is not to add interaction everywhere. The goal is to choose tools that make difficult topics easier to see, test, or compare.

## Guiding principles

- prefer tools that reveal structure, not just calculators that produce a number
- prioritize topics already present in the library
- favor demos that help both beginners and technical readers
- choose tools that can stay useful over time
- keep the first wave small and strong rather than broad and thin

## Best first-wave candidates

These are the strongest candidates for the first version of the library.

### 1. Expression Tree Visualizer

Why it is strong:

- directly supports symbolic computation pages
- shows readers how formulas become structured objects
- helps bridge notation and implementation

Possible features:

- input math expression
- render tree or DAG view
- highlight operators and arguments
- toggle between infix text and tree structure
- step through simple rewrites

Primary supporting pages:

- `Library/symbolic-computation/what-is-symbolic-computation.html`
- `Library/symbolic-computation/term-rewriting-systems.html`

### 2. Symbolic Rewrite Demo

Why it is strong:

- makes rewriting concrete
- helps readers understand local rule application
- fits term rewriting and e-graph material naturally

Possible features:

- choose a sample expression
- choose a rule pack or specific rewrite rule
- show before and after expressions
- optionally show multiple valid rewrites
- explain why a rule applies

Primary supporting pages:

- `Library/symbolic-computation/term-rewriting-systems.html`
- `Library/symbolic-computation/e-graphs.html`

### 3. E-Graph Simplification Demo

Why it is strong:

- directly supports one of the site’s distinctive topics
- makes a hard concept easier to understand
- fits Sym particularly well

Possible features:

- load a sample expression
- apply multiple rewrites
- show equivalent forms collected together
- extract a preferred result using a simple cost model
- visualize equivalence classes at a high level

Primary supporting pages:

- `Library/symbolic-computation/e-graphs.html`

### 4. Jacobian Calculator And Explainer

Why it is strong:

- immediately useful
- bridges advanced math and symbolic tooling
- can be educational rather than only computational

Possible features:

- accept scalar or vector-valued functions
- compute the Jacobian symbolically for selected examples
- show each partial derivative entry
- optionally evaluate at a point
- explain local linear interpretation

Primary supporting pages:

- `Library/advanced-math/jacobian-matrix.html`

### 5. Tensor Expression Graph Demo

Why it is strong:

- supports the AI + symbolic computation angle
- demonstrates why tensor expressions can be optimized symbolically
- aligns with Sym’s tensor-related capabilities

Possible features:

- choose a tensor expression like `Relu(TensorAdd(MatMul(A, B), C))`
- view operator graph
- apply a fusion rewrite
- compare original and fused forms
- display simple cost comparison or shape hints

Primary supporting pages:

- `Library/ai-math/tensor-expression-graphs.html`
- `Library/advanced-math/tensor-contraction.html`

## Strong second-wave candidates

These look promising, but are probably better after the first wave is stable.

### Symbolic Derivative Explorer

- enter or select an expression
- show derivative rules used
- step through chain rule and product rule applications

### Tensor Index Notation Visualizer

- highlight free vs contracted indices
- animate contraction steps
- connect matrix multiplication to tensor notation

### Canonical Form Comparison Demo

- show several equivalent expressions
- compare printed size, structure, or other costs
- explain why one form is preferable in a given context

### Pattern Matching Sandbox

- show how variables match parts of an expression
- explain successful vs failed matches
- support rewrite-rule intuition

### Expression Complexity Comparator

- compare node count
- compare operator counts
- compare simple cost heuristics across equivalent forms

## Possible page types for JavaScript-enabled content

Not every interactive page needs to live under a single `/tools/` folder forever. There are three reasonable patterns:

### Pattern A: Dedicated tools pages

Examples:

- `/tools/expression-tree-visualizer/`
- `/tools/jacobian-calculator/`

Pros:

- clear separation
- easy to promote as tools
- easier to expand later

### Pattern B: Article plus companion demo

Examples:

- article page explains the concept
- companion tool page linked as “Open interactive demo”

Pros:

- keeps educational reading and interaction both strong
- helps readers move from explanation to experimentation

### Pattern C: Embedded demos inside cornerstone pages

Pros:

- immediate engagement
- tighter reading flow

Tradeoff:

- more page complexity
- harder to keep pages lightweight

Current recommendation:

- first version should prefer article pages plus companion demo pages
- keep the educational pages readable
- link out to the interactive layer instead of overloading every article

## First version recommendation

For the first version of the library, the clearest interactive vision is:

1. Expression Tree Visualizer
2. Symbolic Rewrite Demo
3. Jacobian Calculator And Explainer
4. Tensor Expression Graph Demo

Optional fifth if time allows:

5. E-Graph Simplification Demo

## Open questions before implementation

- should the tools use raw JavaScript only, or allow a lightweight visualization library for graphs
- should input parsing be powered by Sym endpoints later, or remain static/demo-oriented at first
- should the first tools be embedded companions or separate pages
- how much step-by-step explanation should each tool include

## Immediate use

Before building any tools, this list should be reviewed and narrowed to the exact first set.
