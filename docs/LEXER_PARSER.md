# Lexer and Parser Architecture

This document summarizes how the HLSL front-end is structured so contributors can confidently evolve the lexer and parser while preserving determinism and FXC-era behavior.

## Pipeline Overview
- A lightweight preprocessor runs first to expand `#define`/`#include`/`#if` directives (deterministic, no stringizing/`##`, missing includes become diagnostics). Include search order matches `-I` paths with quoted includes preferring the current file's directory.
- Input text is tokenized by `HlslLexer.Lex`, producing ordered tokens with spans/trivia and lex diagnostics.
- The parser (`Parser.Parse`) consumes the token stream to build an AST rooted at `CompilationUnit`, emitting parse diagnostics but always returning a tree.
- CLI entrypoints (`openfxc-hlsl lex|parse`) wrap the results into JSON per `docs/TDD.md` with `formatVersion: 1`.

## Lexer Notes
- Stateless pass over UTF-16 text; spans are byte-offset-free and satisfy `0 <= start <= end <= length`.
- Trivia: leading/trailing whitespace/newlines/comments are preserved on tokens; preprocessor lines are emitted as `PreprocessorDirective` with trailing text kept in trivia.
- Keywords vs identifiers: era-specific keywords (sampler/texture/class/interface/technique/technique10/technique11/pass/CompileShader/compile_fragment/Set*Shader, resource types) are recognized case-insensitively; mixed-case tokens that would be keywords (e.g., `Sampler`, `Half`) are preserved as identifiers for FXC compatibility; semantics/register forms (`POSITION0`, `TEXCOORD*`, `register`) remain identifiers for syntax-only handling.
- Numeric literals: decimal/hex/float forms are tokenized; diagnostics include `HLSL0001` (unexpected character) and `HLSL0002` (unterminated block comment).
- Determinism: token order and spans are stable; no macro expansion or semantic filtering is performed.

## Parser Notes
- Recursive-descent with light heuristics:
  - Function vs variable: looks for type + identifier + `(` to classify declarations; template angle brackets are consumed before the identifier where present.
  - Struct/class/interface bodies parse member variable declarations and signature-only methods; typedef supports aliasing types and struct-like inline bodies.
  - Resource blocks: `cbuffer`/`tbuffer` bodies are consumed as spans; `sampler_state` is parsed as a body with a terminating semicolon.
  - Annotations/semantics: colon-prefixed tokens are collected as `Annotation` children (semantics, register(...), packoffset, etc.) until a terminator (`;`, `=`, `,`, `{`).
  - FX constructs: `technique`/`technique10`/`technique11` with nested `pass` blocks parsed as syntax-only bodies; `CompileShader`/`compile_fragment`/`Set*Shader` remain call expressions; FX property assignments accept brace initializer expressions (`MaterialAmbient = {1,1,1,1};`).
- Expressions and statements:
  - Precedence-aware unary/binary parsing; postfix handles member, call, and index expressions.
  - Statements cover blocks, if/else, loops (for/while/do), return, break/continue, discard, and expression statements.
- Recovery and diagnostics:
  - Always emits `CompilationUnit`.
  - Diagnostics include `HLSL1001` (unexpected token), `HLSL1002` (expected delimiter like `{`, `;`), `HLSL1004` (missing `}`), while skipping to recovery points on errors.
  - Spans on AST nodes inherit start/end from constituent tokens to preserve ordering.

## Determinism and Snapshots
- Lex + parse snapshots are pinned per era (SM1, SM2/3, SM4, SM5, FX) to guard output stability (tokens, AST shape, diagnostics).
- The parser does not perform semantic checks; acceptance is syntax-only to mirror FXC tolerance across SM1–SM5.

## Testing Hooks
- Main suite: `dotnet test tests/OpenFXC.Hlsl.Tests/OpenFXC.Hlsl.Tests.csproj`.
- Quick runner: `tests/run-all.cmd` (Windows) or `tests/run-all.sh` (bash) to execute all unit/negative/snapshot/CLI smoke coverage.
