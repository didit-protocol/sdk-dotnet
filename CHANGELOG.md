# Changelog

## [1.0.1](https://github.com/didit-protocol/sdk-dotnet/compare/v1.0.0...v1.0.1) (2026-10-29)


### chore

* release 1.0.1 ([bcbb74d](https://github.com/didit-protocol/sdk-dotnet/commit/bcbb74df84981e3dd992942ed338eb865bbb654d))


### Documentation

* **release:** releases publish straight from the repository ([#12](https://github.com/didit-protocol/sdk-dotnet/issues/12)) ([9615f4a](https://github.com/didit-protocol/sdk-dotnet/commit/9615f4a7e66c54529b1d618f43c05c3b3a35ec82))

## [1.0.0](https://github.com/didit-protocol/sdk-dotnet/compare/v0.1.0...v1.0.0) (2026-10-29)


### ⚠ BREAKING CHANGES

* **spec:** the API spec sync changes the SDK surface (nullable fields, removed fields, account.organizations.list returns a page, the session decision response is the SessionDecision component).

### chore

* release 1.0.0 ([dad0df1](https://github.com/didit-protocol/sdk-dotnet/commit/dad0df11935accc7ea57566d04ef3b1a39a086a7))


### Features

* add bank.account_check (POST /v3/bank/account-check/) and bank_verifications on the decision ([7514537](https://github.com/didit-protocol/sdk-dotnet/commit/751453719bfb6ef38ea3de6d32345beac1ebbf24))
* **spec:** sync API spec 2026-10-23 ([#4](https://github.com/didit-protocol/sdk-dotnet/issues/4)) ([e30816d](https://github.com/didit-protocol/sdk-dotnet/commit/e30816d6852c88b4389a8c3762b938662e5e858f))
* **spec:** sync API spec 2026-10-27 ([#9](https://github.com/didit-protocol/sdk-dotnet/issues/9)) ([a520656](https://github.com/didit-protocol/sdk-dotnet/commit/a5206569c03f854d5d95d443a9ca52b2eab8a5f7))


### Dependencies

* Bump the test group with 2 updates ([#2](https://github.com/didit-protocol/sdk-dotnet/issues/2)) ([300b1c8](https://github.com/didit-protocol/sdk-dotnet/commit/300b1c8ecc2550d02799f056a04dc6e9dcde4826))
