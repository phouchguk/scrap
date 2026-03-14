# Scrapyards

A **scrapyard** is a local content-addressable store for Scrapscript values. Every value you push is serialized to a canonical binary format, SHA1-hashed, and stored by that hash. You can retrieve any value later using its hash reference — a string of the form `sha1~~<40 hex chars>`.

This is the foundation of Scrapscript's identity model: a value *is* its hash. The same value always produces the same hash, on any machine.

---

## Quick start

Initialize a yard, push a value, pull it back:

```sh
scrapscript yard init
scrapscript push "3 * 5"
# $sha1~~c7255dc48b42d44f6c0676d6009051b7e1aa885b

scrapscript pull sha1~~c7255dc48b42d44f6c0676d6009051b7e1aa885b
# 15
```

The `push` command evaluates the expression first, so `3 * 5` and `15` produce the same hash.

---

## The hash reference syntax

In Scrapscript source, a stored value is referred to with a `$` prefix:

```
$sha1~~c7255dc48b42d44f6c0676d6009051b7e1aa885b
```

This is a first-class expression. You can use it anywhere a value is expected:

```
$sha1~~c7255dc48b42d44f6c0676d6009051b7e1aa885b + 1
# 16
```

The `$` is the lexer token prefix; the string stored in the yard and passed to `pull` omits it:

```sh
# In source code:    $sha1~~<hash>
# In the CLI:         sha1~~<hash>   (no $ prefix)
```

---

## CLI commands

### `yard init`

Creates the yard directory if it doesn't exist. Safe to run multiple times.

```sh
scrapscript yard init
# Initialized scrapyard at /home/you/.scrap/yard
```

The default location is `~/.scrap/yard`. Override with the `SCRAP_YARD` environment variable:

```sh
SCRAP_YARD=/tmp/myyard scrapscript yard init
```

### `flat <expr>`

Evaluates an expression and prints the raw flat encoding as uppercase hex. Useful for understanding the wire format or verifying canonical form.

```sh
scrapscript flat "42"
# 2A

scrapscript flat '"hello"'
# A568656C6C6F

scrapscript flat "[1, 2, 3]"
# 93010203

scrapscript flat "{ x = 1, y = 2 }"
# 82A17801A17902

scrapscript flat "#true"
# C705000474727565

scrapscript flat "3.14"
# CB40091EB851EB851F
```

### `push <expr>`

Evaluates an expression, encodes it, stores it in the yard, and prints the hash reference (with `$` prefix, ready to paste into source).

```sh
scrapscript push "42"
# $sha1~~df58248c414f342c81e056b40bee12d17a08bf61

scrapscript push '"hello"'
# $sha1~~8ddf80d0b56beaa9e33d4880a065fc81d964e49f

scrapscript push "[1, 2, 3]"
# $sha1~~78a74af6c06029985f388dfeceb9794100377124

scrapscript push '{ name = "alice", score = 100 }'
# $sha1~~c659d3a01fab87e936643963662a2856c99221b0
```

Pushing the same value twice returns the same hash and overwrites the file with identical bytes — it is idempotent.

### `pull <hashref>`

Fetches the value for a given hash reference and prints it in Scrapscript display format.

```sh
scrapscript pull sha1~~df58248c414f342c81e056b40bee12d17a08bf61
# 42

scrapscript pull sha1~~8ddf80d0b56beaa9e33d4880a065fc81d964e49f
# "hello"

scrapscript pull sha1~~78a74af6c06029985f388dfeceb9794100377124
# [1, 2, 3]
```

If the hash is not found, an error is printed to stderr and the process exits.

---

## The flat binary format

The encoding is a msgpack-compatible subset. All integers are big-endian.

