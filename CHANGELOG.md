# Changelog

## [1.19.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.18.0...v1.19.0) (2026-11-14)


### Features

* **spec:** sync API spec 2026-11-14 ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))


### API changes that can break typed code

* **spec:** `GET /v3/travel-rule/networks/` response `networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/networks/` response `networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/settings/` response `networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `GET /v3/travel-rule/workspace/` response `settings.networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/billing/` response `settings.networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/claim/` response `settings.networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/confirm/` response `settings.networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `POST /v3/travel-rule/workspace/domain/dns/` response `settings.networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.gtr.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.gtr.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.gtr.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.gtr.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.gtr.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.gtr.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.sygna.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.sygna.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.sygna.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.sygna.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.sygna.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.sygna.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.trust.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.trust.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.trust.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.trust.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.trust.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.trust.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.verifyvasp.connected`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.verifyvasp.platform_available`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.verifyvasp.reason`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.verifyvasp.routed_30d`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.verifyvasp.state`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))
* **spec:** `PUT /v3/travel-rule/settings/` response `networks.verifyvasp.vasps_reachable`: can now be null ([11c6a35](https://github.com/didit-protocol/sdk-dotnet/commit/11c6a35437e368051c7b418f0435a7efebf2bf82))

## [1.18.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.17.0...v1.18.0) (2026-11-13)


### Features

* **spec:** sync API spec 2026-11-13 ([f7ec355](https://github.com/didit-protocol/sdk-dotnet/commit/f7ec3559cf9a1fc0201a21b6bbfbf24a3e0e25ee))

## [1.17.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.16.0...v1.17.0) (2026-11-13)


### Features

* **spec:** sync API spec 2026-11-13 ([381d3d9](https://github.com/didit-protocol/sdk-dotnet/commit/381d3d96711242ea67a182617665e0925f75e46c))

## [1.16.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.15.0...v1.16.0) (2026-11-12)


### Features

* **spec:** sync API spec 2026-11-12 ([7616a1c](https://github.com/didit-protocol/sdk-dotnet/commit/7616a1c326c6ae4de7f7057a5228ee6081796803))

## [1.15.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.14.2...v1.15.0) (2026-11-12)


### Features

* **spec:** sync API spec 2026-11-12 ([ac5c29a](https://github.com/didit-protocol/sdk-dotnet/commit/ac5c29aeaaeba9315df99f220f75fc32fdd614c8))

## [1.14.2](https://github.com/didit-protocol/sdk-dotnet/compare/v1.14.1...v1.14.2) (2026-11-12)


### Bug Fixes

* **authentication:** a failure to encode a consent request has a fixed message ([ca9b5dd](https://github.com/didit-protocol/sdk-dotnet/commit/ca9b5dd6b2146717abedde58c2641a7efb5105a6))
* **authentication:** a malformed 2xx consent error keeps no parser exception as its cause ([89b4d24](https://github.com/didit-protocol/sdk-dotnet/commit/89b4d2484f78730feaa333e3ba08d276acaadbc9))
* **authentication:** read consent secrets from the bytes that were sent ([13b39fb](https://github.com/didit-protocol/sdk-dotnet/commit/13b39fbe44bf2844cb660caa82ae96d1b758073f))
* **authentication:** redact the parsed agent-consent response structurally ([497d86b](https://github.com/didit-protocol/sdk-dotnet/commit/497d86b0c7478f6969aac2ce91bfe969658c88b3))
* **authentication:** redact the parsed agent-consent response, not its raw text ([32085e6](https://github.com/didit-protocol/sdk-dotnet/commit/32085e6fee65b5ad95ef06930b125b73d43520b2))
* **authentication:** sanitize the whole consent response once on receipt; deep bodies are not JSON ([f750a75](https://github.com/didit-protocol/sdk-dotnet/commit/f750a758d2425977e4e1af234af044e645662055))
* **authentication:** successful consent responses reach the caller unchanged; fold header-name case ([873b462](https://github.com/didit-protocol/sdk-dotnet/commit/873b462ca9006a50e97ad48cd03618aee65d3061))
* **authentication:** the approval-options call encodes through the same fixed-message wrapper ([ef8e88e](https://github.com/didit-protocol/sdk-dotnet/commit/ef8e88eec7c7c5a5341d73b89216be4b8aff6350))
* **authentication:** treat only assertion strings of 16+ characters as secrets, read from the wire JSON ([cf1a34a](https://github.com/didit-protocol/sdk-dotnet/commit/cf1a34a0178636c141337ecd27420104367acfd5))
* **authentication:** withhold transport exceptions, redact empty bodies, reason phrase and request id, keep safe key names ([c31380e](https://github.com/didit-protocol/sdk-dotnet/commit/c31380e57f99a1273689413b75460a839eabd289))


### Documentation

* **authentication:** cite why the secret length minimum is 16 ([e3ed1ea](https://github.com/didit-protocol/sdk-dotnet/commit/e3ed1ea9ad781a234e9cdfa2362bd18018bc29ee))

## [1.14.1](https://github.com/didit-protocol/sdk-dotnet/compare/v1.14.0...v1.14.1) (2026-11-11)


### Bug Fixes

* **authentication:** redact every string of the consent assertion from errors ([c1cb8ec](https://github.com/didit-protocol/sdk-dotnet/commit/c1cb8ec50b13a313821cd7d7185b3dab6a5c408b))

## [1.14.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.13.0...v1.14.0) (2026-11-11)


### Features

* **spec:** sync API spec 2026-11-11 ([073ba9c](https://github.com/didit-protocol/sdk-dotnet/commit/073ba9c7e2956c56f47b9fd32d9b60ad526d3a42))


### API changes that can break typed code

* **spec:** `POST /v3/organization/{organization_id}/application/{application_id}/cases/` request `links[].entity_type`: now required ([073ba9c](https://github.com/didit-protocol/sdk-dotnet/commit/073ba9c7e2956c56f47b9fd32d9b60ad526d3a42))
* **spec:** `POST /v3/organization/{organization_id}/application/{application_id}/cases/` request `links[].entity_uuid`: now required ([073ba9c](https://github.com/didit-protocol/sdk-dotnet/commit/073ba9c7e2956c56f47b9fd32d9b60ad526d3a42))
* **spec:** `POST /v3/organization/{organization_id}/application/{application_id}/cases/blueprints/{blueprint_uuid}/publish/` response `content_config`: type changed from any to object ([073ba9c](https://github.com/didit-protocol/sdk-dotnet/commit/073ba9c7e2956c56f47b9fd32d9b60ad526d3a42))

## [1.13.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.12.0...v1.13.0) (2026-11-11)


### Features

* **spec:** sync API spec 2026-11-11 ([364bb2c](https://github.com/didit-protocol/sdk-dotnet/commit/364bb2c26457b570920752e20b0b54bc3f92d647))

## [1.12.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.11.0...v1.12.0) (2026-11-11)


### Features

* **spec:** sync API spec 2026-11-11 ([ea61297](https://github.com/didit-protocol/sdk-dotnet/commit/ea61297830829d165e8e5f58ff2587766ccc6975))

## [1.11.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.10.0...v1.11.0) (2026-11-11)


### Features

* **spec:** sync API spec 2026-11-11 ([dd09c57](https://github.com/didit-protocol/sdk-dotnet/commit/dd09c57a0e0367caa607c0e7dddc59b1fcd1c67a))
* **spec:** sync API spec 2026-11-11 ([98aaeab](https://github.com/didit-protocol/sdk-dotnet/commit/98aaeab8a4809bee89836fb76565a45d1a61ada9))
* **spec:** sync API spec 2026-11-11 ([de96d57](https://github.com/didit-protocol/sdk-dotnet/commit/de96d573aea9bc3dfceb550dd1247acd01cee476))
* **spec:** sync API spec 2026-11-11 ([03d0d92](https://github.com/didit-protocol/sdk-dotnet/commit/03d0d9206522fa08e9e27131d4f73897553e7dd5))
* **spec:** sync API spec 2026-11-11 ([862da6b](https://github.com/didit-protocol/sdk-dotnet/commit/862da6b8bf89a4000962c2a4327e743b27789793))
* **spec:** sync API spec 2026-11-11 ([5a6301c](https://github.com/didit-protocol/sdk-dotnet/commit/5a6301cf92774236868e5bb6e5bb6191e9b24d43))
* **spec:** sync API spec 2026-11-11 ([7e62785](https://github.com/didit-protocol/sdk-dotnet/commit/7e62785000e7edcab8c740a0e9149394feb50e3d))

## [1.10.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.9.0...v1.10.0) (2026-11-11)


### Features

* **spec:** sync API spec 2026-11-10 ([4a37c0b](https://github.com/didit-protocol/sdk-dotnet/commit/4a37c0ba3fc36525982e109b627addadadb1effa))
* **spec:** sync API spec 2026-11-11 ([27dfd60](https://github.com/didit-protocol/sdk-dotnet/commit/27dfd60b33f753facfb56eaee8eec0a7a3aa5259))

## [1.9.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.8.0...v1.9.0) (2026-11-10)


### Features

* **spec:** sync API spec 2026-11-10 ([762a7c2](https://github.com/didit-protocol/sdk-dotnet/commit/762a7c2e42f23662db1707fa28ab3b51d9aabe27))

## [1.8.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.7.0...v1.8.0) (2026-11-10)


### Features

* **spec:** sync API spec 2026-11-10 ([8fc5020](https://github.com/didit-protocol/sdk-dotnet/commit/8fc5020c46e027ef2c893819037875ab9a30d40f))

## [1.7.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.6.0...v1.7.0) (2026-11-10)


### Features

* **spec:** sync API spec 2026-11-10 ([4a7538e](https://github.com/didit-protocol/sdk-dotnet/commit/4a7538e80bafe6d11c476b52e65998e42b823a2c))

## [1.6.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.5.0...v1.6.0) (2026-11-10)


### Features

* **spec:** sync API spec 2026-11-10 ([50818ed](https://github.com/didit-protocol/sdk-dotnet/commit/50818ed15f0c2e111490ca34992210b9b9f6083a))

## [1.5.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.4.0...v1.5.0) (2026-11-10)


### Features

* **spec:** sync API spec 2026-11-10 ([677706f](https://github.com/didit-protocol/sdk-dotnet/commit/677706f7ff996a046161859861b9ab2ab13405e6))

## [1.4.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.3.1...v1.4.0) (2026-11-09)


### Features

* **spec:** sync API spec 2026-11-09 ([13b4a15](https://github.com/didit-protocol/sdk-dotnet/commit/13b4a15322a6a511740ee2cbe10222597a42cd4d))

## [1.3.1](https://github.com/didit-protocol/sdk-dotnet/compare/v1.3.0...v1.3.1) (2026-11-08)


### Documentation

* DESIGN.md, CLAUDE.md and AGENTS.md describe the automatic merge and publish ([#30](https://github.com/didit-protocol/sdk-dotnet/issues/30)) ([908f0e9](https://github.com/didit-protocol/sdk-dotnet/commit/908f0e938bb112efea4f50b60759bcbf77e08d5f))

## [1.3.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.2.0...v1.3.0) (2026-11-07)


### Features

* **spec:** sync API spec 2026-11-07 ([9a09ad9](https://github.com/didit-protocol/sdk-dotnet/commit/9a09ad9a176a2c062b6dafdbd63f04b7ce63a40e))

## [1.2.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.1.0...v1.2.0) (2026-11-07)


### Features

* **spec:** sync API spec 2026-11-07 ([b1eb48e](https://github.com/didit-protocol/sdk-dotnet/commit/b1eb48e27d74e1d45f2059ab94a762f14b8a7846))

## [1.1.0](https://github.com/didit-protocol/sdk-dotnet/compare/v1.0.1...v1.1.0) (2026-11-05)


### Features

* **spec:** sync API spec 2026-11-05 (application data location) ([1615188](https://github.com/didit-protocol/sdk-dotnet/commit/1615188055e0c03627158416b26d1d0b75cea669))

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
