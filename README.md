# openfxc-hlsl
The open HLSL lexer / parser for SM1 - SM5 (syntax-only front-end).

# Origin
This project was created by peeling off the commits from Pippinware LLCs' in-progress OpenFXC private project

## Aim of the Project
- Provide a deterministic, syntax-only HLSL front-end matching FXC-era SM1-SM5 acceptance.
- Emit stable JSON for tokens/AST with accurate spans and diagnostics for tooling and downstream compilers.
- Stay reusable: clean CLI (`lex`/`parse`) and library surfaces without semantics, IR, or bytecode concerns.

## Usage (M0 skeleton)
1. Build: `dotnet build src/openfxc-hlsl/openfxc-hlsl.csproj`
2. Lex: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe lex -i path/to/file.hlsl`
3. Parse: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe parse -i path/to/file.hlsl`

Current output (M1) includes a basic lexer for SM1.x-era syntax; parse still emits a `CompilationUnit` root with spans sized to the input and shares the lexed tokens/diagnostics. Schema matches `docs/TDD.md`.

## Testing
- Run all tests: `dotnet test tests/OpenFXC.Hlsl.Tests/OpenFXC.Hlsl.Tests.csproj`

## Docs
- Behavioral contract: `docs/TDD.md`
- Work queue: `docs/TODO.md`
- Scope: Syntax-only SM1-SM5 FXC-era HLSL (no semantics, IR, or bytecode in this layer)
- Milestones: `docs/MILESTONES.md`

## Contributing
We take contributions from the community for .hlsl/.fx files for SM1-SM5, please send us your shader code or fixes/addition via PR. Thank you!

## Compatibility Matrix (FXC-era Syntax Only)

| Shader Model / Era | Lexing | Parsing | Notes |
| ------------------ | ------ | ------- | ----- |
| SM1.x (legacy D3D9) | Done | Skeleton | Lexer covers legacy sampler/texture keywords, comments, numbers, operators; parsing still stub |
| SM2.x / SM3.x | Done (lexer) | Skeleton | Lexer covers flow/storage, vector/matrix, semantics/register/register(), intrinsics; parsing still stub |
| SM4.x | Skeleton | Skeleton | M0 stub JSON; cbuffers/resources/classes to follow |
| SM5.x | Skeleton | Skeleton | M0 stub JSON; RW resources/advanced buffers to follow |
| FX constructs (.fx) | Skeleton | Skeleton | M0 stub JSON; `technique`/`pass` parsing to follow |
