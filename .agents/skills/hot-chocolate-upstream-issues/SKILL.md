---
name: hot-chocolate-upstream-issues
description: Prepare upstream Hot Chocolate issue/comment proposals when FSharp.HotChocolate needs a Hot Chocolate public API or extension point, especially when a local fix would otherwise depend on Hot Chocolate internals.
---

## Workflow

Use this skill when a requested FSharp.HotChocolate fix appears to require unsupported or internal Hot Chocolate APIs.

1. Search `ChilliCream/graphql-platform` issues for the missing public API, extension point, or behavior gap.
2. If a relevant issue exists, prepare a proposed comment only when this repo can add useful new information, such as:
   - an F#-specific scenario
   - a minimal repro
   - affected Hot Chocolate versions
   - a missing public extension point
3. If no relevant issue exists, or the closest issues are closed or resolved in a way that makes a fresh report clearer,
   check the current upstream templates in `ChilliCream/graphql-platform/.github/ISSUE_TEMPLATE`.
4. Choose the current template that best matches the request. Prefer a feature/API request unless behavior is
   demonstrably broken.
5. Propose both:
   - a new issue title
   - the exact inputs the user should select or paste for every required field and any relevant optional field, including
     dropdown/radio values and free-form Markdown fields

## Framing

- Frame upstream posts as requests for public APIs or extension points that `FSharp.HotChocolate` needs in order to
  implement F# support itself. Do not ask Hot Chocolate to own F# support.
- Write for Hot Chocolate maintainers: F# support is handled through `FSharp.HotChocolate`, and this package needs
  supported HC extension points to do that job.
- Include only the minimum F#-specific details needed to explain the integration need:
  - the F# source shape
  - the relevant CLR/runtime wrapper shape
  - the specific public surface gap
- Avoid general F# background unless it directly explains the CLR shape or extension-point gap.
- Keep examples and the whole issue minimal, clear, and focused on what this package needs from Hot Chocolate and why the
  current public surface is insufficient.
- When useful, include concise implementation hints or possible API shapes that could make the upstream change simple to
  implement. Keep them non-prescriptive and light on code.

## Output

- For existing issues, provide the issue number, URL, current state, and a short assessment of whether a comment is worth
  adding. If yes, provide the comment as Markdown source for user approval.
- For new issues, provide the selected template name, issue title, and field-by-field inputs.
- Use fenced code blocks for Markdown field bodies. If the field body contains code fences, wrap the whole body in a
  longer fence, such as four backticks.
- After the upstream issue/comment path has been considered, and before implementing any workaround that depends on Hot
  Chocolate internals, tell the user that an internals-dependent path exists, briefly explain how it would work and which
  internal surface it would rely on, and wait for explicit approval.
