# DX9 Bytecode and Compiler Behavior

`openfxc-hlsl` stays strictly syntax-only, but we still track DX9/FXC-era behavior so downstream bytecode emitters have reliable input.

- Front-end scope: lex/parse + lightweight preprocessing (includes, macros, conditionals). No DXBC/DX9 bytecode emission in this layer.
- Compatibility goal: spans, token order, and recovered syntax should mirror FXC acceptance so later compiler stages can reproduce FXC-compatible bytecode and diagnostics.
- Preprocessor parity: include search order (`"` uses the current file first, `-I` paths next; `<...>` uses `-I` only), macro expansion is deterministic (no stringizing/`##` yet), and conditional handling preserves line structure for stable spans.
- Future tracking items for the backend: DX9 register packing rules, FXC diagnostic text/IDs, and DXBC emission decisions should be documented alongside this front-end once those layers are implemented.
- FX9 effect syntax we accept for parity: angle-bracket resource bindings inside passes (`Texture[0] = <TextureName>;`), compile invocations inside states (`PixelShader = compile ps_1_1 Foo();`), inline asm bodies, and parameter modifiers/array parameters (`in`, `out`, `uniform`) so DXSDK-era .fx files (e.g., DepthOfField.fx) round-trip without diagnostics.

For day-to-day work, keep DX9 expectations in mind while evolving the syntax front-end, and surface any deviations from FXC that could affect bytecode generation downstream.
