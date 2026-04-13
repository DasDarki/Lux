# Lux Language Documentation

Lux is a typed Lua transpiler. It extends Lua 5.1-5.4 and LuaJIT with optional type annotations, an ES-style module system, classes, pattern matching, async/await, and more. All type information is compile-time only -- the output is clean, idiomatic Lua.

## Table of Contents

1. [Getting Started](01-getting-started.md) -- Project setup, configuration, target versions
2. [Type System](02-types.md) -- Primitives, nullable, union, array, map, struct, function, tuple types
3. [Variables & Constants](03-variables.md) -- Local variables, mutability, const, deep freeze
4. [Functions](04-functions.md) -- Named, local, anonymous, default params, varargs, overloading
5. [Control Flow](05-control-flow.md) -- If/else, loops, do blocks, break, goto
6. [Operators](06-operators.md) -- Arithmetic, comparison, logical, bitwise, increment/decrement, nil coalescing, optional chaining
7. [Strings](07-strings.md) -- Literals, interpolation, concat operator
8. [Enums](08-enums.md) -- Named constants with auto-numbering or explicit values
9. [Classes](09-classes.md) -- OOP with constructors, inheritance, abstract, override, protected, getters/setters
10. [Interfaces](10-interfaces.md) -- Type contracts, interface inheritance, implementation
11. [Modules](11-modules.md) -- Import/export, named/default/namespace imports
12. [Pattern Matching](12-pattern-matching.md) -- Match statements/expressions, value/type/wildcard patterns, guards
13. [Async / Await](13-async-await.md) -- Coroutine-based async functions
14. [Nilability & Optionals](14-nilability.md) -- Nullable types, nil assertion, optional chaining, nil coalescing
15. [Declaration Files](15-declarations.md) -- .d.lux files for typing external Lua code
16. [Tables](16-tables.md) -- Table constructors, type annotations, index base
