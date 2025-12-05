# TODO

## Foundation
- [x] Stand up the `openfxc-hlsl` CLI skeleton with `lex` and `parse` verbs that accept file/stdin and emit JSON per `docs/TDD.md`.
- [x] Enforce JSON schema conformance (`formatVersion: 1`, source metadata, tokens, diagnostics) for both lex and parse outputs.
- [ ] Stabilize diagnostic IDs/messages and ensure spans satisfy `0 <= start <= end <= length` on all tokens and nodes.
- [ ] Preserve trivia (whitespace/newlines/comments) and preprocessor lines as specified; ensure operator/punctuation coverage across eras.

## Shader Model 1.x (legacy D3D9 era)
- [x] Lexer: legacy sampler/texture keywords (`sampler`, `sampler1D/2D/3D/CUBE`, `texture`), basic numeric literals, directives.
- [x] Parser: sampler declarations, sampler_state blocks (`sampler_state { MinFilter = Linear; }`), fixed-function style constructs as syntax.
- [x] Tests: positive/negative lex cases for comments/whitespace/legacy keywords; snapshot of a representative SM1.x shader.

## Shader Model 2.x / 3.x
- [x] Lexer: flow/storage keywords, vector/matrix types, intrinsics in identifiers, preprocessor coverage, semantics/register tokens.
- [x] Parser: functions with parameter/return semantics (`POSITION0`, `TEXCOORD*`), arrays, structs/typedefs, expressions (swizzles, indexing, calls), statements (if/else, loops, discard).
- [x] Tests: unit/negative coverage for semantics/register forms, control flow, expression precedence; snapshot of an SM2/SM3 shader with intrinsics and sampler usage.

## Shader Model 4.x / 5.x
- [x] Lexer: SM4/5 resource keywords (`cbuffer`, `tbuffer`, `Texture2D/3D`, `StructuredBuffer`, `RWTexture*`, `RWBuffer`, `AppendStructuredBuffer`, `ConsumeStructuredBuffer`, `ByteAddressBuffer`), class/interface keywords.
- [x] Parser: resource templates (`Texture2D<float4>` etc.), `cbuffer`/`tbuffer` blocks with bindings, interfaces/classes/methods (syntax-only), resource binding/register syntax.
- [x] Tests: positive/negative coverage for resource declarations, binding syntax, class/interface shapes; snapshots for SM4 and SM5 shaders with buffers and RW resources.

## FX Framework Constructs (cross-era)
- [x] Lexer: `technique`, `technique10`, `pass`, directives inside FX blocks.
- [x] Parser: `technique`/`pass` blocks, `CompileShader(...)`, `SetPixelShader(...)` parsed as syntax without semantics.
- [x] Tests: FX construct lex coverage, snapshot of a representative .fx file, and parser coverage for technique/pass bodies.

## Parser Coverage (syntax-only)
- [x] Always emit `CompilationUnit` with deterministic child order and spans.
- [x] Types and declarations: scalars, vectors, matrices, resource templates; global variables with initializers.
- [x] Composite types: arrays, structs, typedefs, class/interface bodies.
- [x] Functions and parameters: parameter lists with semantics/register annotations (syntax-only), return semantics; overloadable signatures pending.
- [x] Expressions: precedence-aware unary/binary, calls, indexing, member access; assignment partially covered.
- [x] Statements: blocks, if/else, for/while/do-while, return, break/continue, discard.
- [x] Semantics and registers: richer annotation capture and binding syntax (register(c0)/packoffset/etc.).
- [x] Legacy sampler/texture syntax (SM1-SM3): sampler declarations and sampler_state blocks.
- [x] SM4/SM5 constructs: `cbuffer`/`tbuffer` blocks (syntax-only bodies) and resource declarations.
- [x] FX framework constructs: `technique`, `technique10`, `pass`, `CompileShader(...)`, `SetPixelShader(...)` parsed as syntax.
- [x] Error recovery: diagnostics with stable IDs for missing semicolon, missing brace, unexpected token; continue producing well-formed trees.

## Error Recovery (all eras)
- [x] Diagnostics with stable IDs for missing semicolon, missing brace, unexpected token; recovery keeps `CompilationUnit` well-formed.
- [x] Tests: targeted negative cases for missing semicolon/braces; expand per era as parser grows.

## Testing and Snapshots
- [x] Unit and negative tests per category above (lexer and parser) across eras.
- [x] SM1.x legacy lex snapshots (fixtures + December 2002 Glow.fx).
- [x] SM2/SM3 lex snapshot (tests/fixtures/sm2-snapshot.hlsl).
- [x] SM4/SM5 lex snapshot (tests/fixtures/sm4-snapshot.hlsl).
- [x] Parser snapshots per era (SM1, SM2/3, SM4, SM5, FX) covering AST/tokens/diagnostics.
- [x] CLI smoke tests for `lex`/`parse` with file/stdin IO paths (covered via C# tests).
- [x] CI/local test script to run unit, snapshot, negative, and CLI smoke suites quickly.
