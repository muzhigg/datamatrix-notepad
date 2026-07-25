# Datamatrix Notepad

## Source of truth

- Product behavior: `docs/spec.md`
- Implementation order: `tasks/plan.md`
- Task acceptance criteria: `tasks/todo.md`

Update the specification before changing agreed behavior.

## Stack

- .NET 10 and C#
- Blazor WebAssembly
- Microsoft Fluent UI Blazor
- xUnit
- JavaScript ES modules for browser-only APIs
- GitHub Pages

## Commands

Run from the repository root:

```powershell
dotnet run --project ".\Datamatrix Notepad\Datamatrix Notepad.csproj"
dotnet test ".\Datamatrix Notepad.slnx" --configuration Release
dotnet build ".\Datamatrix Notepad.slnx" --configuration Release
dotnet format ".\Datamatrix Notepad.slnx" --verify-no-changes
```

## Conventions

- Keep nullable reference types enabled.
- Use `PascalCase` for public members and `camelCase` for locals.
- Suffix asynchronous methods with `Async`.
- Keep parsing and filename rules in pure, testable C# services.
- Keep browser device handles inside JavaScript; never serialize `SerialPort`.
- Treat scanner content as plain text, never HTML.
- Prefer small vertical slices and failing-first tests for behavior.

## Boundaries

- Do not send notes, settings, or scans to a server.
- Do not add telemetry.
- Ask before adding runtime dependencies or changing storage away from `localStorage`.
- Ask before listening to more than one serial port.
- Require explicit user confirmation before deleting a note.
- Run focused tests, the full suite, formatting, and a Release build before completing a task.
