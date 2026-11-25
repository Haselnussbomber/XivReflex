# Changelog

## [v1.0.1] (2026-11-25)

This is just a small release for some code maintenance.

- Updated dependencies.
- Reworked hooks to use addresses from ClientStructs.
- The initialization function now uses GetProcAddress to resolve nvapi_QueryInterface instead of scanning for a reference to the function within the game.
- PCLStats are now initialized before enabling hooks.
- Added a LICENSES.md file for the MIT license of NVAPI.

## [v1.0.0] (2026-08-25)

First release. 🥳

[v1.0.0]: https://github.com/Haselnussbomber/XivReflex
[v1.0.1]: https://github.com/Haselnussbomber/XivReflex/compare/v1.0.0...v1.0.1
