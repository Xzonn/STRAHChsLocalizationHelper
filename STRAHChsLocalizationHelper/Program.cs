﻿using AssetStudio;
using BundleHelper;
using Ionic.Zip;
using LibCPK;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace Helper
{
    internal class Program
    {
        static void Main(string[] args)
        {
            ExtractFiles();
            PatchAsset();
            PatchBundle();
            PatchPak();
            CreateRomfsFolder();
        }

        static void ExtractFiles()
        {
            if (!File.Exists("files/level1"))
            {
                using ZipFile archive = new("files/files.zip");
                archive.Password = "hogehoge66";
                archive.Encryption = EncryptionAlgorithm.PkzipWeak;
                archive.StatusMessageTextWriter = Console.Out;
                archive.ExtractAll("files", ExtractExistingFileAction.OverwriteSilently);
            }
        }

        static void PatchAsset()
        {
            Logger.Default = new LogHelper();

            string[] FILE_NAMES =
            [
                "level1",
                "vridge.unity3d",

                "adv2.unity3d",
                "dataselect.unity3d",
                "omkalb.unity3d",
                "title.unity3d",
            ];
            string[] CLASS_FOR_EXPORT =
            [
                "AppGameDataTipsData",
                "FlowChartData",
                "TextFlyMoveData",

                "Text",
            ];

            AssetsManager manager = new()
            {
                SpecifyUnityVersion = "2020.3.37f1"
            };

            manager.LoadFiles(FILE_NAMES.Select(x => $"files/Switch/{x}").ToArray());
            if (!Directory.Exists("json")) { Directory.CreateDirectory("json"); }
            Directory.CreateDirectory("out");

            Dictionary<string, string> textTranslations = [];
            if (File.Exists("texts/zh_Hans/Text.json"))
            {
                textTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("texts/zh_Hans/Text.json"))!;
            }

            foreach (var assetsFile in manager.assetsFileList)
            {
                var replaceStreams = new Dictionary<long, Stream> { };
                foreach (var @object in assetsFile.Objects)
                {
                    if (@object is not MonoBehaviour m_MonoBehaviour || !m_MonoBehaviour.m_Script.TryGet(out var m_Script)) { continue; }
                    var m_ClassName = m_Script.m_ClassName;
                    if (!CLASS_FOR_EXPORT.Contains(m_ClassName)) { continue; }
                    var m_Type = m_MonoBehaviour.serializedType?.m_Type;
                    if (m_Type == null)
                    {
                        using var fs = File.OpenRead($"files/TypeTree/{m_ClassName}.bin");
                        m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
                    }
                    var type = m_MonoBehaviour.ToType(m_Type);

                    if (m_ClassName == "Text")
                    {
                        string text = (string)type["m_Text"]!;
                        if (textTranslations.TryGetValue(text, out var translation))
                        {
                            if (translation != text)
                            {
                                type["m_Text"] = translation;
                                MemoryStream memoryStream = new();
                                BinaryWriter bw = new(memoryStream);
                                TypeTreeHelper.WriteType(type, m_Type, bw);
                                replaceStreams[m_MonoBehaviour.m_PathID] = memoryStream;
                                Console.WriteLine($"Replacing: {m_MonoBehaviour.assetsFile.fileName}/{m_ClassName} ({m_MonoBehaviour.m_PathID})");
                            }
                        }
                        else
                        {
                            textTranslations[text] = text;
                        }
                    }
                    else
                    {
                        if (!File.Exists($"texts/zh_Hans/{m_ClassName}.json"))
                        {
                            string json = JsonConvert.SerializeObject(type, Formatting.Indented);
                            File.WriteAllText($"texts/zh_Hans/{m_Script.m_ClassName}.json", json);
                            Console.WriteLine($"Extracting: {m_MonoBehaviour.assetsFile.fileName}/{m_ClassName}");
                        }
                        else
                        {
                            string json = File.ReadAllText($"texts/zh_Hans/{m_Script.m_ClassName}.json");
                            var jObject = JsonConvert.DeserializeObject<JObject>(json);
                            type = JsonHelper.ReadType(m_Type, jObject);
                            MemoryStream memoryStream = new();
                            BinaryWriter bw = new(memoryStream);
                            TypeTreeHelper.WriteType(type, m_Type, bw);
                            replaceStreams[m_MonoBehaviour.m_PathID] = memoryStream;
                            Console.WriteLine($"Replacing: {m_ClassName}");
                        }
                    }
                }
                if (replaceStreams.Count > 0)
                {
                    Console.WriteLine($"Saving: {assetsFile.fileName}");
                    assetsFile.SaveAs($"out/{assetsFile.fileName}", replaceStreams);
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

        static void PatchBundle()
        {
            string[] FILE_NAMES =
            [
                "vridge.unity3d",

                "adv2.unity3d",
                "dataselect.unity3d",
                "omkalb.unity3d",
                "title.unity3d",
            ];

            foreach (string fileName in FILE_NAMES)
            {
                var reader = new BundleHelper.EndianBinaryReader(File.OpenRead($"files/Switch/{fileName}"));
                Bundle bundleData = new(reader);

                reader.Close();

                bool changed = false;
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{file.fileName}"))
                    {
                        file.stream = File.OpenRead($"out/{file.fileName}");
                        changed = true;
                    }
                }
                if (!changed)
                {
                    continue;
                }

                Console.WriteLine($"Writing: {fileName}");
                var writer = new BundleHelper.EndianBinaryWriter(File.Create($"out/{fileName}"));
                bundleData.DumpRaw(writer);

                writer.Close();
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{file.fileName}"))
                    {
                        File.Delete($"out/{file.fileName}");
                    }
                }
            }
        }

        static void PatchPak()
        {
            var writer = new XorWriter();
            foreach (var fileName in Directory.GetFiles("texts/zh_Hans/scrpt.cpk", "*.json"))
            {
                var rawName = Path.GetFileNameWithoutExtension(fileName);
                Console.WriteLine($"Writing: {rawName}");
                writer.Write(fileName, Path.Combine("out", rawName));
            }
            var cpk = new CPK();
            cpk.ReadCPK("files/Switch/scrpt.cpk");
            var batch_file_list = new Dictionary<string, string>();
            var patch = new PatchCPK(cpk, Path.GetFullPath("files/Switch/scrpt.cpk"));
            patch.SetListener(null, Console.WriteLine, null);
            foreach (var file in cpk.fileTable)
            {
                if (File.Exists($"out/{file.FileName}"))
                {
                    batch_file_list[$"/{file.FileName}"] = Path.GetFullPath($"out/{file.FileName}");
                }
            }
            patch.Patch("out/scrpt.cpk", true, batch_file_list);
            foreach (var fileName in Directory.GetFiles("texts/zh_Hans/scrpt.cpk", "*.json"))
            {
                var rawName = Path.GetFileNameWithoutExtension(fileName);
                File.Delete(Path.Combine("out", rawName));
            }
        }

        static void CreateRomfsFolder()
        {
            Directory.CreateDirectory("out/01005940182ec000/romfs/Data/StreamingAssets/Switch/AssetBundles/data/");
            Copy("out/level1",         "out/01005940182ec000/romfs/Data/level1");
            Copy("out/scrpt.cpk",      "out/01005940182ec000/romfs/Data/StreamingAssets/scrpt.cpk");
            Copy("out/vridge.unity3d", "out/01005940182ec000/romfs/Data/StreamingAssets/Switch/AssetBundles/data/vridge.unity3d");
            Directory.CreateDirectory("out/01005940182ec000/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/");
            Copy("out/adv2.unity3d",       "out/01005940182ec000/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/adv2.unity3d");
            Copy("out/dataselect.unity3d", "out/01005940182ec000/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/dataselect.unity3d");
            Copy("out/omkalb.unity3d",     "out/01005940182ec000/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/omkalb.unity3d");
            Copy("out/title.unity3d",      "out/01005940182ec000/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/title.unity3d");
        }

        static void Copy(string source, string destination)
        {
            if (!File.Exists(source)) return;
            File.Move(source, destination, true);
        }
    }
}