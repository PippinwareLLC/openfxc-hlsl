# openfxc-hlsl
The open HLSL lexer / parser for SM1 - SM5 (syntax-only front-end).

# Origin
This project was created by peeling off the commits from Pippinware LLCs' in-progress OpenFXC (currently private) project

## Aim of the Project
- Provide a deterministic, syntax-only HLSL front-end matching FXC-era SM1-SM5 acceptance.
- Emit stable JSON for tokens/AST with accurate spans and diagnostics for tooling and downstream compilers.
- Stay reusable: clean CLI (`lex`/`parse`) and library surfaces without semantics, IR, or bytecode concerns.

## Usage
1. Build: `dotnet build src/openfxc-hlsl/openfxc-hlsl.csproj`
2. Lex: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe lex -i path/to/file.hlsl`
3. Parse: `src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe parse -i path/to/file.hlsl`

Current output includes full lexing for SM1–SM5 and FX constructs; parsing now covers era-specific declarations (samplers/sampler_state, semantics/register/bindings, cbuffer/tbuffer, class/interface/struct bodies, typedefs) and FX technique/pass bodies (syntax-only) with a `CompilationUnit` AST and diagnostics. Schema matches `docs/TDD.md`.

## Testing
- Run all tests: `dotnet test tests/OpenFXC.Hlsl.Tests/OpenFXC.Hlsl.Tests.csproj`
- Snapshot coverage: lex + parse snapshots pinned per era (SM1, SM2/3, SM4, SM5) plus FX technique/pass to keep output deterministic.
- Quick all-in-one: `tests/run-all.cmd` (Windows) or `tests/run-all.sh` (bash) to execute the full suite.

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
| SM1.x (legacy D3D9) | Done | Era parsing | Samplers, `sampler_state` blocks, semantics/register annotations, core statements/expressions |
| SM2.x / SM3.x | Done | Era parsing | Functions with parameter/return semantics, structs/typedefs, arrays, control flow, sampler-heavy code |
| SM4.x | Done | Era parsing | `cbuffer`/`tbuffer` with bindings, resource templates, class/interface/method signatures |
| SM5.x | Done | Era parsing | RW/structured/byte address resources with bindings and expressions/statements |
| FX constructs (.fx) | Done | FX parsing | Lexer covers technique/technique10/pass and Compile/Set* shader calls; parser handles technique/pass bodies syntax-only |
