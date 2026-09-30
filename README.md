# TowerFactory

<details>
<summary>Read me in English</summary>

***Turn logistics stations into factories***

## Overview

* Put a production building in slot 1 of a logistics station and set that slot to **Storage** (neither Supply nor Demand). Put what you want to make in slot 2, and the ingredients after it.
* About 1 second after the setup is complete, the station becomes a "Tower Factory". It works as if the buildings stored in slot 1 were running inside the station: N buildings run at N times the base speed.
* Each base cycle is split into 10 shorter cycles, and each short cycle produces ceil(N / 10) times.
* Tower Factories do not use power (critical photon towers use surplus Dyson sphere energy instead).
* Proliferator effects are ignored.
* If a station doesn't work as expected, `BepInEx/LogOutput.log` tells you why it was or wasn't recognized.

## Production

* Works with every building that has recipes: assemblers, smelters, chemical plants, oil refineries, particle colliders and matrix labs.
* Slot 2 is the product; slots 3 and later are the ingredients, in recipe order.
* Byproducts:
  * Only the product in slot 2 is stored. Byproducts are discarded, but still counted as produced in the statistics panel.
  * Byproducts never block the main product.
* Auto-fill ingredients:
  * Set slot 1 (Storage) and slot 2, leave the rest empty, then click the "塔厂：填原料" button at the top right of the station window.
  * Ingredients are set to Demand. Pseudo recipes take priority.
  * If no recipe fits, you'll see "原料格子不够，请加装运输塔扩容mod" (not enough slots, install a station expansion mod).
* Pseudo recipes (several real recipes merged into one; every intermediate step is counted in the statistics):
  * Oil Refinery: 4 Crude Oil + 2 Energetic Graphite → 3 Energy Matrix
  * Oil Refinery: 1 Refined Oil → 1 Hydrogen + 1 Energetic Graphite (put either one in slot 2)
  * Oil Refinery: 2 Crude Oil + 1 Coal → 3 Refined Oil
  * Assembler: Titanium Crystal / Optical Grating Crystal + Fire Ice + Hydrogen → Casimir Crystal

## Mining

* Mining Machine: slot 2 is a mineral found on this planet. 30 per minute per machine, multiplied by mining speed research.
* Advanced Mining Machine: produces every mineral from slot 2 onwards, each at 3 times the Mining Machine speed. Slots with minerals not found on this planet are skipped.
* Water Pump: slot 2 is this planet's ocean item (water or sulfuric acid). Uses the real pump speed.
* Oil Extractor: slot 2 is crude oil, and the planet must have oil seeps. 1 per second per extractor, multiplied by mining speed research.
* Veins are never depleted.

## Critical Photons

* Slot 1 is a Ray Receiver, slot 2 is Critical Photon.
* Each receiver makes photons at the speed of one real receiver at full warm-up without a lens.
* Energy is taken from the Dyson sphere's surplus (whatever real receivers leave). All Tower Factories share it; production stops when there isn't enough.

## Fractionator

* Slot 1 is a Fractionator, slot 2 is Deuterium, slot 3 is Hydrogen.
* Each fractionator converts 0.1 Hydrogen into Deuterium every half second.

## Accumulators

* Slot 1 is an Energy Exchanger, slot 2 is a full Accumulator, slot 3 is an empty Accumulator.
* Charges empty accumulators at the real exchanger speed. Charging only; no power is used for now.

## Notes

* Multiplayer is untested.
* The button text is Chinese only.

## CREDITS

* [Dyson Sphere Program](https://store.steampowered.com/app/1366540): The great game
* [BepInEx](https://bepinex.dev/): Base modding framework

</details>

<details>
<summary>中文读我</summary>

***让运输塔也能生产物资***

## 塔厂总述

* 运输塔第一格放一种生产建筑，并设为**仓储**（不能是供应或需求）；第二格放想要的产品，后面几格放原料。
* 设置完成约 1 秒后，这座塔就成为"塔厂"，相当于第一格里的建筑在塔里干活：第一格有几座建筑，就以几倍速度生产。
* 生产周期缩短为原来的 1/10，每个周期生产"建筑数 ÷ 10（向上取整）"次。
* 塔厂不耗电（临界光子塔厂消耗的是戴森球的多余能量）。
* 忽略增产剂效果。
* 塔没按预期工作时，可以看 `BepInEx/LogOutput.log`，里面会写明这座塔为什么被识别、或为什么没被识别为塔厂。

## 生产

* 支持所有有配方的生产建筑：制造台、熔炉、化工厂、原油精炼厂、微型粒子对撞机、矩阵研究站。
* 第二格放产品，第三格起按配方顺序放原料。
* 副产物：
  * 只收第二格的产品，副产物直接丢弃，但会计入生产统计。
  * 副产物不会影响主产物的生产。
* 自动填原料：
  * 设好第一格（仓储）和第二格，后面留空，点运输塔窗口右上角的"塔厂：填原料"按钮。
  * 原料格设为需求，伪配方优先。
  * 如果格子放不下任何配方，会提示"原料格子不够，请加装运输塔扩容mod"。
* 伪配方（多步真实配方合成一步，中间步骤的生产和消耗都计入统计）：
  * 原油精炼厂：4 原油 + 2 高能石墨 → 3 能量矩阵
  * 原油精炼厂：1 精炼油 → 1 氢 + 1 高能石墨（第二格放哪个就收哪个）
  * 原油精炼厂：2 原油 + 1 煤矿 → 3 精炼油
  * 制造台：钛晶石 / 光栅石 + 可燃冰 + 氢 → 卡西米尔晶体

## 采集

* 采矿机：第二格放本行星有的矿物，每座每分钟 30 个，乘以采矿速度科技加成。
* 大型采矿机：第二格起的矿物全部生产，每格都是采矿机的 3 倍速度；本行星没有的矿会被跳过。
* 抽水机：第二格放本行星海洋里的物品（水或硫酸），按抽水机的真实速度生产。
* 原油萃取站：第二格放原油，本行星需要有油井；每座每秒 1 个，乘以采矿速度科技加成。
* 不消耗行星上的资源。

## 临界光子

* 第一格放射线接收站，第二格放临界光子。
* 每座接收站的速度，等于一座满预热、不放透镜的真实接收站。
* 能量取自戴森球的多余能量（真实接收站用剩的部分），所有塔厂共用；能量不够就不产。

## 分馏塔

* 第一格放分馏塔，第二格放重氢，第三格放氢。
* 每座分馏塔每半秒把 0.1 个氢转换为重氢。

## 蓄电器

* 第一格放能量枢纽，第二格放蓄电器（满），第三格放蓄电器（空）。
* 按能量枢纽的真实速度把空蓄电器充满。只做充电，暂时不耗电。

## 注意事项

* 未在多人游戏中测试。

## 鸣谢

* [戴森球计划](https://store.steampowered.com/app/1366540): 伟大的游戏
* [BepInEx](https://bepinex.dev/): 基础模组框架

</details>
