# Contributing

Thanks for your interest! This project is an early-stage, single-maintainer toolkit — PRs welcome, but please read the ground rules first.

## Development setup

1. **Engine**: Tuanjie 2022.3.62t13 (Unity 2022.3-compatible fork). Open the project root — it runs as-is. Stock Unity has not been verified.
2. **Python**: 3.10+ for the `Tools/` toolchain.
3. Open `Assets/Scenes/A2UITestBed.unity`, press Play, and use `A2UI Scheme A → 测试发送面板` to push samples.

## Before you open a PR

**Read [docs/engine-compat-tuanjie.md](docs/engine-compat-tuanjie.md) first** if you touch USS or the Mapper. It documents 30+ measured engine pitfalls (var()-with-fallback silently dropped, transform crash, flex-shrink default 0, Column wrap traps…). Most "one-line" USS changes are not one-liners on this engine.

### House rules encoded in the codebase

- USS: **no `transform`**, no `gap`, no `box-shadow`, no `:last-child`; **never** write `font-size: var(--x, Npx)` — the parser silently drops it (literals only)
- Mapper: Column is forced `NoWrap`; text variants are `a2ui-text--<hint>` — skins must cover all 8 variants
- Overlay ScrollView: horizontal scroller must stay `ScrollerVisibility.Hidden`
- Agent JSONL is untrusted input: keep the G0 validation/security table in the README intact

## Testing

| What | How |
|------|-----|
| Unit + full-matrix layout regression | Editor Test Runner (PlayMode) or `python Tools/run_regression.py --editor "<path to Tuanjie.exe>"` |
| Visual diff | `python Tools/regression_diff.py` (needs screenshot baselines) |
| Figma calibration | see [docs/figma_pipeline_status.md](docs/figma_pipeline_status.md) |

CI on GitHub runs lightweight static checks only (Python syntax, USS lint). **The layout regression requires a local Tuanjie editor** — CI cannot run it; please run it locally before PRs that touch `Runtime/`, `Styles/`, or `Samples/`.

## Commit style

Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`). Keep subjects short; put the "why" in the body.

## License

By contributing you agree that your contributions are licensed under the MIT License of this repository. Third-party assets keep their own licenses (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)) — do not re-license them.