| Value | Encoding | Example |
|-------|----------|---------|
| `ScrapInt` 0–127 | positive fixint: `0x00`–`0x7F` | `42` → `2A` |
| `ScrapInt` −32–−1 | negative fixint: `0xE0`–`0xFF` | `-1` → `FF` |
| `ScrapInt` other | `D3` + 8-byte int64 | `1000000000` → `D3 00000000 3B9ACA00` |
| `ScrapFloat` | `CB` + 8-byte float64 | `3.14` → `CB 400921F9F01B866E` |
| `ScrapHole` | `C0` | `()` → `C0` |
| `ScrapText` ≤31 UTF-8 bytes | `A0\|len` + bytes | `"hi"` → `A2 6869` |
| `ScrapText` longer | `D9` + 1-byte len + bytes | |
| `ScrapBytes` | `C4` + 1-byte len + bytes | |
| `ScrapList` ≤15 items | `90\|count` + items | `[1,2,3]` → `93 01 02 03` |
| `ScrapList` longer | `DC` + 2-byte count + items | |
| `ScrapRecord` ≤15 fields | `80\|count` + (key, value) pairs, **keys sorted** | |
| `ScrapRecord` longer | `DE` + 2-byte count + pairs | |
| `ScrapVariant` | `C7` + 1-byte ext-len + `00` + tag-len(1) + tag + payload? | `#true` → `C7 05 00 04 74727565` |

Record fields are always sorted alphabetically by key. This ensures two records with the same fields in different insertion order encode identically and hash to the same value.

Functions and builtins cannot be encoded (they are runtime-only and have no canonical form).

---

## Storage layout

The yard uses the same two-character sharding strategy as Git:

```
~/.scrap/yard/
└── sha1/
    ├── df/
    │   └── 58248c414f342c81e056b40bee12d17a08bf61   ← raw bytes for 42
    ├── 8d/
    │   └── df80d0b56beaa9e33d4880a065fc81d964e49f   ← raw bytes for "hello"
    └── 78/
        └── a74af6c06029985f388dfeceb9794100377124   ← raw bytes for [1, 2, 3]
```

Each file contains the raw flat bytes — no envelope, no metadata. You can inspect them directly:

```sh
xxd ~/.scrap/yard/sha1/df/58248c414f342c81e056b40bee12d17a08bf61
# 00000000: 2a   *

xxd ~/.scrap/yard/sha1/8d/df80d0b56beaa9e33d4880a065fc81d964e49f
# 00000000: a568 656c 6c6f   .hello
```

---

## Using hash refs in programs

Once a value is in the yard, you reference it directly in source code. The evaluator fetches and decodes it transparently.

```
# Using a stored list
list/length $sha1~~78a74af6c06029985f388dfeceb9794100377124
# 3

# Arithmetic on a stored number
$sha1~~df58248c414f342c81e056b40bee12d17a08bf61 * 2
# 84

# Binding a hash ref to a name
result
; stored = $sha1~~78a74af6c06029985f388dfeceb9794100377124
; result = list/length stored
# 3
```

This lets you separate the definition of a value from its use — the hash is a stable, permanent name for that exact value.

---

## Using the API from C#

```csharp
using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Scrapyard;
using Scrapscript.Core.Serialization;

// Set up a yard
var yard = new LocalYard();  // defaults to ~/.scrap/yard
yard.Init();

// Push a value
var bytes = FlatEncoder.Encode(new ScrapInt(42));
var hashRef = yard.Push(bytes);
// hashRef = "sha1~~df58248c414f342c81e056b40bee12d17a08bf61"

// Pull it back
var raw = yard.Pull(hashRef);
var value = FlatDecoder.Decode(raw!);
// value = ScrapInt(42)

// Use hash refs in evaluated expressions
var interpreter = new ScrapInterpreter(yard);
var result = interpreter.Eval($"${hashRef} + 1", typeCheck: false);
// result = ScrapInt(43)

// Check presence without fetching
bool exists = yard.Contains(hashRef);  // true

// Use a custom location
var tempYard = new LocalYard("/tmp/my-test-yard");
tempYard.Init();

// Or via environment variable
// SCRAP_YARD=/mnt/shared/yard scrapscript ...
```

---

## Notes

- **Hash refs disable type checking.** Pass `typeCheck: false` when evaluating expressions that contain `$sha1~~...` references, since the type checker cannot look up the type of a stored value.
- **Only data values can be stored.** Functions, builtins, and partial applications have no flat encoding and will throw if you attempt to encode them.
- **The yard is append-only by design.** A hash identifies exactly one value forever. Overwriting is safe only because the content is always identical (same value → same bytes → same hash).
- **The `SCRAP_YARD` environment variable** overrides the default path for both the CLI and the `LocalYard()` constructor when no explicit root is passed.
