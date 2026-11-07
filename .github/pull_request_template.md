## Summary

<!-- What changes and why. Link the issue or the backend / mintlify-docs change this follows. -->

## Tests

<!-- How this is tested: new or updated unit tests, generated tests, smoke tests (DIDIT_SANDBOX_API_KEY), manual checks. -->

## Checklist

- [ ] Conventional commit title (`feat:`, `fix:`, `docs:`, `chore:` ...) whose type matches the semver impact; `!` for a breaking change
- [ ] Generated code regenerated with `python3 codegen/generate.py`
- [ ] No hand edits under `Generated/` (change `codegen/` or `openapi/overlay.json` instead)
- [ ] No hand edits to `openapi/verification.json`, `openapi/account.json`, `openapi/webhook-vectors.json` or `CHANGELOG.md`
- [ ] Tests added or updated; `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass locally
- [ ] Public API changes are reflected in `README.md`
- [ ] Parity: same change in the other six SDK repositories, or not applicable because ...
- [ ] No secrets, API keys or real customer data (fixtures are synthetic)
