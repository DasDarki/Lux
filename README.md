<p align="center">
  <img src="assets/banner.png" alt="Lux Banner" width="200px" />
</p>

<p align="center">
  <strong>A typed superset of Lua that transpiles to clean, portable Lua.</strong><br/>
  Think TypeScript for Lua &mdash; full type safety, modern module system, zero runtime overhead.
</p>

<p align="center">
  <a href="#features">Features</a> &bull;
  <a href="#quick-start">Quick Start</a> &bull;
  <a href="#syntax-overview">Syntax</a> &bull;
  <a href="#type-system">Type System</a> &bull;
  <a href="#module-system">Modules</a> &bull;
  <a href="#configuration">Configuration</a> &bull;
  <a href="#language-server">Language Server</a> &bull;
  <a href="#building-from-source">Building</a>
</p>

---

## What is Lux?

Lux is a **typed Lua superset**. It accepts standard Lua (5.1 through 5.4 and LuaJIT) enriched with **optional type annotations**, an **ES-style import/export module system**, and **declaration files** (`.d.lux`) for describing external Lua APIs. The compiler emits clean, idiomatic Lua for your chosen target version.

Lux does not invent a new language. It **extends** Lua with the things you always wanted: types, modules, and tooling &mdash; while keeping the output readable and dependency-free.

## Features

- **Gradual type system** &mdash; Add types at your own pace. Every valid Lua program is valid Lux.
- **Multi-target** &mdash; Emit Lua 5.1, 5.2, 5.3, 5.4, or LuaJIT from a single codebase.
- **ES-style modules** &mdash; `import` / `export` with named, default, namespace, and side-effect imports.
- **Declaration files** &mdash; `.d.lux` files describe existing Lua APIs for type-safe interop.
- **Full type inference** &mdash; Types propagate through assignments, return values, and expressions.
- **Nullable safety** &mdash; Optional strict nil checking prevents `nil`-related runtime errors.
- **Immutability** &mdash; Optional immutable-by-default mode with deep freeze support.
- **Overrideable index base** &mdash; Write 0-based arrays; the compiler adjusts to Lua's 1-based indexing.
- **Overrideable concat operator** &mdash; Use `+` for string concatenation instead of `..`.
- **Dead code elimination** &mdash; Unused variables and functions are stripped from the output.
- **Name mangling & minification** &mdash; Built-in minifier with granular control over what gets mangled.
- **Pre/post build scripts** &mdash; Run shell commands before and after compilation.
- **Integrated language server** &mdash; LSP support with hover, go-to-definition, completions, references, rename, signature help, and semantic highlighting.
- **TOML configuration** &mdash; One `lux.toml` to configure everything, with config inheritance and presets.

## Quick Start

```bash
# Initialize a new project
lux init

# Write some code
cat > src/main.lux << 'EOF'
function greet(name: string): string
    return "Hello, " .. name .. "!"
end

local message = greet("World")
print(message)
EOF

# Build
lux build
```

This produces `out/main.lua` with clean, idiomatic Lua:

```lua
function greet(name)
    return "Hello, " .. name .. "!"
end

local message = greet("World")
print(message)
```

### CLI Commands

| Command       | Description                                      |
|---------------|--------------------------------------------------|
| `lux build`   | Compile the project (reads `lux.toml`)           |
| `lux init`    | Scaffold a new project in the current directory   |
| `lux lps`     | Start the language server (LSP via stdio)         |
| `lux version` | Print the Lux compiler version                    |
| `lux help`    | Show available commands                           |

## Syntax Overview

Lux is Lua with type annotations. Annotations are always optional.

### Variables

```lua
local x: number = 42
local name: string = "Lux"
local active: boolean = true
local data: any = nil
```

### Functions

```lua
function add(a: number, b: number): number
    return a + b
end

local function multiply(a: number, b: number): number
    return a * b
end
```

### Type annotations on parameters and return types

```lua
-- Nullable types
local name: string? = nil

-- Union types
local id: string | number = "abc"

-- Array types
local scores: number[] = {100, 95, 87}

-- Map types
local config: { [string]: boolean } = { verbose = true }

-- Struct types
local point: { x: number, y: number } = { x = 1, y = 2 }

-- Function types
local callback: (number, string) -> boolean

-- Tuple types (multi-return)
local result: (number, string) = compute()
```

### Configurable Concat Operator

With the default config (`concat_operator = "+"`), you can concatenate strings using `+`:

```lua
local greeting = "Hello, " + name + "!"
-- Transpiles to: local greeting = tostring("Hello, ") .. tostring(name) .. tostring("!")
```

The native `..` operator always works regardless of configuration.

### Configurable Index Base

With `index_base = 0` (default), write 0-based code:

```lua
local arr = {10, 20, 30}
local first = arr[0]        -- Transpiles to: arr[1]

for i = 0, 2 do
    print(arr[i])            -- Transpiles to: arr[(i + 1)]
end
```

## Type System

Lux implements a full gradual type system with inference.

### Primitive Types

`number`, `string`, `boolean`, `nil`, `any`, `void`

### Composite Types

| Syntax                         | Description         |
|--------------------------------|---------------------|
| `string?`                      | Nullable            |
| `string \| number`             | Union               |
| `number[]`                     | Array               |
| `number[][]`                   | Nested array        |
| `{ [string]: number }`         | Map                 |
| `{ x: number, y: string }`    | Struct / record     |
| `(number, string) -> boolean`  | Function            |
| `(number, string)`             | Tuple (multi-return)|
| `...: number`                  | Variadic            |

