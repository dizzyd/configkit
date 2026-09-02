# Vendored dependencies

## SimpleExpressionEngine

Source: https://github.com/maltiez2/SimpleExpressionEngine (CC0 1.0 Universal)
Vendored at commit of configlib v1.13.2, 2026-09-02.

Used by `Config/Expressions.cs` to evaluate the boolean and arithmetic expressions
that `configlib-patches.json` settings can carry. Vendored rather than referenced as
a submodule so the build has no dependency on a third-party repository staying up.
