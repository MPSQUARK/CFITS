# CFITS repository conventions

## One type per file

Every authored C# file must contain exactly one top-level class, struct, record,
interface, or enum declaration. Put related top-level types in separate files
with filenames matching the type name, and keep namespaces aligned with the
folder structure.

Private nested implementation details are allowed only when they are inseparable
from their containing type and are not independently reusable. Extract any
helper that represents an independent concept into its own file. Generated
source text may contain nested types when the generated API requires that shape.

Before completing a change:

1. Search the changed C# files for every type declaration.
2. Split additional types into appropriately named files.
3. Run the build and tests after the split.

Do not introduce a new multi-type file merely to avoid a small file addition.

## Arithmetic in hot paths

Avoid division and modulo in measured hot loops when an equivalent incremental
or bitwise formulation is provably correct for the complete operand domain.
Prefer maintaining running indexes/strides with addition over recomputing
products. For unsigned non-negative values and constant powers of two, a
right-shift/mask or named helper may replace division/modulo; do not apply that
rewrite blindly to signed values, negative operands, overflow-prone expressions,
or cases where C# remainder semantics differ.

Every arithmetic rewrite must:

1. State the valid domain and overflow assumptions.
2. Preserve exact signed, negative, zero, and boundary behavior.
3. Have focused differential tests against the original formulation.
4. Have a targeted benchmark showing a speed or allocation benefit.

Do not add helpers solely to disguise arithmetic. Use a helper only when the
operation is reused, its domain is explicit, and the JIT can still inline it.
