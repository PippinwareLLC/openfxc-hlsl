# TODO

## Foundation
- [x] Stand up the `openfxc-hlsl` CLI skeleton with `lex` and `parse` verbs that accept file/stdin and emit JSON per `docs/TDD.md`.
- [x] Enforce JSON schema conformance (`formatVersion: 1`, source metadata, tokens, diagnostics) for both lex and parse outputs.
- [ ] Stabilize diagnostic IDs/messages and ensure spans satisfy `0 <= start <= end <= length` on all tokens and nodes.
- [ ] Preserve trivia (whitespace/newlines/comments) and preprocessor lines as specified; ensure operator/punctuation coverage across eras.

## Shader Model 1.x (legacy D3D9 era)
- [x] Lexer: legacy sampler/texture keywords (`sampler`, `sampler1D/2D/3D/CUBE`, `texture`), basic numeric literals, directives.
- [ ] Parser: sampler declarations, sampler_state blocks (`sampler_state { MinFilter = Linear; }`), fixed-function style constructs as syntax.
- [x] Tests: positive/negative lex cases for comments/whitespace/legacy keywords; snapshot of a representative SM1.x shader.

## Shader Model 2.x / 3.x
- [x] Lexer: flow/storage keywords, vector/matrix types, intrinsics in identifiers, preprocessor coverage, semantics/register tokens.
- [ ] Parser: functions with parameter/return semantics (`POSITION0`, `TEXCOORD*`), arrays, structs/typedefs, expressions (swizzles, indexing, calls), statements (if/else, loops, discard).
- [x] Tests: unit/negative coverage for semantics/register forms, control flow, expression precedence; snapshot of an SM2/SM3 shader with intrinsics and sampler usage.

## Shader Model 4.x / 5.x
- [ ] Lexer: SM4/5 resource keywords (`cbuffer`, `tbuffer`, `Texture2D/3D`, `StructuredBuffer`, `RWTexture*`, `RWBuffer`, `AppendStructuredBuffer`, `ConsumeStructuredBuffer`, `ByteAddressBuffer`), class/interface keywords.
- [ ] Parser: resource templates (`Texture2D<float4>` etc.), `cbuffer`/`tbuffer` blocks with bindings, interfaces/classes/methods (syntax-only), resource binding/register syntax.
- [ ] Tests: positive/negative coverage for resource declarations, binding syntax, class/interface shapes; snapshots for SM4 and SM5 shaders with buffers and RW resources.

## FX Framework Constructs (cross-era)
- [ ] Lexer: `technique`, `technique10`, `pass`, directives inside FX blocks.
- [ ] Parser: `technique`/`pass` blocks, `CompileShader(...)`, `SetPixelShader(...)` parsed as syntax without semantics.
- [ ] Tests: FX construct lex/parse coverage and snapshot of a representative .fx file.

## Error Recovery (all eras)
- [ ] Diagnostics with stable IDs for missing semicolon, missing brace, unexpected token; recovery to keep trees well-formed.
- [ ] Tests: targeted negative cases in each era to verify recovery continues producing a `CompilationUnit` with children.

## Testing and Snapshots
- [x] Unit and negative tests per category above (lexer and parser) — SM1 lexing covered; parser pending.
- [x] SM1.x legacy lex snapshots (fixtures + December 2002 Glow.fx); remaining eras pending.
- [x] SM2/SM3 lex snapshot (tests/fixtures/sm2-snapshot.hlsl).
- [x] CLI smoke tests for `lex`/`parse` with file/stdin IO paths (covered via C# tests).
- [ ] CI/local test script to run unit, snapshot, negative, and CLI smoke suites quickly.
