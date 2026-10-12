# Assessment practice sets

Timed practice for online assessments, with the problem statements on the handbook's [practice page](../../docs/interviews/assessments/practice/index.md).

| Set | Time | Folder |
|---|---|---|
| 1. Warm-up | 60 min | `Set1.Warmup` |
| 2. Large inputs | 75 min | `Set2.LargeInputs` |
| 3. SQL (needs Docker) | 45 min | `Set3.Sql` |
| 4. Project task | 60 min | `Set4.Project`, with the task in its README |

`Toolkit/` holds the tested snippets from the handbook's C# toolkit page; it isn't a practice set.

Each set is a test project:

- `Problems/` holds your starting points. Write your answers here.
- `Reference/` holds worked solutions. Don't open it until you've finished the set.
- `Tests/` holds the tests, marked the way a platform marks them: a few **visible** examples, and **hidden** tests for edge cases and large inputs. Large-input tests have a time limit, so a solution with the wrong complexity fails them, as it would on a platform.

## Running a set

From this folder:

```sh
dotnet test --project Set1.Warmup --filter-trait "Tests=Visible"   # only the examples, as the platform shows them
dotnet test --project Set1.Warmup                                  # everything, as the platform scores it
```

To see the worked solutions pass, build them instead of your answers:

```sh
dotnet test --project Set1.Warmup -p:UseReference=true
```

Switch back by running without `-p:UseReference=true`.
