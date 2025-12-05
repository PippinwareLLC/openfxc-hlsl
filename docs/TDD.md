# **OpenFXC – HLSL Front-End TDD (Lexer + Parser, Full SM1–SM5 Coverage)**

This document defines the **Test-Driven Development specification** for the **`openfxc-hlsl`** component of OpenFXC, covering a complete HLSL lexer and parser with full syntax compatibility for **FXC Shader Model 1–5 era HLSL**.

This TDD defines:

* **Scope & goals**
* **CLI behavior**
* **JSON output schemas**
* **Unit / snapshot test expectations**
* **Syntax coverage matrix**
* **Error recovery requirements**
* **Definition of done (DoD)**

All passes after this stage (semantics, IR, lowering, DX9 backend, DXBC emission) depend on the guarantees defined here.

---

# 0. Goals

The `openfxc-hlsl` component shall:

1. Provide a **standalone, reusable HLSL front-end**:

   * C# library (`OpenFXC.Hlsl`)
   * CLI tool (`openfxc-hlsl`)
2. Correctly lex and parse **all HLSL syntax accepted by Microsoft FXC** for:

   * Shader Model **1.0, 1.1, 1.4**
   * Shader Model **2.0, 2.a, 2.b**
   * Shader Model **3.0**
   * Shader Model **4.0 / 4.1**
   * Shader Model **5.0**
3. Produce a **complete syntax tree (AST)** that is:

   * Deterministic
   * Structured
   * Lossless enough for round-trip HLSL reconstruction
4. Never crash on invalid input:

   * Always emit at least a `CompilationUnit` node
   * Emit diagnostics for parse errors
5. Remain strictly **syntax-only**:

   * No semantic analysis
   * No shader model legality
   * No IR generation

---

# 1. CLI Specification

## 1.1 Commands

### Lex

```bash
openfxc-hlsl lex input.hlsl --format json > tokens.json
```

### Parse

```bash
openfxc-hlsl parse input.hlsl --format json > ast.json
```

Tools must accept:

* `-i <file>` or piped input
* `-o <file>` or stdout output

## 1.2 Exit Codes

| Code | Meaning                                    |
| ---- | ------------------------------------------ |
| 0    | Successful execution (diagnostics allowed) |
| 1    | Internal error / exception                 |

---

# 2. Output Schema

## 2.1 Token JSON Schema (lex output)

```json
{
  "formatVersion": 1,
  "source": { "fileName": "shader.hlsl", "length": 1234 },
  "tokens": [
    {
      "kind": "KeywordFloat4",
      "text": "float4",
      "span": { "start": 0, "end": 6 },
      "leadingTrivia": [],
      "trailingTrivia": []
    }
  ],
  "diagnostics": []
}
```

## 2.2 AST JSON Schema (parse output)

```json
{
  "formatVersion": 1,
  "source": { "fileName": "shader.hlsl", "length": 1234 },
  "root": {
    "id": 1,
    "kind": "CompilationUnit",
    "span": { "start": 0, "end": 96 },
    "children": [
      { "role": "declaration", "node": { "$ref": 2 } }
    ]
  },
  "tokens": [ /* same token list as lex */ ],
  "diagnostics": [ /* parser diagnostics */ ]
}
```

---

# 3. Testing Strategy

## 3.1 Types of tests

* **Unit tests** – validate individual constructs
* **Snapshot tests** – round-trip AST output for complex files
* **Negative tests** – malformed input, error recovery
* **Era tests** – SM1, SM2/3, SM4/5 syntax sets
* **Large integration tests** – real HLSL files from:

  * D3D9 SDK samples
  * D3D10/11 samples
  * Modern HLSL examples

---

# 4. Lexer TDD

## 4.1 Fundamental Requirements

For any input:

* Tokens must appear in source order.
* Spans must satisfy:

  ```
  0 <= span.start <= span.end <= source.length
  ```
* Leading/trailing trivia must capture whitespace & comments.
* Diagnostics must be present for lex errors (invalid numerics, unterminated block comments).

## 4.2 Lexical Coverage Matrix

Each of the following must have **at least one positive and one negative test**:

### 4.2.1 Whitespace & newlines

* ` `, `\t`, `\r`, `\n`, `\r\n`

### 4.2.2 Comments

* Single-line: `// comment`
* Multi-line: `/* comment */`
* Unterminated: `/* ...` ⇒ Diagnostics required

### 4.2.3 Identifiers & keywords

Must lex all FXC-era keywords, including:

* Types: `float`, `float4`, `half`, `int`, `uint`, `double`
* Flow: `if`, `else`, `for`, `while`, `do`, `break`, `continue`, `return`, `discard`
* Storage: `static`, `extern`, `uniform`, `const`, `volatile`
* SM2/3 texture/sampler: `sampler`, `sampler2D`, `texture`
* SM4/5: `cbuffer`, `tbuffer`, `StructuredBuffer`, `RWTexture2D`, etc.

### 4.2.4 Literals

* Decimal integers
* Hex integers
* Floating literals with exponent, suffixes (`f`, `F`)
* Invalid tokens must yield diagnostics

