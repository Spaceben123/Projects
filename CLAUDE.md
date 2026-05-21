# Projects Monorepo — Claude Instructions

## Session Start (Required)

At the start of every session in this directory or any subdirectory, read these files before doing anything else:

1. `C:\Users\space\Projects\AI_MEMORY.md` — cross-project shared memory (also used by Gemini/Antigravit)
2. The active project's `AI_MEMORY.md` (e.g. `C:\Users\space\Projects\SpaceGame\AI_MEMORY.md`)
3. The active project's `CLAUDE.md` (e.g. `C:\Users\space\Projects\SpaceGame\CLAUDE.md`)

Also apply this rule if the user says they are about to work in this directory or a project subdirectory.

## Repo Structure

Single monorepo on `main` branch. Each project is a subfolder. Do not suggest branch-per-project.

| Project | Path | Status |
|---|---|---|
| SpaceGame | `SpaceGame\` | Active |

## What Is and Isn't in Git

- **In git:** all code, shaders, scripts, scenes, settings, docs
- **Not in git:** raw 8K .tif textures (1GB+) — synced via OneDrive/cloud
