# openfxc-hlsl
The open HLSL lexer / parser for SM1 - SM5 (syntax-only front-end).

# Origin
This project was created by peeling off the commits from Pippinware LLCs' in-progress OpenFXC private project

## Aim of the Project
- Provide a deterministic, syntax-only HLSL front-end matching FXC-era SM1-SM5 acceptance.
- Emit stable JSON for tokens/AST with accurate spans and diagnostics for tooling and downstream compilers.
- Stay reusable: clean CLI (`lex`/`parse`) and library surfaces without semantics, IR, or bytecode concerns.

## Usage
1. Build: `dotnet build src/openfxc-hlsl/openfxc-hlsl.csproj`
2. Lex: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe lex -i path/to/file.hlsl`
3. Parse: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe parse -i path/to/file.hlsl`

Current output includes full lexing for SM1–SM5 and FX constructs; parsing produces a `CompilationUnit` AST with spans/diagnostics and baseline statements/expressions/decls (syntax-only, no semantics). Schema matches `docs/TDD.md`.

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
| SM1.x (legacy D3D9) | Done | Baseline | Lexer covers legacy sampler/texture keywords, comments, numbers, operators; parser handles basic decls/blocks/flow |
| SM2.x / SM3.x | Done | Baseline | Lexer covers flow/storage, vector/matrix, semantics/register/register(), intrinsics; parser handles basic decls/blocks/flow |
| SM4.x | Done | Baseline | Lexer covers cbuffers/tbuffers, resources, class/interface tokens; parser handles basic decls/blocks/flow |
| SM5.x | Done | Baseline | Lexer covers RW resources, structured/byte address buffers; parser handles basic decls/blocks/flow |
| FX constructs (.fx) | Done | Baseline | Lexer covers technique/technique10/pass and Compile/Set* shader calls; parser handles basic decls/blocks/flow |
