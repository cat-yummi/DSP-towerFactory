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
  * Set slot 1 (Storage) and slot 2, leave the rest empty, then click the "Fill Ingredients" button at the top right of the station window.
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

## Dyson sphere (v1.2.0)

* **Ejector tower**: Slot 1 ejector(s) (Storage), slot 2 **solar sails**. Builds **cell points** on the shell directly (like ray-receiver absorption, stat 11903). No sail bullets, no Dyson cloud. Each node must have **30/30 structure points** (`sp == spMax`) before cell work on that node.
* **Silo tower**: Slot 1 silo(s), slot 2 **small carrier rockets**. Builds **structure points** directly (like rocket arrival, stat 11902).
* **Swarm cap**: Dyson cloud sail count is hard-capped at **10 000** (vanilla can go much higher).

## Matrix research tower (v1.2.0)

* Slot 1 matrix lab(s) (Storage). Put **research matrices (6001–6006) from slot 2 onward** (slot 2 may be Supply). Slot 2 must **not** be a recipe “product” slot.
* Feeds the **head of the UI research queue** (`currentTech`).
* Tech that needs **only universe matrix (6006)**: **1 matrix per lab per second** (hash follows vanilla ratios).
* Other lab techs: when the station holds **enough matrices for the remaining progress** on the current level, consumes them and completes that level in one go (same matrix counts as the tech UI).

## Tech unlock gifts (v1.1.2+)

* First unlock of Electromagnetics **1001**, Basic assembling **1201**, Electromagnetic matrix **1002**, Automatic metallurgy **1401**, or Basic logistics **1601** grants **2 planetary logistics stations (2103)** to inventory only (does not unlock logistics tech).

## Notes

* Multiplayer is untested.
* The button and its popup tips follow the game language (Chinese for zh-CN, English otherwise). Log messages are Chinese only.
* Stations are re-checked as tower factories when a save finishes loading.

## CREDITS

* [Dyson Sphere Program](https://store.steampowered.com/app/1366540): The great game
* [BepInEx](https://bepinex.dev/): Base modding framework

## Author

* cat yummi

</details>

<details>
<summary>中文读我</summary>

***轮椅，让运输塔也能生产物资***

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

## 戴森球（1.2.0）

* **弹射器塔厂**：第一格弹射器（仓储），第二格**太阳帆**。直接增加壳面**细胞点数**（等同射线吸收完成，统计 11903），不发射帆弹、不进戴森云。每个顶点须先满 **30 结构点**（`sp == spMax`）才能在该节点铺细胞点。
* **发射井塔厂**：第一格发射井，第二格**小运载火箭**。直接增加**结构点数**（等同火箭抵达，统计 11902）。
* **戴森云上限**：全星系戴森云太阳帆数量硬顶 **10000**。

## 矩阵研究塔（1.2.0）

* 第一格矩阵研究站（仓储）；**第二格起**放研究矩阵（6001–6006），第二格可设供应。第二格不能是「产矩阵配方」的产品格。
* 为 **科技树 UI 研究队列队首** 科技供料。
* **纯宇宙矩阵**科技：每秒每座研究站消耗 **1 个宇宙矩阵（6006）**，哈希增量按原版比例。
* 其它矩阵科技：塔内矩阵 **≥ 完成当前等级剩余进度所需** 时，扣料并一次完成该等级（数量与科技界面一致）。

## 科技解锁送塔（1.1.2 起）

* 首次解锁电磁学 **1001**、基础制造 **1201**、电磁矩阵 **1002**、自动化冶金 **1401**、基础物流 **1601** 时，背包获得 **2 个行星内物流运输塔（2103）**（不解锁物流科技）。

## 注意事项

* 未在多人游戏中测试。
* 进档完成后会重新识别所有运输塔是否为塔厂。

## 鸣谢

* [戴森球计划](https://store.steampowered.com/app/1366540): 伟大的游戏
* [BepInEx](https://bepinex.dev/): 基础模组框架

## 制作人

* 流浪法师.悠米

</details>
