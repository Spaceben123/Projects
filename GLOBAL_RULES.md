# Global AI Rules — All Projects

This file applies to ALL projects in this monorepo. Every AI agent (Antigravity/Gemini and Claude) must read this file before starting work on any project.

## Core Directives

1. **Always Read First:** At the start of every session, read GLOBAL_RULES.md (this file) AND the project-specific `AI_MEMORY.md` inside the relevant project subfolder.
2. **Update When Necessary:** If a global architectural decision or cross-project rule changes, update THIS file. If a project-specific decision changes, update only that project's `AI_MEMORY.md`.
3. **Monorepo Root:** `C:\Users\space\Projects`
4. **Aggressive Clean Up:** Always clean up files, scripts, or workspaces that are no longer used. If an agent is deprecated or superseded, proactively delete any leftover files, branches, or temporary artifacts.
5. **Dynamic Token Fallback:** If any agent or model (Gemini or Claude) runs out of tokens, hits a rate limit, or exhausts its context window, the orchestrator must dynamically swap to another available model or spin up a fresh subagent to continue the role seamlessly.
6. **Continuous State Sync (Eager Flushing):** Never wait until tokens are critically low to write state. Update `AI_MEMORY.md` and `task.md` incrementally after every major action so any replacement orchestrator can recover cleanly.

## Agent Swarm Architecture (Applies to All Projects)

| Role | Model | Responsibility |
|---|---|---|
| **Orchestrator** | Antigravity (Gemini) | Manages the swarm, coordinates agents, handles fallback |
| **Kernel Architect** | Gemini | System design, `implementation_plan.md` — no code |
| **Implementer** | Claude | Writes the actual codebase from the plan |
| **QA Validator** | Gemini | Headless automated tests first; MCP Unity inspection for special cases |

## Repository Structure

```
C:\Users\space\Projects\
  ├── GLOBAL_RULES.md          ← You are here (global rules for all agents)
  ├── AI_MEMORY.md             ← Global cross-project memory
  ├── SpaceGame\               ← Unity 6 URP alien spaceship sim
  │     ├── AI_MEMORY.md       ← SpaceGame-specific memory
  │     └── Assets\...
  └── [Future Projects]\
        └── AI_MEMORY.md       ← Project-specific memory
```

## GitHub Sync

- Remote: configured via `gh` CLI (authenticated)
- All agents may autonomously run `git add`, `git commit`, and `git push`
- Commit messages must follow: `type(scope): description` (e.g. `feat(spacegame): add weapon trajectory`)
