using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace H1Emu_Launcher.JsonEndPoints
{
    public class AssetPackJson
    {
        public class Asset
        {
            public string version { get; set; }
            public string filename { get; set; }
            public string url { get; set; }
            public string hash { get; set; }

            // Destination folder relative to the game directory. Empty keeps the
            // original behaviour and installs the file into Resources\Assets.
            public string path { get; set; }

            // When true the downloaded file is treated as a zip archive and
            // extracted into the destination folder instead of being installed as-is.
            public bool extract { get; set; }
        }

        public class Root
        {
            public List<Asset> assets { get; set; }
        }
    }
}
