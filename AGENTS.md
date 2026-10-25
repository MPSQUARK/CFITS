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
