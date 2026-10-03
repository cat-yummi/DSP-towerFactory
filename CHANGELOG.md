# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.1.2] - 2026-10-04

### Added

- Unlocking **Electromagnetism** (tech 1001) grants **3 planetary logistics stations** (item 2103) to your inventory. Logistics tech and recipes are **not** unlocked, to avoid tech-tree issues.
- One-time grant per save (feature key `99001001`).

### 新增

- 解锁科技 **「电磁学」**（1001）时，背包获得 **3 个行星内物流运输塔**（2103）；**不**解锁物流科技与配方，避免科技树异常。
- 每个存档仅发放一次（featureKey `99001001`）。

## [1.1.1] - 2026-10-03

### Fixed / 修复

- **Self-assembler towers / 自产制造台**: With an empty slot 2, output goes to slot 1 (not the empty product slot).
- **Self-assembler towers / 自产制造台**: Production works with **0** assemblers in slot 1 at the minimum **100-building** rate.
- **Auto-fill / 自动填原料**: Empty slot 2 on assemblers; UI refresh after fill; `SetStationStorage` uses the main player.

### Build / 构建

- Release builds automatically produce `bin/Release/TowerFactory.zip`.

## [1.1.0] - 2026-09-30

### Added / 新增

- Thunderstore dependency **BigTower 1.1.0**.
- Bilingual README, author credit, composite icon.
- Fill-ingredients button and tips: English by default, Chinese on zh-CN.

### Changed / 变更

- Chinese README tagline adds「轮椅」.

## [1.0.0] - 2026-09-30

### Added / 新增

- **Tower Factory** core: storage building in slot 1, product in slot 2, ingredients after; 1 s activation; 1/10 cycle time; `ceil(N/10)` crafts per cycle; no power; ignores proliferator.
- Production, pseudo-recipes, mining, pumps, oil, photons, fractionator, energy exchanger (charge only).
- **Fill ingredients** button in the station window.
- Requires **BepInEx 5.4.17** (xiaoye97).

[1.1.2]: https://github.com/cat-yummi/DSP-towerFactory/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/cat-yummi/DSP-towerFactory/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/cat-yummi/DSP-towerFactory/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/cat-yummi/DSP-towerFactory/releases/tag/v1.0.0
