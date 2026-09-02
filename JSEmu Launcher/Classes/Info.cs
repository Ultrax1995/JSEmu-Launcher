using System;

namespace H1Emu_Launcher.Classes
{
    class Info
    {
        public static string APPLICATION_DATA_PATH = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        public static string NODEJS_VERSION = "22.9.0";

        public static string H1EMU_SERVER_IP = "loginserver.jsemu.eu:1115";

        public static string H1EMU_OFFICIAL_SERVER_IP = "loginserver.h1emu.com:1115";
        public static string SERVER_JSON_API = "https://api.github.com/repos/QuentinGruber/h1z1-server/releases/latest";

        // JSEmu launcher auto-update endpoint.
        public static string LAUNCHER_JSON_API = "https://api.github.com/repos/Ultrax1995/JSEmu-Launcher/releases/latest";

        // 0 = JSEmu.eu - Assets Pack
        // v2 also lists patch files, which need the path/extract fields to be installed correctly
        public static string OFFICIAL_ASSET_PACK = "https://assets.h1emukrakow.eu/feed/v2";

        // 1 = H1Emu.com - Assets Pack
        public static string H1EMU_ASSET_PACK = "https://raw.githubusercontent.com/H1emu/asset-pack/refs/heads/main/feed.json";

        public static string SERVER_BUG_LINK = "https://github.com/QuentinGruber/h1z1-server/issues/new?assignees=&labels=bug&template=bug_report.md&title=";

        public static string LAUNCHER_BUG_LINK = "https://github.com/Ultrax1995/JSEmu-Launcher/issues/new";

        public static string ACCOUNT_KEY_CHECK_API = "https://jsemu.eu/";
        public static string JSEMU_ACCOUNT_KEY_CHECK_API = "https://jsemu.eu/";

        public static string H1EMU_ACCOUNT_KEY_CHECK_API = "http://loginserver.h1emu.com/isverified?authKey=";

        public static string DISCORD_LINK = "https://discord.gg/bmRndA6FSG";

        public static string DISCORD_UPDATES_LINK = "https://discord.com/channels/1416363907336896544/1425465707339972629";

        public static string CHANGELOG = "https://discord.com/channels/1416363907336896544/1425465707339972629";
        public static string WEBSITE = "https://jsemu.eu/";

        public static string GAME_CRASH_URL = "https://jsemu.eu/";

        public static string H1EMU_CHINESE_LINK = "https://jsemu.eu/";

        public static string ALLOWED_ACCOUNT_KEY_CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        public static int TOS_ITERATION = 3;
    }
}
