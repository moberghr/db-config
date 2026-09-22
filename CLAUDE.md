# DbConfig — Engineering Standards

@AGENTS.md

The project constitution lives in AGENTS.md — read that file. This one only imports it.

## Claude Code only

- `.claude/rules/*.md` are auto-loaded by Claude Code; other harnesses must read them explicitly.
- Keep every rule in AGENTS.md, never here. Under the default `claude-md-or-agents-md` mode Claude Code reads this file and would otherwise ignore AGENTS.md entirely — the bare `@AGENTS.md` import above is the only thing that makes the constitution reachable. Rule text added here would be invisible to every non-Claude harness, which reads AGENTS.md directly.
- MTK ships as a Claude Code plugin — upgrade through the plugin manager, not from this repo.

<!-- mtk-setup: v7.35.0
     coding-guidelines: moberghr/coding-guidelines@4043387ca2c70ed0cd76e005861f5c471908c3bb
     generated: 2026-09-21T15:52:00Z -->