### Inference

Types propagate automatically &mdash; you don't need to annotate everything:

```lua
local x = 42              -- inferred as number
local y = "hello"          -- inferred as string
local z = x + y            -- type error: cannot add number and string
```

### Strict Mode

Enable the `strict` preset for maximum safety:

```toml
preset = "strict"
```

This enables:
- **Strict nil checking** &mdash; Variables must be explicitly nullable (`?`) to hold `nil`
- **No `any` type** &mdash; All values must have a concrete type
- **Immutable by default** &mdash; Variables are immutable unless marked otherwise
- **Exhaustive matching** &mdash; Pattern/switch expressions must cover all cases

## Module System

Lux uses an ES-style import/export system that transpiles to `require()` calls.

### Imports

```lua
-- Named imports
import { Vector2, Rect } from "engine/math"

-- Default import
import Player from "entities/player"

-- Namespace import
import * as utils from "lib/utils"

-- Aliased import
import { Vector2 as Vec2 } from "engine/math"

-- Side-effect import
import "polyfill"
```

### Exports

```lua
export function calculate(x: number): number
    return x * 2
end

export local PI: number = 3.14159
```

## Declaration Files

Describe existing Lua APIs with `.d.lux` files for type-safe interop:

```lua
-- math.d.lux
declare module "math"
    declare abs: (number) -> number
    declare floor: (number) -> number
    declare pi: number
end

declare function print(msg: string): void
declare function tostring(v: any): string
```

## Configuration

All configuration lives in `lux.toml` at the project root.

```toml
name = "my-project"
target = "lua54"            # lua51, lua52, lua53, lua54, luajit
source = "src"
output = "out"
entry = "src/main.lux"
minify = false

[scripts]
pre_build = ["echo Building..."]
post_build = ["echo Done!"]

[code]
string_interpolation = true
semicolons = "optional"     # optional, required, or forbidden
import_statement = "require(%s)"
strip_unused = true

[mangle]
enabled = false
mangle_locals = true
mangle_params = true
mangle_top_level = false
keep_function_names = true

[rules]
allow_any = true
strict_nil = false
immutable_default = false
deep_freeze = false
exhaustive_match = false
```

### Config Inheritance

Extend shared configurations:

```toml
extends = ["./base.toml", "./team-rules.toml"]
```

### Presets

Use built-in presets as a starting point:

```toml
preset = "strict"    # Maximum type safety
# or
preset = "relaxed"   # Standard Lua with types (default)
```

## Language Server

Lux ships with a built-in language server (LPS) providing IDE support:

- **Diagnostics** &mdash; Real-time error and warning reporting
- **Hover** &mdash; Type information on hover
- **Go to Definition** &mdash; Jump to symbol declarations
- **Completions** &mdash; Keywords and scope-aware symbol suggestions
- **Document Symbols** &mdash; File outline for functions and variables
- **Semantic Tokens** &mdash; Syntax highlighting from the compiler's token stream
- **Find References** &mdash; Locate all usages of a symbol
- **Rename** &mdash; Rename symbols across the file
- **Signature Help** &mdash; Parameter info for function calls

Start it with:

```bash
lux lps
```

The server communicates over stdio using the Language Server Protocol, making it compatible with any LSP-capable editor (VS Code, Neovim, Sublime Text, etc.).

## Compilation Pipeline

Lux compiles through a multi-pass pipeline:

```
Source (.lux)
  |
  v
[ANTLR4 Lexer/Parser] --> Concrete Syntax Tree
  |
  v
[IR Visitor] --> High-Level IR (Nodes: Expr, Stmt, Decl)
  |
  v
[ResolveLibs]       Load declaration files and builtins
[BindDeclare]       Bind declare statements to the type universe
[ResolveNames]      Resolve all name references to symbols
[ResolveTypeRefs]   Resolve type annotations to type IDs
[InferTypes]        Propagate and check types across expressions
[DetectUnused]      Flag unused symbols for stripping
[Mangle]            Rename symbols for minification
[Codegen]           Emit target Lua code
  |
  v
Output (.lua)
```

## Multi-Target Support

Write once, emit for any Lua version. The compiler automatically handles version differences:

| Feature             | 5.1 | 5.2 | 5.3 | 5.4 | LuaJIT |
|---------------------|-----|-----|-----|-----|--------|
| `goto` / labels     |     | Yes | Yes | Yes | Yes    |
| Floor division `//` |     |     | Yes | Yes |        |
| Bitwise operators   |     |     | Yes | Yes |        |
| `bit.*` library     |     |     |     |     | Yes    |
| `<const>` locals    |     |     |     | Yes |        |
| `<close>` locals    |     |     |     | Yes |        |

When a feature isn't available on the target, the compiler either emits a polyfill or skips the construct.

## Building from Source

Lux requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/DasDarki/Lux.git
cd Lux
dotnet build Lux/Lux.csproj
dotnet run --project Lux/Lux.csproj -- help
```

### Regenerating the Parser

After editing the ANTLR4 grammar (`Lux/Lux.g4`):

```bash
cd Lux && gen_antlr4.bat
```

## License

[MIT](LICENSE)
