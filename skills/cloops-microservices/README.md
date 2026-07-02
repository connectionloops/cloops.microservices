# cloops-microservices skill

An installable [Agent Skill](https://cursor.com/docs/skills) that gives coding agents the `cloops.microservices` SDK documentation index, with absolute links back to the source docs.

## Install

```bash
npx skills add connectionloops/cloops.microservices --skill cloops-microservices
```

Add `-g` to install globally across all projects.

## Why absolute links?

Installing a skill copies its `SKILL.md` onto the user's machine, away from this repository. Relative paths to `../docs/*` (or symlinks) would not resolve there, so `SKILL.md` links to the docs using absolute GitHub URLs on the `main` branch. The documentation itself stays in [`/docs`](https://github.com/connectionloops/cloops.microservices/tree/main/docs) as the single source of truth.

## Updating

When docs are added or renamed under `/docs`, update the Documentation Index in `SKILL.md` to match.
