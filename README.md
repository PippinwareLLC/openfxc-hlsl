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
4. Preprocessor: runs automatically for `lex`/`parse` to expand `#define`/`#include`/`#if`; add include search paths with `-I <dir>` (quoted includes search the source file's folder first).
5. Command-line defines: use `-D NAME` or `-D NAME=VALUE` (repeatable) to seed macros before preprocessing.

Current output includes full lexing for SM1-SM5 and FX constructs; parsing now covers era-specific declarations (samplers/sampler_state, semantics/register/bindings, cbuffer/tbuffer, class/interface/struct bodies, typedefs), FX technique/pass bodies (syntax-only), inline asm blocks, postfix/prefix ++/-- and compound assignments, cast expressions, FX resource bindings (`Texture[0] = <TextureName>;`), and FX9/FX10 compile invocations (`VertexShader = compile vs_1_1 Foo();`) with a `CompilationUnit` AST and diagnostics. Schema matches `docs/TDD.md`.

## Testing
- Run all tests: `dotnet test tests/OpenFXC.Hlsl.Tests/OpenFXC.Hlsl.Tests.csproj`
- Snapshot coverage: lex + parse snapshots pinned per era (SM1, SM2/3, SM4, SM5) plus FX technique/pass to keep output deterministic.
- Quick all-in-one: `tests/run-all.cmd` (Windows) or `tests/run-all.sh` (bash) to execute the full suite.

## Build (single-file binaries)

From the repo root, publish self-contained, single-file executables:

- Windows (x64):\
  `dotnet publish src/openfxc-hlsl/openfxc-hlsl.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true`

- Linux (x64):\
  `dotnet publish src/openfxc-hlsl/openfxc-hlsl.csproj -c Release -r linux-x64 -p:PublishSingleFile=true -p:SelfContained=true`

- macOS Intel:\
  `dotnet publish src/openfxc-hlsl/openfxc-hlsl.csproj -c Release -r osx-x64 -p:PublishSingleFile=true -p:SelfContained=true`

- macOS Apple Silicon:\
  `dotnet publish src/openfxc-hlsl/openfxc-hlsl.csproj -c Release -r osx-arm64 -p:PublishSingleFile=true -p:SelfContained=true`

Artifacts land under `src/openfxc-hlsl/bin/Release/net8.0/<rid>/publish/`. Add `-p:PublishTrimmed=true` if you want smaller binaries (verify before distributing).

## Library usage

- Core library: `src/OpenFXC.Hlsl/OpenFXC.Hlsl.csproj` (produces `OpenFXC.Hlsl.dll`).
- CLI wrapper: `src/openfxc-hlsl/openfxc-hlsl.csproj` references the library; use it for `lex`/`parse` commands.
- Example (C#):
  ```csharp
  var text = File.ReadAllText("shader.hlsl");
  var pre = Preprocessor.Preprocess(text, new PreprocessorOptions { FilePath = "shader.hlsl" });
  var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
  var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
  ```
- Reference the project directly or the built DLL to consume the lexer/parser from other tools.

## Docs
- Behavioral contract: `docs/TDD.md`
- Work queue: `docs/TODO.md`
- Scope: Syntax-only SM1-SM5 FXC-era HLSL (no semantics, IR, or bytecode in this layer)
- Milestones: `docs/MILESTONES.md`
- Architecture: `docs/LEXER_PARSER.md` (lexer/parser internals, diagnostics, recovery, determinism)
- DX9 bytecode + compiler behavior: `docs/dx9_bytecode.md`

## Contributing
We take contributions from the community for .hlsl/.fx files for SM1-SM5, please send us your shader code or fixes/addition via PR. Thank you!

## Compatibility Matrix (FXC-era Syntax Only)

| Shader Model / Era | Lexing | Parsing | Notes |
| ------------------ | ------ | ------- | ----- |
| SM1.x (legacy D3D9) | Done | Complete | Samplers, `sampler_state` blocks, semantics/register annotations, core statements/expressions |
| SM2.x / SM3.x | Done | Complete | Functions with parameter/return semantics, structs/typedefs, arrays, control flow, sampler-heavy code |
| SM4.x | Done | Complete | `cbuffer`/`tbuffer` with bindings, resource templates, class/interface/method signatures |
| SM5.x | Done | Complete | RW/structured/byte address resources with bindings and expressions/statements |
| FX constructs (.fx) | Done | Complete | Technique/technique10/pass bodies parsed syntax-only, FX10 state objects, Compile/Set shader calls tokenized |

## DXSDK Sample Coverage (lex/parse smoke)

- Source set: all `.fx` files under `samples/` (DX9 SDK drops, including Dec 2002 and later DXSDKs).
- Lexing: runs without crashes across all samples; diagnostics logged where legacy/non-HLSL text appears but tokens are produced.
- Parsing: always returns a `CompilationUnit` with full-span coverage; diagnostics are allowed for syntax outside the FXC-era subset, but trees are produced for every sample.
- To run locally: `dotnet test tests/OpenFXC.Hlsl.Tests/OpenFXC.Hlsl.Tests.csproj` (includes sample smoke) or `tests/run-all.cmd` / `tests/run-all.sh`.
- Samples attribution: the DX9/DXSDK `.fx` files are sourced from Microsoft DirectX SDK drops (e.g., Dec 2002, DXSDK_Feb10) and remain (c) Microsoft; included here solely for testing/compatibility purposes.



