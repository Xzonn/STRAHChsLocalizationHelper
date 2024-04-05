using AssetStudio;
using BundleHelper;
using Ionic.Zip;
using LibCPK;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Processing;
using System.Globalization;

namespace Helper
{
    internal class Program
    {
        static readonly string[] FILE_NAMES =
            [
                "sharedassets0.assets",
                "sharedassets1.assets",
                "sharedassets2.assets",
                "level0",
                "level1",
                "level2",

                "fonts.unity3d",
                "manualp.unity3d",
                "manuals.unity3d",
                "vridge.unity3d",

                "adv2.unity3d",
                "ci.unity3d",
                "dataselect.unity3d",
                "omkalb.unity3d",
                "title.unity3d",
            ];

        static void Main(string[] args)
        {
            Logger.Default = new LogHelper();

            foreach (var platform in new string[] { "Switch" })
            {
                ExtractFiles(platform);
                PatchAsset(platform);
                PatchBundle(platform);
                PatchPak(platform);

                if (platform == "Switch")
                {
                    CreateRomfsFolder();
                }
            }
        }

        static void ExtractFiles(string platform)
        {
            if (!File.Exists($"original_files/{platform}/level1"))
            {
                Directory.CreateDirectory($"original_files/{platform}/");
                using ZipFile archive = new($"original_files/{platform}.zip")
                {
                    Password = "hogehoge66",
                    Encryption = EncryptionAlgorithm.PkzipWeak,
                    StatusMessageTextWriter = Console.Out
                };
                archive.ExtractAll($"original_files/{platform}/", ExtractExistingFileAction.OverwriteSilently);
            }
        }

        static void PatchAsset(string platform)
        {
            AssetsManager manager = new()
            {
                SpecifyUnityVersion = "2020.3.37f1"
            };

            manager.LoadFiles(FILE_NAMES.Select(x => $"original_files/{platform}/{x}").ToArray());
            Directory.CreateDirectory($"out/{platform}");

            Dictionary<string, string> textTranslations = [];
            if (File.Exists("texts/zh_Hans/Text.json"))
            {
                textTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("texts/zh_Hans/Text.json"))!;
            }

            foreach (var assetsFile in manager.assetsFileList)
            {
                var fileName = Path.GetFileNameWithoutExtension(assetsFile.fileName);
                var assetHelper = new AssetHelper();
                foreach (var @object in assetsFile.Objects)
                {
                    if (@object is MonoBehaviour m_MonoBehaviour
                        && m_MonoBehaviour.m_Script.TryGet(out var m_Script))
                    {
                        assetHelper.ReplaceMonoBehaviour(m_MonoBehaviour, m_Script, textTranslations);
                    }
                    else if ((@object is Texture2D m_Texture2D)
                        && File.Exists($"files/images/{m_Texture2D.m_Name}.png"))
                    {
                        assetHelper.ReplaceTexture(m_Texture2D);
                    }
                    else if ((@object is Sprite m_Sprite)
                        && File.Exists($"files/sprites/{m_Sprite.m_Name}.png"))
                    {
                        assetHelper.ReplaceSprite(m_Sprite);
                    }
                    else if ((@object is Font m_Font)
                        && File.Exists($"files/fonts/{m_Font.m_Name}.ttf"))
                    {
                        assetHelper.ReplaceFont(m_Font);
                    }
                }
                foreach (var (m_PathID, image) in assetHelper.ReplacedImages)
                {
                    image.Mutate(_ => _.Flip(FlipMode.Vertical));
                    assetHelper.ReplaceTexture((Texture2D)assetsFile.Objects.Where(_ => _.m_PathID == m_PathID).First());
                    image.Dispose();
                }
                if (assetHelper.ReplacedStreams.Count > 0)
                {
                    assetsFile.SaveAs($"out/{platform}/{assetsFile.fileName}", assetHelper.ReplacedStreams);
                    foreach (var (m_PathID, stream) in assetHelper.ReplacedStreams)
                    {
                        stream.Dispose();
                    }
                    Console.WriteLine($"Saved: {assetsFile.fileName}");
                }
            }

            var textTranslationsSorted = new Dictionary<string, string>();
            var textKeys = textTranslations.Keys.ToList();
            var comparer = StringComparer.Create(new CultureInfo("ja-JP"), false);
            textKeys.Sort(comparer);
            foreach (var key in textKeys)
            {
                textTranslationsSorted[key] = textTranslations[key];
            }
            File.WriteAllText("texts/zh_Hans/Text.json", JsonConvert.SerializeObject(textTranslationsSorted, Formatting.Indented));
        }

