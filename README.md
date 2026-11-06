# Sailwind Drop Safety

Stop dropping things by accident. Your pick up/interact button no longer lets go of what you're holding.

## Install

### Mod managers

Install Sailwind Drop Safety through [Sailwind Mod Synchronizer](https://github.com/foxyv/SailwindModSynchronizer/releases/latest), r2modman or Thunderstore Mod Manager, then launch Sailwind through your manager. Dependencies are installed automatically.

### Manual installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) in your Sailwind game folder, following its installation instructions.
2. Download and extract the [Sailwind Drop Safety ZIP](https://github.com/Skeptic043/Sailwind-Drop-Safety/releases/latest). Copy its `BepInEx/plugins/DropSafety` folder into `BepInEx/plugins` in your Sailwind folder.
3. Launch Sailwind normally.

## How it works

By default, Drop Safety prevents the pick up/interact button (left-click or F) from dropping what you're holding. The Throw key (T) works normally: tap it to drop an item or hold it to throw. Enable `RequireModifier` to allow dropping with the pick up/interact button while holding your chosen modifier key. Placing items on tables, shelves and hooks works normally. `InventoryItemsOnly` can be enabled for Drop Safety to only apply to small items that fit in your inventory.

## Configuration

Settings are in `BepInEx/config/com.skeptic043.sailwind.dropsafety.cfg` after the first launch, or in game with the BepInEx 5 version of [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager).

| Setting | Default | Options |
| --- | --- | --- |
| `DisableDrop` | `true` | Prevents the pick up/interact button from dropping what you're holding. |
| `RequireModifier` | `false` | Pick up/interact button only drops held item while `ModifierKey` is held. Works with `DisableDrop` enabled. |
| `ModifierKey` | `LeftAlt` | The key to hold when `RequireModifier` is on. |
| `InventoryItemsOnly` | `false` | Only apply drop protection to items that fit in your inventory. |

What happens when you attempt to drop a protected item with the pick up/interact button, outside a valid placement:

| `DisableDrop` | `RequireModifier` | Result |
| --- | --- | --- |
| `true` | `false` | Never drops (default) |
| either | `true` | Drops only while `ModifierKey` is held |
| `false` | `false` | Drops like the base game |

Type `ModifierKey` as a single key name, such as `LeftAlt`, `RightControl`, `Mouse3` or `JoystickButton4`. Key names ignore capitalization and spaces, so `left alt` works too. Leave it blank or use `None` to turn it off. Unsupported values stay in the file and produce a warning in the log.

## Compatibility

No known incompatibilities.

## AI Use

AI was used to write all of the code in this project. The original concept, design direction, testing, debugging, and release decisions are my own. If you prefer not to use mods developed with AI assistance, I understand and respect that choice.

## Links

[Report an issue](https://github.com/Skeptic043/Sailwind-Drop-Safety/issues) · [Source code](https://github.com/Skeptic043/Sailwind-Drop-Safety) · [Release notes](https://github.com/Skeptic043/Sailwind-Drop-Safety/blob/main/CHANGELOG.md) · [Build instructions](https://github.com/Skeptic043/Sailwind-Drop-Safety/blob/main/BUILDING.md) · [License](https://github.com/Skeptic043/Sailwind-Drop-Safety/blob/main/LICENSE) · [Support on Ko-fi](https://ko-fi.com/skeptic043) · skeptic043
