# openfxc-hlsl
The open HLSL lexer / parser for SM1 - SM5 (syntax-only front-end).

## Aim of the Project
- Provide a deterministic, syntax-only HLSL front-end matching FXC-era SM1-SM5 acceptance.
- Emit stable JSON for tokens/AST with accurate spans and diagnostics for tooling and downstream compilers.
- Stay reusable: clean CLI (`lex`/`parse`) and library surfaces without semantics, IR, or bytecode concerns.

## Usage (M0 skeleton)
1. Build: `dotnet build src/openfxc-hlsl/openfxc-hlsl.csproj`
2. Lex: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe lex -i path/to/file.hlsl`
3. Parse: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe parse -i path/to/file.hlsl`

Current M0 output is a stub: empty token lists and a `CompilationUnit` root with spans sized to the input; schema matches `docs/TDD.md`.

## Docs
- Behavioral contract: `docs/TDD.md`
- Work queue: `docs/TODO.md`
- Scope: Syntax-only SM1-SM5 FXC-era HLSL (no semantics, IR, or bytecode in this layer)
- Milestones: `docs/MILESTONES.md`

## Compatibility Matrix (FXC-era Syntax Only)

| Shader Model / Era | Lexing | Parsing | Notes |
| ------------------ | ------ | ------- | ----- |
| SM1.x (legacy D3D9) | Skeleton | Skeleton | M0 stub JSON; sampler/texture semantics to follow |
| SM2.x / SM3.x | Skeleton | Skeleton | M0 stub JSON; semantics/registers/intrinsics to follow |
| SM4.x | Skeleton | Skeleton | M0 stub JSON; cbuffers/resources/classes to follow |
| SM5.x | Skeleton | Skeleton | M0 stub JSON; RW resources/advanced buffers to follow |
| FX constructs (.fx) | Skeleton | Skeleton | M0 stub JSON; `technique`/`pass` parsing to follow |