        static void PatchBundle(string platform)
        {
            foreach (string fileName in FILE_NAMES)
            {
                if (!fileName.EndsWith(".unity3d")) { continue; }
                var reader = new BundleHelper.EndianBinaryReader(File.OpenRead($"original_files/{platform}/{fileName}"));
                Bundle bundleData = new(reader);

                reader.Close();

                bool changed = false;
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{platform}/{file.fileName}"))
                    {
                        file.stream = File.OpenRead($"out/{platform}/{file.fileName}");
                        changed = true;
                    }
                }
                if (!changed)
                {
                    continue;
                }

                Console.WriteLine($"Writing: {fileName}");
                var writer = new BundleHelper.EndianBinaryWriter(File.Create($"out/{platform}/{fileName}"));
                bundleData.DumpRaw(writer);

                writer.Close();
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{platform}/{file.fileName}"))
                    {
                        File.Delete($"out/{platform}/{file.fileName}");
                    }
                }
            }
        }

        static void PatchPak(string platform)
        {
            var writer = new XorWriter();
            foreach (var fileName in Directory.GetFiles("texts/zh_Hans/scrpt.cpk", "*.json"))
            {
                var rawName = Path.GetFileNameWithoutExtension(fileName);
                Console.WriteLine($"Writing: {rawName}");
                writer.Write(fileName, $"out/{platform}/{rawName}");
            }
            var cpk = new CPK();
            cpk.ReadCPK($"original_files/{platform}/scrpt.cpk");
            var replacedFiles = new Dictionary<string, string>();
            var patch = new PatchCPK(cpk, Path.GetFullPath($"original_files/{platform}/scrpt.cpk"));
            patch.SetListener(null, Console.WriteLine, null);
            foreach (var file in cpk.fileTable)
            {
                if (File.Exists($"out/{platform}/{file.FileName}"))
                {
                    replacedFiles[$"/{file.FileName}"] = Path.GetFullPath($"out/{platform}/{file.FileName}");
                }
            }
            patch.Patch($"out/{platform}/scrpt.cpk", true, replacedFiles);
            foreach (var fileName in Directory.GetFiles("texts/zh_Hans/scrpt.cpk", "*.json"))
            {
                var rawName = Path.GetFileNameWithoutExtension(fileName);
                File.Delete($"out/{platform}/{rawName}");
            }
        }

        static void CreateRomfsFolder()
        {
            string romfsDir = "out/Switch/01005940182ec000/romfs/Data";
            Directory.CreateDirectory($"{romfsDir}/StreamingAssets/Switch/AssetBundles/data/");
            Directory.CreateDirectory($"{romfsDir}/StreamingAssets/Switch/AssetBundles/mgr/");
            Copy("out/Switch/level0",               $"{romfsDir}/level0");
            Copy("out/Switch/level1",               $"{romfsDir}/level1");
            Copy("out/Switch/level2",               $"{romfsDir}/level2");
            Copy("out/Switch/sharedassets0.assets", $"{romfsDir}/sharedassets0.assets");
            Copy("out/Switch/sharedassets1.assets", $"{romfsDir}/sharedassets1.assets");
            Copy("out/Switch/sharedassets2.assets", $"{romfsDir}/sharedassets2.assets");
            Copy("out/Switch/scrpt.cpk",            $"{romfsDir}/StreamingAssets/scrpt.cpk");
            Copy("out/Switch/fonts.unity3d",        $"{romfsDir}/StreamingAssets/Switch/AssetBundles/data/fonts.unity3d");
            Copy("out/Switch/manualp.unity3d",      $"{romfsDir}/StreamingAssets/Switch/AssetBundles/data/manualp.unity3d");
            Copy("out/Switch/manuals.unity3d",      $"{romfsDir}/StreamingAssets/Switch/AssetBundles/data/manuals.unity3d");
            Copy("out/Switch/vridge.unity3d",       $"{romfsDir}/StreamingAssets/Switch/AssetBundles/data/vridge.unity3d");
            Copy("out/Switch/adv2.unity3d",         $"{romfsDir}/StreamingAssets/Switch/AssetBundles/mgr/adv2.unity3d");
            Copy("out/Switch/ci.unity3d",           $"{romfsDir}/StreamingAssets/Switch/AssetBundles/mgr/ci.unity3d");
            Copy("out/Switch/dataselect.unity3d",   $"{romfsDir}/StreamingAssets/Switch/AssetBundles/mgr/dataselect.unity3d");
            Copy("out/Switch/omkalb.unity3d",       $"{romfsDir}/StreamingAssets/Switch/AssetBundles/mgr/omkalb.unity3d");
            Copy("out/Switch/title.unity3d",        $"{romfsDir}/StreamingAssets/Switch/AssetBundles/mgr/title.unity3d");
        }

        static void Copy(string source, string destination)
        {
            if (!File.Exists(source)) return;
            File.Move(source, destination, true);
        }
    }
}