### 4.2.5 Operators & punctuation

All of:

```
+ - * / % ++ --
&& || !
& | ^ ~
<< >>
== != < <= > >=
= += -= *= /= %= &= |= ^= <<= >>=
? : ::
( ) [ ] { } , ; .
```

### 4.2.6 Preprocessor tokens

* `#define`, `#if`, `#elif`, `#else`, `#endif`, `#include`, `#pragma`, `#error`, `#line`
* Entire line after directive lexes as trivia.

---

# 5. Parser TDD

## 5.1 General Requirements

* Always emit a `CompilationUnit` node
* All nodes have:

  * `kind`
  * `span`
  * `children`
* Parser must **recover** from errors and continue producing a tree
* Diagnostics always specify:

  * stable diagnostic ID (`HLSL10xx`)
  * span
  * message

## 5.2 Syntax Categories & Test Requirements

For full SM1–SM5 syntax compatibility, create tests for each major syntax category:

### 5.2.1 Types & declarations

* Scalar, vector, matrix types (`float4x4`)
* Structs
* Typedefs
* Arrays, e.g. `float4 data[16];`
* Resource declarations (SM4/5):

  * `Texture2D<float4>`
  * `RWStructuredBuffer<int>`
  * `ByteAddressBuffer`

### 5.2.2 Functions & parameters

* Simple functions
* Functions with return semantics
* Parameters with semantics (`POSITION0`)
* Overloaded parameter lists
* Default values (parse, semantic rules come later)

### 5.2.3 Expressions

* Unary, binary, assignment expressions
* Swizzles (`pos.xyzw`)
* Index expressions (`arr[i]`)
* Calls (`mul(a, b)`)

### 5.2.4 Statements

* `if`, `if/else`
* `for`, `while`, `do/while`
* `return`
* `break`, `continue`
* `discard`
* Statement blocks & nested scopes

### 5.2.5 Semantics & registers

* Parse forms:

  * `: POSITION`
  * `: POSITION0`
  * `: SV_Position`
* Register forms:

  ```
  : register(c0)
  : register(s1)
  : register(t2)
  ```

### 5.2.6 Sampler & texture syntax (legacy SM1–SM3)

* Sampler declarations
* Sampler state blocks:

  ```
  sampler_state { MinFilter = Linear; }
  ```

### 5.2.7 SM4/5 blocks

* `cbuffer` / `tbuffer`
* Interface & class declarations
* Method declarations

### 5.2.8 FX Framework constructs

* `technique`, `technique10`
* `pass`
* `CompileShader(vs_2_0, MainVS())`
* `SetPixelShader(...)`

These must **parse**, even if semantics will later reject them.

---

# 6. Error Recovery TDD

For each major syntax form:

* Introduce malformed input (missing semicolon, missing brace, bad token)
* Parser must:

  * Produce a diagnostic error
  * Skip to the next viable parse point
  * Continue emitting valid nodes after error

Examples:

### Missing semicolon

```hlsl
float4 main(float4 pos : POSITION) : POSITION
{
    float x = 1
    return pos;
}
```

Test:

* Diagnostic: `Expected ';'`
* AST still contains both statements
* Parser resumes after the invalid line

### Missing brace

```hlsl
float4 main() :
{
    return 1;
```

Test:

* Diagnostic: “Expected ‘}’”
* Root still constructed
* Return statement parsed

---

# 7. Era-Based Coverage

Add **integration test shaders** for representative eras:

### SM1.x era

* Fixed function replacement shaders
* Sampler/texture blocks

### SM2/SM3 era

* D3D9 samples with:

  * Intrinsics (tex2D, normalize, saturate)
  * Sampler states
  * Semantic-heavy interfaces (POSITION0, TEXCOORD*)

### SM4/SM5 era

* cbuffers, SV semantics, structured buffers, RW resources

### FX Framework .fx files

* Techniques, passes, CompileShader

Each must produce a tree and no internal parser failures.

---

# 8. Definition of Done

OpenFXC-HLSL parser is considered **complete for SM1–SM5 syntax** when:

1. Lexer:

   * 100% keyword/operator coverage
   * All numeric literal forms lexed correctly
   * Preprocessor lines tokenized without parse disruption

2. Parser:

   * All syntax categories in §5.2 have:

     * At least one positive test
     * Optional negative tests
     * Snapshot test for complex forms
   * Error recovery works for:

     * Missing semicolon
     * Missing brace
     * Unexpected tokens
   * No known HLSL sample fails to produce a syntax tree

3. Integration:

   * A curated corpus of HLSL shaders from SM1–SM5 parses without internal errors
   * Diagnostics appear only for intentionally malformed tests

4. Stability:

   * AST JSON shape is deterministic
   * Tests pin the shape and prevent regressions
   * CLI remains stable and scriptable

---

# 9. Future Extensions (Not Required for DoD)

* Preprocessor evaluation (`#define`, macro expansion)
* Include resolution
* AST to canonical HLSL emitter
* Language server protocol (LSP) integration
* Recoverable AST rewriter for IDE tooling

