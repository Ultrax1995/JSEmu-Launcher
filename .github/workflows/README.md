# JSEmu Server

Server source code used by **JSEmu.eu** for our H1Z1: Just Survive server infrastructure.

This project is based on the open-source H1Z1 server emulator originally developed by **Quentin Gruber** and the **H1Emu community**, with additional systems, fixes, optimizations and gameplay features developed specifically for JSEmu.

> This repository contains the server-side source used for JSEmu development and testing.

---

## About JSEmu

**JSEmu** is a community-driven H1Z1: Just Survive project focused on preserving and expanding the classic survival experience.

Our goal is to keep the original feel of Just Survive while improving server stability, fixing unfinished systems and introducing features that fit naturally into the game.

Website / Launcher:

https://github.com/Ultrax1995/JSEmu-Launcher

---

## Features

JSEmu includes numerous changes and additions on top of the original emulator, including:

- Custom server infrastructure
- Improved world persistence
- Construction system fixes
- Sleeping Mat respawn system
- Sleeping Mat cooldowns and map integration
- Group members visible on the map
- Custom items and backpacks
- Additional weapon skins
- Skin repainting support
- Custom loot tables
- Double Loot events
- Vehicle scrap event integration
- Improved landmine detection
- Construction placement improvements
- Ground Tiller improvements
- Fertilizer loot adjustments
- Weapon Locker loot
- Custom commands
- Discord integrations
- Server events
- FairPlay / anti-cheat integrations
- Performance and stability improvements
- Numerous bug fixes

The server is continuously developed and individual systems may change over time.

---

## Project Structure

```text
src/
├── clients/
├── packets/
├── protocols/
├── servers/
│   ├── LoginServer/
│   ├── ZoneServer2016/
│   ├── GatewayServer/
│   └── LoginZoneConnection/
├── types/
└── utils/